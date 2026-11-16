using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Shifts;

public record ShiftRow(int Id, string Employee, string Position, DateTime Start, DateTime End,
                       decimal Hours, decimal? Pay);
public record PayRow(string Employee, string Position, int Shifts, decimal Hours, decimal? Pay);

// График смен отеля за неделю. Часы смены считает сама БД (shifts.hours_worked — вычисляемый столбец),
// оплата = часы × почасовая ставка должности. Пересечение смен одного сотрудника запрещает
// EXCLUDE-ограничение shifts_no_overlap — код его не проверяет, а показывает сообщение из DbErrors.
[Authorize(Policy = "RequireAccountant")]
public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    [BindProperty(SupportsGet = true)] public int? Hotel { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Week { get; set; }   // любой день нужной недели
    [TempData] public string? Error { get; set; }

    public DateOnly Monday { get; private set; }
    public List<(string Value, string Label)> Hotels { get; private set; } = [];
    public List<(string Value, string Label)> Employees { get; private set; } = [];
    public List<ShiftRow> Rows { get; private set; } = [];
    public List<PayRow> Totals { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        Hotels = await AdminLists.OptionsAsync(conn, AdminLists.Hotels);
        Hotel ??= Hotels.Count > 0 ? int.Parse(Hotels[0].Value) : null;

        var day = Week ?? Clock.Today;
        Monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        if (Hotel is not { } hotelId) return;

        // Работающие сотрудники отеля — для формы добавления смены
        await using (var cmd = new NpgsqlCommand("""
            SELECT e.id, e.full_name || ' — ' || p.name
            FROM employees e JOIN positions p ON p.id = e.position_id
            WHERE e.hotel_id = @h AND e.dismissal_date IS NULL
            ORDER BY e.full_name
            """, conn))
        {
            cmd.Parameters.AddWithValue("h", hotelId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) Employees.Add((r.GetInt32(0).ToString(), r.GetString(1)));
        }

        // Смены, которые начинаются на этой неделе
        await using (var cmd = new NpgsqlCommand("""
            SELECT s.id, e.full_name, p.name, s.shift_start, s.shift_end, s.hours_worked,
                   s.hours_worked * p.hourly_rate
            FROM shifts s
            JOIN employees e ON e.id = s.employee_id
            JOIN positions p ON p.id = e.position_id
            WHERE e.hotel_id = @h AND s.shift_start >= @mon AND s.shift_start < @mon + 7
            ORDER BY s.shift_start, e.full_name
            """, conn))
        {
            cmd.Parameters.AddWithValue("h", hotelId);
            cmd.Parameters.AddWithValue("mon", Monday);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Rows.Add(new ShiftRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetDateTime(3),
                    r.GetDateTime(4), r.GetDecimal(5), r.IsDBNull(6) ? null : r.GetDecimal(6)));
        }

        // Итог недели по сотрудникам — считает SQL (GROUP BY)
        await using (var cmd = new NpgsqlCommand("""
            SELECT e.full_name, p.name, count(*)::int, sum(s.hours_worked), sum(s.hours_worked * p.hourly_rate)
            FROM shifts s
            JOIN employees e ON e.id = s.employee_id
            JOIN positions p ON p.id = e.position_id
            WHERE e.hotel_id = @h AND s.shift_start >= @mon AND s.shift_start < @mon + 7
            GROUP BY e.id, e.full_name, p.name
            ORDER BY e.full_name
            """, conn))
        {
            cmd.Parameters.AddWithValue("h", hotelId);
            cmd.Parameters.AddWithValue("mon", Monday);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Totals.Add(new PayRow(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetDecimal(3),
                    r.IsDBNull(4) ? null : r.GetDecimal(4)));
        }
    }

    public async Task<IActionResult> OnPostAddAsync(int employeeId, DateTime start, DateTime end)
    {
        await using var conn = await _db.OpenAsync();
        try
        {
            // Сотрудник — только из выбранного отеля и не уволенный, иначе вставится 0 строк.
            // Остальное проверяет БД: конец позже начала, не дольше 24 часов, без пересечений.
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO shifts (employee_id, shift_start, shift_end)
                SELECT e.id, @s, @e
                FROM employees e
                WHERE e.id = @emp AND e.hotel_id = @h AND e.dismissal_date IS NULL
                """, conn);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("h", Hotel ?? 0);
            cmd.Parameters.AddWithValue("s", DateTime.SpecifyKind(start, DateTimeKind.Unspecified));
            cmd.Parameters.AddWithValue("e", DateTime.SpecifyKind(end, DateTimeKind.Unspecified));
            if (await cmd.ExecuteNonQueryAsync() == 0)
                Error = "Сотрудник не найден в этом отеле или уволен.";
        }
        catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
        {
            Error = DbErrors.Describe(ex);
        }
        return RedirectToPage(new { Hotel, Week = DateOnly.FromDateTime(start).ToString("yyyy-MM-dd") });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int shiftId)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM shifts WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", shiftId);
        await cmd.ExecuteNonQueryAsync();
        return RedirectToPage(new { Hotel, Week = Week?.ToString("yyyy-MM-dd") });
    }
}
