using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Users;

[Authorize(Policy = "RequireAdmin")]
public class DeleteModel : PageModel
{
    private readonly Db _db;
    public DeleteModel(Db db) => _db = db;

    public string EmployeeName { get; set; } = "";
    public string Login { get; set; } = "";
    public string Role { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT e.full_name, u.login, u.role " +
            "FROM users u JOIN employees e ON e.id = u.employee_id " +
            "WHERE u.id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return NotFound();

        EmployeeName = r.GetString(0);
        Login        = r.GetString(1);
        Role         = r.GetString(2);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        await using var conn = await _db.OpenAsync();

        // Проверка роли — в том же операторе, что и отключение: администраторов отключать нельзя
        await using (var cmd = new NpgsqlCommand("""
            UPDATE users
            SET is_active = false, locked_until = now() + interval '100 years'
            WHERE id = @id AND role <> @admin
            """, conn))
        {
            cmd.Parameters.AddWithValue("id",    id);
            cmd.Parameters.AddWithValue("admin", Roles.Admin);
            if (await cmd.ExecuteNonQueryAsync() > 0) return RedirectToPage("Index");
        }

        // Ничего не обновлено: пользователя нет или это администратор
        await using var exists = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM users WHERE id = @id)", conn);
        exists.Parameters.AddWithValue("id", id);
        return (bool)(await exists.ExecuteScalarAsync())! ? Forbid() : NotFound();
    }
}
