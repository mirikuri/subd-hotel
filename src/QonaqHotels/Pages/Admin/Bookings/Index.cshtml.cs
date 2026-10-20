using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Pages.Admin;

namespace QonaqHotels.Pages.Admin.Bookings;

public class BookingRow
{
    public int Id { get; set; }
    public string GuestName { get; set; } = "";
    public string HotelName { get; set; } = "";
    public DateOnly CheckInPlan { get; set; }
    public DateOnly CheckOutPlan { get; set; }
    public string Status { get; set; } = "";
    public string? Source { get; set; }
}

[Authorize(Policy = "RequireReceptionist")]
public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    public List<BookingRow> Bookings { get; set; } = [];
    public FilterBarModel FilterBar { get; set; } = null!;

    public async Task OnGetAsync()
    {
        using var conn = _db.Open();
        Bookings = await LoadBookings(conn, null, null, null, null, null, null, null, null);
        var hotels = await LoadHotelOptions(conn);
        FilterBar = new FilterBarModel
        {
            FormId = "bookings-filter",
            Fields =
            [
                new() { Type = "text",   Name = "q",      Placeholder = "Поиск по гостю…" },
                new() { Type = "select", Name = "hotel",  Placeholder = "Все отели",    Options = hotels },
                new() { Type = "select", Name = "status", Placeholder = "Все статусы",
                    Options = [("новая","новая"),("подтверждена","подтверждена"),
                               ("заселён","заселён"),("завершена","завершена"),("отменена","отменена")] },
                new() { Type = "select", Name = "source", Placeholder = "Все источники",
                    Options = [("сайт","сайт"),("стойка","стойка"),("телефон","телефон"),("агрегатор","агрегатор")] },
                new() { Type = "date", Name = "checkinFrom",  Placeholder = "Заезд с"  },
                new() { Type = "date", Name = "checkinTo",    Placeholder = "Заезд по" },
                new() { Type = "date", Name = "checkoutFrom", Placeholder = "Выезд с"  },
                new() { Type = "date", Name = "checkoutTo",   Placeholder = "Выезд по" }
            ]
        };
    }

    public async Task<IActionResult> OnGetSearchAsync(
        string? q, int? hotel, string? status, string? source,
        DateOnly? checkinFrom, DateOnly? checkinTo,
        DateOnly? checkoutFrom, DateOnly? checkoutTo)
    {
        using var conn = _db.Open();
        var rows = await LoadBookings(conn, q, hotel, status, source,
            checkinFrom, checkinTo, checkoutFrom, checkoutTo);
        return Partial("_BookingsRows", rows);
    }

    private static async Task<List<BookingRow>> LoadBookings(
        NpgsqlConnection conn, string? q, int? hotel, string? status, string? source,
        DateOnly? checkinFrom, DateOnly? checkinTo, DateOnly? checkoutFrom, DateOnly? checkoutTo)
    {
        var safeQ = q is null ? null : "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        using var cmd = new NpgsqlCommand(
            "SELECT b.id, g.full_name, h.name, b.check_in_plan, b.check_out_plan, b.status, b.source " +
            "FROM bookings b " +
            "JOIN guests g ON g.id = b.guest_id " +
            "JOIN hotels h ON h.id = b.hotel_id " +
            "WHERE (@q::text IS NULL OR lower(g.full_name) LIKE lower(@q) ESCAPE '\\') " +
            "  AND (@hotel::int IS NULL OR b.hotel_id = @hotel) " +
            "  AND (@status::text IS NULL OR b.status = @status) " +
            "  AND (@source::text IS NULL OR b.source = @source) " +
            "  AND (@checkinFrom::date IS NULL OR b.check_in_plan >= @checkinFrom) " +
            "  AND (@checkinTo::date IS NULL OR b.check_in_plan <= @checkinTo) " +
            "  AND (@checkoutFrom::date IS NULL OR b.check_out_plan >= @checkoutFrom) " +
            "  AND (@checkoutTo::date IS NULL OR b.check_out_plan <= @checkoutTo) " +
            "ORDER BY b.booking_date DESC", conn);

        cmd.Parameters.AddWithValue("q",           (object?)safeQ       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hotel",       (object?)hotel       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status",      (object?)status      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("source",      (object?)source      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("checkinFrom",  (object?)checkinFrom  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("checkinTo",    (object?)checkinTo    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("checkoutFrom", (object?)checkoutFrom ?? DBNull.Value);
        cmd.Parameters.AddWithValue("checkoutTo",   (object?)checkoutTo   ?? DBNull.Value);

        var rows = new List<BookingRow>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new BookingRow
            {
                Id           = r.GetInt32(0),
                GuestName    = r.GetString(1),
                HotelName    = r.GetString(2),
                CheckInPlan  = r.GetFieldValue<DateOnly>(3),
                CheckOutPlan = r.GetFieldValue<DateOnly>(4),
                Status       = r.GetString(5),
                Source       = r.IsDBNull(6) ? null : r.GetString(6)
            });
        }
        return rows;
    }

    private static async Task<List<(string Value, string Label)>> LoadHotelOptions(NpgsqlConnection conn)
    {
        var list = new List<(string Value, string Label)>();
        using var cmd = new NpgsqlCommand("SELECT id, name FROM hotels ORDER BY name", conn);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add((r.GetInt32(0).ToString(), r.GetString(1)));
        return list;
    }
}
