using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Pages.Admin.Bookings;

namespace QonaqHotels.Tests;

// Форма брони в админке: номер обязателен, гостей не больше вместимости номера
public class BookingFormTests
{
    private readonly Db _db = TestDb.Create();
    private static readonly DateOnly D = Clock.Today.AddDays(60);

    private CreateModel Create(TestHotel t, int? roomId, short guests) => new(_db)
    {
        GuestId = t.GuestId, HotelId = t.HotelId, CheckInPlan = D, CheckOutPlan = D.AddDays(2),
        RoomId = roomId, GuestsCount = guests,
        PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
    };

    private async Task<long> CountBookingsAsync(TestHotel t)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM bookings WHERE hotel_id = @h", conn);
        cmd.Parameters.AddWithValue("h", t.HotelId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Create_WithoutRoom_Rejected()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var page = Create(t, roomId: null, guests: 1);

        Assert.IsType<PageResult>(await page.OnPostAsync());
        Assert.True(page.ModelState.ContainsKey(nameof(CreateModel.RoomId)));
        Assert.Equal(0, await CountBookingsAsync(t));
    }

    [Fact]
    public async Task Create_TooManyGuests_Rejected_ByDatabase()
    {
        await using var t = await TestHotel.CreateAsync(_db);        // «Люкс», вместимость 2
        var page = Create(t, t.RoomIds[0], guests: 3);

        Assert.IsType<PageResult>(await page.OnPostAsync());
        var error = Assert.Single(page.ModelState[nameof(CreateModel.GuestsCount)]!.Errors);
        Assert.Equal("Гостей больше, чем вмещает выбранный номер.", error.ErrorMessage);
        Assert.Equal(0, await CountBookingsAsync(t));                // бронь откатилась вместе с номером
    }

    [Fact]
    public async Task Create_WithRoomAndGuests_SavesGuestsCount()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        Assert.IsType<RedirectToPageResult>(await Create(t, t.RoomIds[0], guests: 2).OnPostAsync());

        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT guests_count FROM booking_rooms WHERE room_id = @r AND is_active", conn);
        cmd.Parameters.AddWithValue("r", t.RoomIds[0]);
        Assert.Equal((short)2, await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Edit_RemovingRoom_Rejected_ChangingGuests_Saved()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var id = await t.BookAsync(t.RoomIds[0], D, D.AddDays(2));

        EditModel Edit(int? roomId, short guests) => new(_db)
        {
            Id = id, CheckInPlan = D, CheckOutPlan = D.AddDays(2), Status = "новая",
            RoomId = roomId, GuestsCount = guests,
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
        };

        var noRoom = Edit(null, 1);
        Assert.IsType<PageResult>(await noRoom.OnPostAsync());
        Assert.True(noRoom.ModelState.ContainsKey(nameof(EditModel.RoomId)));

        var tooMany = Edit(t.RoomIds[0], 5);
        Assert.IsType<PageResult>(await tooMany.OnPostAsync());
        Assert.True(tooMany.ModelState.ContainsKey(nameof(EditModel.GuestsCount)));

        Assert.IsType<RedirectToPageResult>(await Edit(t.RoomIds[0], 2).OnPostAsync());
        var loaded = Edit(null, 0);
        await loaded.OnGetAsync(id);
        Assert.Equal((short)2, loaded.GuestsCount);
        Assert.Equal(t.RoomIds[0], loaded.RoomId);
    }
}
