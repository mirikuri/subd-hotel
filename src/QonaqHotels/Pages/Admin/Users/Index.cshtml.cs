using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;

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

    public async Task<IActionResult> OnPostEnableAsync(int id)
    {
        using var conn = _db.Open();
        using var cmd = new NpgsqlCommand(
            "UPDATE users SET is_active = true, locked_until = NULL, failed_attempts = 0 WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
        return RedirectToPage();
    }

    public async Task OnGetAsync()
    {
        using var conn = _db.Open();
        using var cmd = new NpgsqlCommand(
            "SELECT u.id, e.full_name, h.name, u.login, u.role, u.last_login_at, u.is_active, u.locked_until " +
            "FROM users u " +
            "JOIN employees e ON e.id = u.employee_id " +
            "JOIN hotels h ON h.id = e.hotel_id " +
            "ORDER BY e.full_name", conn);

        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            Users.Add(new UserRow
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
    }
}
