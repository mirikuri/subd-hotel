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

    // Первоначальная загрузка страницы
    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();

        Bookings = await LoadBookings(
            conn, null, null, null, null, null, null,
            null, null, null, null);

        var countries = await AdminLists.OptionsAsync(conn, AdminLists.Countries);

        FilterBar = new FilterBarModel
        {
            FormId = "bookings-filter",
            Fields =
            [
                new()
                {
                    Type = "text",
                    Name = "q",
                    Placeholder = "Поиск по гостю…"
                },

                // Родительский фильтр
                new()
                {
                    Type = "select",
                    Name = "country",
                    Placeholder = "Все страны",
                    Options = countries
                },

                // Зависит от страны
                new()
                {
                    Type = "select",
                    Name = "city",
                    Placeholder = "Все города",
                    DependsOn = "country",
                    OptionsHandler = "Cities",
                    OptionsParameter = "countryId"
                },

                // Зависит от города
                new()
                {
                    Type = "select",
                    Name = "hotel",
                    Placeholder = "Все отели",
                    DependsOn = "city",
                    OptionsHandler = "Hotels",
                    OptionsParameter = "cityId"
                },

                new()
                {
                    Type = "select",
                    Name = "status",
                    Placeholder = "Все статусы",
                    Options =
                    [
                        ("новая", "новая"),
                        ("подтверждена", "подтверждена"),
                        ("заселён", "заселён"),
                        ("завершена", "завершена"),
                        ("отменена", "отменена")
                    ]
                },

                new()
                {
                    Type = "select",
                    Name = "source",
                    Placeholder = "Все источники",
                    Options =
                    [
                        ("сайт", "сайт"),
                        ("стойка", "стойка"),
                        ("телефон", "телефон"),
                        ("агрегатор", "агрегатор")
                    ]
                },

                new() { Type = "date", Name = "checkinFrom", Placeholder = "Заезд с" },
                new() { Type = "date", Name = "checkinTo", Placeholder = "Заезд по" },
                new() { Type = "date", Name = "checkoutFrom", Placeholder = "Выезд с" },
                new() { Type = "date", Name = "checkoutTo", Placeholder = "Выезд по" }
            ]
        };
    }

    // AJAX: получение городов по стране
    public async Task<IActionResult> OnGetCitiesAsync(int? countryId)
    {
        if (countryId is null)
            return new JsonResult(Array.Empty<object>());

        await using var conn = await _db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT id, name FROM cities " +
            "WHERE country_id = @countryId ORDER BY name",
            conn
        );

        cmd.Parameters.AddWithValue("countryId", countryId.Value);

        var options = new List<object>();

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            options.Add(new
            {
                value = reader.GetInt32(0).ToString(),
                label = reader.GetString(1)
            });
        }

        return new JsonResult(options);
    }

    // AJAX: получение отелей по городу
    public async Task<IActionResult> OnGetHotelsAsync(int? cityId)
    {
        if (cityId is null)
            return new JsonResult(Array.Empty<object>());

        await using var conn = await _db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT id, name FROM hotels " +
            "WHERE city_id = @cityId ORDER BY name",
            conn
        );

        cmd.Parameters.AddWithValue("cityId", cityId.Value);

        var options = new List<object>();

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            options.Add(new
            {
                value = reader.GetInt32(0).ToString(),
                label = reader.GetString(1)
            });
        }

        return new JsonResult(options);
    }

    // AJAX: фильтрация таблицы
    public async Task<IActionResult> OnGetSearchAsync(
        string? q,
        int? country,
        int? city,
        int? hotel,
        string? status,
        string? source,
        DateOnly? checkinFrom,
        DateOnly? checkinTo,
        DateOnly? checkoutFrom,
        DateOnly? checkoutTo)
    {
        await using var conn = await _db.OpenAsync();

        var rows = await LoadBookings(
            conn, q, country, city, hotel, status, source,
            checkinFrom, checkinTo, checkoutFrom, checkoutTo
        );

        return Partial("_BookingsRows", rows);
    }

    // Общий SQL-запрос для загрузки бронирований
    private static async Task<List<BookingRow>> LoadBookings(
        NpgsqlConnection conn,
        string? q,
        int? country,
        int? city,
        int? hotel,
        string? status,
        string? source,
        DateOnly? checkinFrom,
        DateOnly? checkinTo,
        DateOnly? checkoutFrom,
        DateOnly? checkoutTo)
    {
        var safeQ = q is null
            ? null
            : "%" + q.Replace("\\", "\\\\")
                     .Replace("%", "\\%")
                     .Replace("_", "\\_") + "%";

        await using var cmd = new NpgsqlCommand(
            """
            SELECT
                b.id,
                g.full_name,
                h.name,
                b.check_in_plan,
                b.check_out_plan,
                b.status,
                b.source
            FROM bookings b
            JOIN guests g ON g.id = b.guest_id
            JOIN hotels h ON h.id = b.hotel_id
            JOIN cities c ON c.id = h.city_id
            WHERE
                (@q::text IS NULL OR lower(g.full_name) LIKE lower(@q) ESCAPE '\')
                AND (@country::int IS NULL OR c.country_id = @country)
                AND (@city::int IS NULL OR h.city_id = @city)
                AND (@hotel::int IS NULL OR b.hotel_id = @hotel)
                AND (@status::text IS NULL OR b.status = @status)
                AND (@source::text IS NULL OR b.source = @source)
                AND (@checkinFrom::date IS NULL OR b.check_in_plan >= @checkinFrom)
                AND (@checkinTo::date IS NULL OR b.check_in_plan <= @checkinTo)
                AND (@checkoutFrom::date IS NULL OR b.check_out_plan >= @checkoutFrom)
                AND (@checkoutTo::date IS NULL OR b.check_out_plan <= @checkoutTo)
            ORDER BY b.booking_date DESC
            """,
            conn
        );

        cmd.Parameters.AddWithValue("q", (object?)safeQ ?? DBNull.Value);
        cmd.Parameters.AddWithValue("country", (object?)country ?? DBNull.Value);
        cmd.Parameters.AddWithValue("city", (object?)city ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hotel", (object?)hotel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("source", (object?)source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("checkinFrom", (object?)checkinFrom ?? DBNull.Value);
        cmd.Parameters.AddWithValue("checkinTo", (object?)checkinTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("checkoutFrom", (object?)checkoutFrom ?? DBNull.Value);
        cmd.Parameters.AddWithValue("checkoutTo", (object?)checkoutTo ?? DBNull.Value);

        var rows = new List<BookingRow>();

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new BookingRow
            {
                Id = reader.GetInt32(0),
                GuestName = reader.GetString(1),
                HotelName = reader.GetString(2),
                CheckInPlan = reader.GetFieldValue<DateOnly>(3),
                CheckOutPlan = reader.GetFieldValue<DateOnly>(4),
                Status = reader.GetString(5),
                Source = reader.IsDBNull(6)
                    ? null
                    : reader.GetString(6)
            });
        }

        return rows;
    }
}
