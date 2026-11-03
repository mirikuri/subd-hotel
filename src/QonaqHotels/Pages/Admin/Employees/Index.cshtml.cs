using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Pages.Admin;

namespace QonaqHotels.Pages.Admin.Employees;

public class EmployeeRow
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string HotelName { get; set; } = "";
    public string PositionName { get; set; } = "";
    public DateOnly HireDate { get; set; }
    public DateOnly? DismissalDate { get; set; }
}

[Authorize(Policy = "RequireAdmin")]
public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    public List<EmployeeRow> Employees { get; set; } = [];
    public FilterBarModel FilterBar { get; set; } = null!;

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        Employees = await LoadEmployees(conn, null, null, null, null, null, null);
        var hotels    = await AdminLists.OptionsAsync(conn, AdminLists.Hotels);
        var positions = await AdminLists.OptionsAsync(conn, AdminLists.Positions);
        FilterBar = new FilterBarModel
        {
            FormId = "employees-filter",
            Fields =
            [
                new() { Type = "text",   Name = "q",        Placeholder = "Поиск по имени…" },
                new() { Type = "select", Name = "hotel",    Placeholder = "Все отели",     Options = hotels },
                new() { Type = "select", Name = "position", Placeholder = "Все должности", Options = positions },
                new() { Type = "select", Name = "status",   Placeholder = "Все статусы",
                    Options = [("работает","Работает"),("уволен","Уволен")] },
                new() { Type = "date", Name = "hireDateFrom", Placeholder = "Принят с"  },
                new() { Type = "date", Name = "hireDateTo",   Placeholder = "Принят по" }
            ]
        };
    }

    public async Task<IActionResult> OnGetSearchAsync(
        string? q, int? hotel, int? position, string? status,
        DateOnly? hireDateFrom, DateOnly? hireDateTo)
    {
        await using var conn = await _db.OpenAsync();
        var rows = await LoadEmployees(conn, q, hotel, position, status, hireDateFrom, hireDateTo);
        return Partial("_EmployeesRows", rows);
    }

    // Восстановить уволенного: сотрудник и его учётка — в одной транзакции
    public async Task<IActionResult> OnPostRehireAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var emp = new NpgsqlCommand(
            "UPDATE employees SET dismissal_date = NULL WHERE id = @id", conn, tx))
        {
            emp.Parameters.AddWithValue("id", id);
            if (await emp.ExecuteNonQueryAsync() == 0) return NotFound();
        }

        await EmployeeAccounts.EnableAsync(conn, tx, id);
        await tx.CommitAsync();
        return RedirectToPage();
    }

    private static async Task<List<EmployeeRow>> LoadEmployees(
        NpgsqlConnection conn, string? q, int? hotel, int? position, string? status,
        DateOnly? hireDateFrom, DateOnly? hireDateTo)
    {
        var safeQ = q is null ? null : "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        await using var cmd = new NpgsqlCommand(
            "SELECT e.id, e.full_name, h.name, p.name, e.hire_date, e.dismissal_date " +
            "FROM employees e " +
            "JOIN hotels h ON h.id = e.hotel_id " +
            "JOIN positions p ON p.id = e.position_id " +
            "WHERE (@q::text IS NULL OR lower(e.full_name) LIKE lower(@q) ESCAPE '\\') " +
            "  AND (@hotel::int IS NULL OR e.hotel_id = @hotel) " +
            "  AND (@position::int IS NULL OR e.position_id = @position) " +
            "  AND (@status::text IS NULL " +
            "       OR (@status = 'работает' AND e.dismissal_date IS NULL) " +
            "       OR (@status = 'уволен'   AND e.dismissal_date IS NOT NULL)) " +
            "  AND (@hireDateFrom::date IS NULL OR e.hire_date >= @hireDateFrom) " +
            "  AND (@hireDateTo::date IS NULL OR e.hire_date <= @hireDateTo) " +
            "ORDER BY e.full_name", conn);

        cmd.Parameters.AddWithValue("q",           (object?)safeQ       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hotel",       (object?)hotel       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("position",    (object?)position    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status",      (object?)status      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hireDateFrom", (object?)hireDateFrom ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hireDateTo",   (object?)hireDateTo   ?? DBNull.Value);

        var rows = new List<EmployeeRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new EmployeeRow
            {
                Id            = r.GetInt32(0),
                FullName      = r.GetString(1),
                HotelName     = r.GetString(2),
                PositionName  = r.GetString(3),
                HireDate      = r.GetFieldValue<DateOnly>(4),
                DismissalDate = r.IsDBNull(5) ? null : r.GetFieldValue<DateOnly>(5)
            });
        }
        return rows;
    }
}
