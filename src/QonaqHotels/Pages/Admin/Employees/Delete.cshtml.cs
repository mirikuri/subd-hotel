using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Employees;

[Authorize(Policy = "RequireAdmin")]
public class DeleteModel : PageModel
{
    private readonly Db _db;
    public DeleteModel(Db db) => _db = db;

    public string EmployeeName { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        return await LoadNameAsync(conn, id) ? Page() : NotFound();
    }

    // «Удаление» сотрудника — увольнение (мягкое удаление): строка остаётся,
    // на неё ссылаются смены, заселения и заказы услуг.
    public async Task<IActionResult> OnPostAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        if (!await LoadNameAsync(conn, id)) return NotFound();

        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            // dismissal_date IS NULL — уже уволенному дату увольнения не переписываем
            await using (var cmd = new NpgsqlCommand("""
                UPDATE employees SET dismissal_date = @d
                WHERE id = @id AND dismissal_date IS NULL
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("d",  Clock.Today);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            await EmployeeAccounts.DisableAsync(conn, tx, id);
            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
        {
            // Например, дата приёма ещё в будущем: CHECK (dismissal_date >= hire_date)
            await tx.RollbackAsync();
            ModelState.AddModelError("", DbErrors.Describe(ex));
            return Page();
        }

        return RedirectToPage("Index");
    }

    private async Task<bool> LoadNameAsync(NpgsqlConnection conn, int id)
    {
        await using var cmd = new NpgsqlCommand("SELECT full_name FROM employees WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        if (await cmd.ExecuteScalarAsync() is not string name) return false;
        EmployeeName = name;
        return true;
    }
}
