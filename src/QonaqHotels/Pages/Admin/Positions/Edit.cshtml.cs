using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Positions;

[Authorize(Policy = "RequireAdmin")]
public class EditModel : PageModel
{
    private readonly Db _db;
    public EditModel(Db db) => _db = db;

    [BindProperty] public int Id { get; set; }

    [BindProperty, Required(ErrorMessage = "Укажите название должности"),
     StringLength(80, MinimumLength = 2, ErrorMessage = "Название должно содержать от 2 до 80 символов")]
    public string Name { get; set; } = "";
    [BindProperty] public string Role { get; set; } = Roles.Reception;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT id, name, role FROM positions WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return NotFound();

        Id   = r.GetInt32(0);
        Name = r.GetString(1);
        Role = r.GetString(2);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        await using var conn = await _db.OpenAsync();

        // Должность и роли всех её сотрудников меняются вместе
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            await using (var upd = new NpgsqlCommand(
                "UPDATE positions SET name = @name, role = @role WHERE id = @id", conn, tx))
            {
                upd.Parameters.AddWithValue("name", Name.Trim());
                upd.Parameters.AddWithValue("role", Role);
                upd.Parameters.AddWithValue("id",   Id);
                if (await upd.ExecuteNonQueryAsync() == 0) return NotFound();
            }

            await using (var sync = new NpgsqlCommand("""
                UPDATE users u
                SET role = @role
                FROM employees e
                WHERE e.id = u.employee_id AND e.position_id = @id
                """, conn, tx))
            {
                sync.Parameters.AddWithValue("role", Role);
                sync.Parameters.AddWithValue("id",   Id);
                await sync.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
        {
            await tx.RollbackAsync();
            ModelState.AddModelError(ex.ConstraintName == "positions_name_key" ? nameof(Name) : "",
                DbErrors.Describe(ex));
            return Page();
        }

        return RedirectToPage("Index");
    }
}
