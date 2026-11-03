using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Positions;

[Authorize(Policy = "RequireAdmin")]
public class DeleteModel : PageModel
{
    private readonly Db _db;
    public DeleteModel(Db db) => _db = db;

    public string PositionName { get; set; } = "";
    public string Role { get; set; } = "";
    public int EmployeeCount { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        await LoadPosition(conn, id);
        if (PositionName == "") return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        await LoadPosition(conn, id);
        if (PositionName == "") return NotFound();

        if (EmployeeCount > 0)
        {
            ModelState.AddModelError("", $"Нельзя удалить должность — к ней привязано сотрудников: {EmployeeCount}.");
            return Page();
        }

        // Гарантию даёт внешний ключ employees.position_id (ON DELETE RESTRICT)
        try
        {
            await using var del = new NpgsqlCommand("DELETE FROM positions WHERE id = @id", conn);
            del.Parameters.AddWithValue("id", id);
            await del.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
        {
            ModelState.AddModelError("", DbErrors.DescribeDelete(ex));
            return Page();
        }

        return RedirectToPage("Index");
    }

    private async Task LoadPosition(NpgsqlConnection conn, int id)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT p.name, p.role, COUNT(e.id) " +
            "FROM positions p " +
            "LEFT JOIN employees e ON e.position_id = p.id " +
            "WHERE p.id = @id " +
            "GROUP BY p.name, p.role", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            PositionName  = r.GetString(0);
            Role          = r.GetString(1);
            EmployeeCount = (int)r.GetInt64(2);
        }
    }
}
