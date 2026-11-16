using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Pages.Admin.Bookings;

namespace QonaqHotels.Tests;

// Счёт брони: проживание по тарифам + услуги (service_orders)
public class BookingBillTests
{
    private readonly Db _db = TestDb.Create();
    private static readonly DateOnly D = Clock.Today.AddDays(50);

    private BillModel Page() =>
        new(_db) { PageContext = new PageContext { HttpContext = new DefaultHttpContext() } };

    private async Task<T> ScalarAsync<T>(string sql, params (string, object)[] ps)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private Task<int> AddServiceAsync(int hotelId, decimal price) => ScalarAsync<int>("""
        INSERT INTO services (category_id, hotel_id, name, price, unit)
        SELECT min(id), @h, 'Тестовый завтрак', @p, 'чел.' FROM service_categories RETURNING id
        """, ("h", hotelId), ("p", price));

    private async Task CleanupAsync(TestHotel t) => await ScalarAsync<int>("""
        WITH o AS (DELETE FROM service_orders WHERE booking_id IN (SELECT id FROM bookings WHERE hotel_id = @h)),
             s AS (DELETE FROM services WHERE hotel_id = @h)
        SELECT 1
        """, ("h", t.HotelId));

    [Fact]
    public async Task Bill_SumsRoomAndServices_AndKeepsPriceAtOrder()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var bookingId = await t.BookAsync(t.RoomIds[0], D, D.AddDays(2));
        var serviceId = await AddServiceAsync(t.HotelId, 12m);
        try
        {
            await Page().OnPostAddAsync(bookingId, serviceId, 3);
            // цена услуги выросла после заказа — счёт меняться не должен
            await ScalarAsync<int>("UPDATE services SET price = 99 WHERE id = @s RETURNING 1", ("s", serviceId));

            var page = Page();
            await page.OnGetAsync(bookingId);

            var roomTotal = await ScalarAsync<decimal>("""
                SELECT sum(ra.price_per_night)
                FROM generate_series(@f::date, @f::date + 1, interval '1 day') AS n(d)
                JOIN seasons s ON n.d::date BETWEEN s.date_start AND s.date_end
                JOIN rates ra  ON ra.season_id = s.id AND ra.room_type_id = @rt
                """, ("f", D), ("rt", t.RoomTypeId));

            Assert.Equal(2, page.Nights);
            Assert.Equal(roomTotal, page.RoomTotal);
            var order = Assert.Single(page.Orders);
            Assert.Equal(12m, order.Price);
            Assert.Equal(36m, page.ServicesTotal);
            Assert.Equal(roomTotal + 36m, page.Total);      // у тестового гостя карты нет — скидки нет

            await Page().OnPostRemoveAsync(bookingId, order.Id);
            page = Page();
            await page.OnGetAsync(bookingId);
            Assert.Empty(page.Orders);
        }
        finally { await CleanupAsync(t); }
    }

    [Fact]
    public async Task Add_Refused_ForForeignHotelService_AndClosedBooking()
    {
        await using var t = await TestHotel.CreateAsync(_db, rooms: 2);
        var open   = await t.BookAsync(t.RoomIds[0], D, D.AddDays(2));
        var closed = await t.BookAsync(t.RoomIds[1], D, D.AddDays(2), status: "отменена");
        var own    = await AddServiceAsync(t.HotelId, 5m);
        var foreign = await ScalarAsync<int>("SELECT min(id) FROM services WHERE hotel_id <> @h", ("h", t.HotelId));
        try
        {
            var page = Page();
            await page.OnPostAddAsync(open, foreign, 1);
            Assert.NotNull(page.Error);

            page = Page();
            await page.OnPostAddAsync(closed, own, 1);
            Assert.NotNull(page.Error);

            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM service_orders WHERE booking_id IN (@a, @b)", ("a", open), ("b", closed)));
        }
        finally { await CleanupAsync(t); }
    }

    [Fact]
    public async Task Bill_AppliesLoyaltyDiscount_FromSeed()
    {
        // гость с картой gold из seed: скидка 10% на проживание
        var bookingId = await ScalarAsync<int>("""
            SELECT b.id FROM bookings b
            JOIN loyalty_cards lc ON lc.guest_id = b.guest_id AND lc.level = 'gold'
            JOIN booking_rooms br ON br.booking_id = b.id AND br.is_active
            ORDER BY b.id LIMIT 1
            """);
        var page = Page();
        await page.OnGetAsync(bookingId);

        Assert.Equal(10, page.DiscountPercent);
        Assert.True(page.RoomTotal > 0);
        Assert.Equal(Math.Round(page.RoomTotal * 0.10m, 2), page.Discount);
        Assert.Equal(page.RoomTotal - page.Discount + page.ServicesTotal, page.Total);
    }
}
