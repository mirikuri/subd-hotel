using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Services;

namespace QonaqHotels.Tests;

public class SiteBookingServiceTests
{
    private readonly Db _db = TestDb.Create();
    private static readonly DateOnly D = Clock.Today.AddDays(10);

    private SiteBookingService NewService() => new(_db);

    private async Task<T> ScalarAsync<T>(string sql, params (string, object)[] ps)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Submit_CreatesNewSiteBookingWithRoom()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var res = await NewService().SubmitAsync(t.GuestId, t.HotelId, t.RoomTypeId, D, D.AddDays(2), 2, "  у окна ");

        Assert.Equal(SiteBookingOutcome.Success, res.Outcome);
        Assert.Equal($"Заявка №{res.BookingId} отправлена, администратор свяжется с вами.", res.Message);
        Assert.Equal("новая|сайт|у окна", await ScalarAsync<string>(
            "SELECT status || '|' || source || '|' || guest_comment FROM bookings WHERE id = @id", ("id", res.BookingId!)));
        Assert.Equal(2, await ScalarAsync<short>(
            "SELECT guests_count FROM booking_rooms WHERE booking_id = @id AND is_active", ("id", res.BookingId!)));
    }

    [Fact]
    public async Task Submit_Concurrent_LastRoom()
    {
        await using var t = await TestHotel.CreateAsync(_db, rooms: 1);
        var other = await t.AddGuestAsync();

        var results = await Task.WhenAll(
            NewService().SubmitAsync(t.GuestId, t.HotelId, t.RoomTypeId, D, D.AddDays(3), 1, null),
            NewService().SubmitAsync(other,     t.HotelId, t.RoomTypeId, D, D.AddDays(3), 1, null));

        Assert.Single(results, r => r.Outcome == SiteBookingOutcome.Success);
        Assert.Single(results, r => r.Outcome == SiteBookingOutcome.NoRooms);
        Assert.Equal(1L, await ScalarAsync<long>(
            "SELECT count(*) FROM booking_rooms WHERE room_id = @r AND is_active", ("r", t.RoomIds[0])));
    }

    [Fact]
    public async Task Submit_Concurrent_TwoRooms()
    {
        await using var t = await TestHotel.CreateAsync(_db, rooms: 2);
        var other = await t.AddGuestAsync();

        var results = await Task.WhenAll(
            NewService().SubmitAsync(t.GuestId, t.HotelId, t.RoomTypeId, D, D.AddDays(3), 1, null),
            NewService().SubmitAsync(other,     t.HotelId, t.RoomTypeId, D, D.AddDays(3), 1, null));

        Assert.All(results, r => Assert.Equal(SiteBookingOutcome.Success, r.Outcome));
        Assert.Equal(2L, await ScalarAsync<long>(
            "SELECT count(DISTINCT room_id) FROM booking_rooms WHERE booking_id = ANY(@b)",
            ("b", results.Select(r => r.BookingId!.Value).ToArray())));
    }

    [Fact]
    public async Task Submit_FourthRequest_TooMany()
    {
        await using var t = await TestHotel.CreateAsync(_db, rooms: 4);
        var svc = NewService();
        for (var i = 0; i < 3; i++)
            Assert.Equal(SiteBookingOutcome.Success,
                (await svc.SubmitAsync(t.GuestId, t.HotelId, t.RoomTypeId, D, D.AddDays(1), 1, null)).Outcome);

        var fourth = await svc.SubmitAsync(t.GuestId, t.HotelId, t.RoomTypeId, D, D.AddDays(1), 1, null);
        Assert.Equal(SiteBookingOutcome.TooManyRequests, fourth.Outcome);
        Assert.Equal("У вас уже 3 необработанные заявки.", fourth.Message);
    }

    [Fact]
    public async Task Submit_DisabledGuest()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        await t.SetGuestActiveAsync(false);
        var res = await NewService().SubmitAsync(t.GuestId, t.HotelId, t.RoomTypeId, D, D.AddDays(1), 1, null);
        Assert.Equal(SiteBookingOutcome.GuestDisabled, res.Outcome);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM bookings WHERE guest_id = @g", ("g", t.GuestId)));
    }

    [Fact]
    public async Task Submit_InvalidDates()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var res = await NewService().SubmitAsync(t.GuestId, t.HotelId, t.RoomTypeId,
            Clock.Today.AddDays(-1), Clock.Today.AddDays(1), 1, null);
        Assert.Equal(SiteBookingOutcome.Invalid, res.Outcome);
        Assert.Equal("Нельзя бронировать прошедшие даты.", res.Message);
    }

    [Fact]
    public async Task Submit_TooLongComment_Invalid()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var res = await NewService().SubmitAsync(t.GuestId, t.HotelId, t.RoomTypeId, D, D.AddDays(1), 1, new string('x', 501));
        Assert.Equal(SiteBookingOutcome.Invalid, res.Outcome);
    }

    [Fact]
    public async Task Cancel_OwnNew_FreesRoom()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var svc = NewService();
        var res = await svc.SubmitAsync(t.GuestId, t.HotelId, t.RoomTypeId, D, D.AddDays(2), 1, null);

        Assert.True(await svc.CancelAsync(t.GuestId, res.BookingId!.Value));
        Assert.Equal("отменена|Отменено гостем", await ScalarAsync<string>(
            "SELECT status || '|' || cancel_reason FROM bookings WHERE id = @id", ("id", res.BookingId!)));

        await using var conn = await _db.OpenAsync();
        Assert.Single(await RoomSearch.SearchAsync(conn, t.CityId, D, D.AddDays(2), 1));
    }

    [Fact]
    public async Task Cancel_Foreign_False()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var other = await t.AddGuestAsync();
        var bookingId = await t.BookAsync(t.RoomIds[0], D, D.AddDays(1), guestId: other);
        Assert.False(await NewService().CancelAsync(t.GuestId, bookingId));
    }

    [Fact]
    public async Task Cancel_Confirmed_False()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var bookingId = await t.BookAsync(t.RoomIds[0], D, D.AddDays(1), status: "подтверждена");
        Assert.False(await NewService().CancelAsync(t.GuestId, bookingId));
    }

    [Fact]
    public async Task List_OnlyOwn()
    {
        await using var t = await TestHotel.CreateAsync(_db, rooms: 2);
        var other = await t.AddGuestAsync();
        var mine = await t.BookAsync(t.RoomIds[0], D, D.AddDays(1));
        await t.BookAsync(t.RoomIds[1], D, D.AddDays(1), guestId: other);

        var list = await NewService().ListAsync(t.GuestId);
        var row = Assert.Single(list);
        Assert.Equal(mine, row.Id);
        Assert.Equal("201", row.RoomNumber);
    }
}
