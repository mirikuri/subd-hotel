using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Pages.Admin.Bookings;

namespace QonaqHotels.Tests;

public class RoomSearchTests
{
    private readonly Db _db = TestDb.Create();
    private static readonly DateOnly D = Clock.Today.AddDays(10);

    [Fact]
    public async Task Search_HidesTypeWhenAllRoomsBooked()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        await t.BookAsync(t.RoomIds[0], D, D.AddDays(3));
        await using var conn = await _db.OpenAsync();

        Assert.Empty(await RoomSearch.SearchAsync(conn, t.CityId, D.AddDays(1), D.AddDays(2), 2));

        var after = await RoomSearch.SearchAsync(conn, t.CityId, D.AddDays(3), D.AddDays(5), 2);
        var offer = Assert.Single(after);
        Assert.Equal(1, offer.FreeRooms);
        Assert.Equal(2, offer.Nights);
        Assert.True(offer.Amenities.Count >= 3);
    }

    [Fact]
    public async Task Search_IgnoresCancelledBookingRooms()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        await t.BookAsync(t.RoomIds[0], D, D.AddDays(3), status: "отменена");
        await using var conn = await _db.OpenAsync();
        Assert.Single(await RoomSearch.SearchAsync(conn, t.CityId, D, D.AddDays(3), 1));
    }

    [Fact]
    public async Task Search_FiltersByCapacity()
    {
        await using var t = await TestHotel.CreateAsync(_db, roomType: "Люкс");   // вместимость 2
        await using var conn = await _db.OpenAsync();
        Assert.Empty(await RoomSearch.SearchAsync(conn, t.CityId, D, D.AddDays(1), 3));
        Assert.Single(await RoomSearch.SearchAsync(conn, t.CityId, D, D.AddDays(1), 2));
    }

    [Fact]
    public async Task Search_SkipsRepairRooms()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        await t.SetRoomStatusAsync(t.RoomIds[0], "ремонт");
        await using var conn = await _db.OpenAsync();
        Assert.Empty(await RoomSearch.SearchAsync(conn, t.CityId, D, D.AddDays(1), 1));
    }

    [Fact]
    public async Task FindOffer_ReturnsOneOffer()
    {
        await using var t = await TestHotel.CreateAsync(_db, rooms: 2);
        await using var conn = await _db.OpenAsync();
        var offer = await RoomSearch.FindOfferAsync(conn, t.HotelId, t.RoomTypeId, D, D.AddDays(2), 2);
        Assert.NotNull(offer);
        Assert.Equal(2, offer.FreeRooms);
        Assert.Null(await RoomSearch.FindOfferAsync(conn, t.HotelId, t.RoomTypeId, D, D.AddDays(2), 5));
    }

    // Ближайшие 30 ноября (осень, Стандарт $50) и 1 декабря (зима, $55)
    private static DateOnly NextNov30()
    {
        var y = Clock.Today.Year;
        var d = new DateOnly(y, 11, 30);
        return d > Clock.Today ? d : new DateOnly(y + 1, 11, 30);
    }

    [Fact]
    public async Task PeriodPrice_AcrossSeasonBoundary()
    {
        await using var conn = await _db.OpenAsync();
        var standard = await StandardIdAsync(conn);
        var from = NextNov30();
        Assert.Equal(105m, await RoomSearch.PeriodPriceAsync(conn, standard, from, from.AddDays(2)));
    }

    [Fact]
    public async Task PeriodPrice_NullWithoutRate()
    {
        await using var conn = await _db.OpenAsync();
        var standard = await StandardIdAsync(conn);
        var from = NextNov30();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var del = new NpgsqlCommand("""
            DELETE FROM rates WHERE room_type_id = @rt
              AND season_id = (SELECT id FROM seasons WHERE @d BETWEEN date_start AND date_end)
            """, conn, tx))
        {
            del.Parameters.AddWithValue("rt", standard);
            del.Parameters.AddWithValue("d", from.AddDays(1));
            await del.ExecuteNonQueryAsync();
        }
        Assert.Null(await RoomSearch.PeriodPriceAsync(conn, standard, from, from.AddDays(2)));
        await tx.RollbackAsync();
    }

    [Fact]
    public void Validate_Messages()
    {
        var today = new DateOnly(2026, 11, 10);
        Assert.Null(RoomSearch.Validate(today, today.AddDays(3), 2, today));
        Assert.Equal("Дата выезда должна быть позже даты заезда.", RoomSearch.Validate(today, today, 2, today));
        Assert.Equal("Нельзя бронировать прошедшие даты.", RoomSearch.Validate(today.AddDays(-1), today.AddDays(1), 2, today));
        Assert.Equal("Бронирование возможно максимум за год вперёд.",
            RoomSearch.Validate(today.AddYears(1), today.AddYears(1).AddDays(1), 2, today));
        Assert.Equal("Не больше 30 ночей.", RoomSearch.Validate(today, today.AddDays(31), 2, today));
        Assert.Equal("Гостей — от 1 до 6.", RoomSearch.Validate(today, today.AddDays(1), 0, today));
        Assert.Equal("Гостей — от 1 до 6.", RoomSearch.Validate(today, today.AddDays(1), 7, today));
    }

    [Fact]
    public async Task AdminAvailableRooms_StillWork()
    {
        await using var t = await TestHotel.CreateAsync(_db, rooms: 2);
        await t.BookAsync(t.RoomIds[0], D, D.AddDays(2));
        await using var conn = await _db.OpenAsync();
        var rooms = await BookingQueries.LoadAvailableRoomsAsync(conn, t.HotelId, D, D.AddDays(2));
        var only = Assert.Single(rooms);
        Assert.Equal(t.RoomIds[1].ToString(), only.Value);
    }

    [Fact]
    public async Task MinNightlyRates_StandardIs48()
    {
        await using var conn = await _db.OpenAsync();
        var min = await RoomSearch.MinNightlyRatesAsync(conn, Clock.Today);
        Assert.Equal(48m, min[await StandardIdAsync(conn)]);   // весна: 50 × 0.95 = 47.5 → 48
    }

    private static async Task<int> StandardIdAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand("SELECT id FROM room_types WHERE name = 'Стандарт'", conn);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
