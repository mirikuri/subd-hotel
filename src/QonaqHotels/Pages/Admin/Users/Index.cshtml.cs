using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Pages.Admin;

namespace QonaqHotels.Pages.Admin.Users;

public class UserRow
{
    public int Id { get; set; }
    public string EmployeeName { get; set; } = "";
    public string HotelName { get; set; } = "";
    public string Login { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LockedUntil { get; set; }
}

[Authorize(Policy = "RequireAdmin")]
public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    public List<UserRow> Users { get; set; } = [];
    public FilterBarModel FilterBar { get; set; } = null!;

    public async Task<IActionResult> OnPostEnableAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET is_active = true, locked_until = NULL, failed_attempts = 0 WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
        return RedirectToPage();
    }

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        Users = await LoadUsers(conn, null, null, null);
        FilterBar = new FilterBarModel
        {
            FormId = "users-filter",
            Fields =
            [
                new() { Type = "text",   Name = "q",      Placeholder = "Имя или логин…" },
                new() { Type = "select", Name = "role",   Placeholder = "Все роли",
                    Options = [("админ","Администратор"),("ресепшен","Ресепшен"),("бухгалтер","Бухгалтер")] },
                new() { Type = "select", Name = "status", Placeholder = "Все статусы",
                    Options = [("активен","Активен"),("заблокирован","Заблокирован"),("отключён","Отключён")] }
            ]
        };
    }

    public async Task<IActionResult> OnGetSearchAsync(string? q, string? role, string? status)
    {
        await using var conn = await _db.OpenAsync();
        var rows = await LoadUsers(conn, q, role, status);
        return Partial("_UsersRows", rows);
    }

    private static async Task<List<UserRow>> LoadUsers(
        NpgsqlConnection conn, string? q, string? role, string? status)
    {
        var safeQ = q is null ? null : "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        await using var cmd = new NpgsqlCommand(
            "SELECT u.id, e.full_name, h.name, u.login, u.role, u.last_login_at, u.is_active, u.locked_until " +
            "FROM users u " +
            "JOIN employees e ON e.id = u.employee_id " +
            "JOIN hotels h ON h.id = e.hotel_id " +
            "WHERE (@q::text IS NULL " +
            "       OR lower(e.full_name) LIKE lower(@q) ESCAPE '\\' " +
            "       OR lower(u.login) LIKE lower(@q) ESCAPE '\\') " +
            "  AND (@role::text IS NULL OR u.role = @role) " +
            "  AND (@status::text IS NULL " +
            "       OR (@status = 'активен'      AND u.is_active = true  AND (u.locked_until IS NULL OR u.locked_until <= NOW())) " +
            "       OR (@status = 'заблокирован' AND u.is_active = true  AND u.locked_until > NOW()) " +
            "       OR (@status = 'отключён'     AND u.is_active = false)) " +
            "ORDER BY e.full_name", conn);

        cmd.Parameters.AddWithValue("q",      (object?)safeQ  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("role",   (object?)role   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);

        var rows = new List<UserRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new UserRow
            {
                Id           = r.GetInt32(0),
                EmployeeName = r.GetString(1),
                HotelName    = r.GetString(2),
                Login        = r.GetString(3),
                Role         = r.GetString(4),
                LastLoginAt  = r.IsDBNull(5) ? null : r.GetDateTime(5),
                IsActive     = r.GetBoolean(6),
                LockedUntil  = r.IsDBNull(7) ? null : r.GetDateTime(7)
            });
        }
        return rows;
    }
}
