using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Catalogs;

// Общая страница удаления записи справочника — только админ.
// Подклассы задают таблицу, подпись записи и куда вернуться.
// Запрет удаления даёт внешний ключ (ON DELETE RESTRICT): ошибку переводим в понятный текст.
[Authorize(Policy = "RequireAdmin")]
public abstract class DeleteRowModel : PageModel
{
    private readonly Db _db;
    protected DeleteRowModel(Db db) => _db = db;

    protected abstract string Table { get; }
    protected abstract string TitleSql { get; }       // SELECT <подпись> FROM ... WHERE id = @id
    public abstract string What { get; }               // «отель», «номер» ...
    public string Title { get; private set; } = "";

    // Действия до удаления в той же транзакции (например, удалить тарифы сезона)
    protected virtual Task BeforeDeleteAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int id) => Task.CompletedTask;

    public async Task<IActionResult> OnGetAsync(int id) =>
        await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        await using var conn = await _db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            await BeforeDeleteAsync(conn, tx, id);
            await CatalogDb.ExecAsync(conn, $"DELETE FROM {Table} WHERE id = @id", tx, ("id", id));
            await tx.CommitAsync();
            return RedirectToPage("Index");
        }
        catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
        {
            await tx.RollbackAsync();
            ModelState.AddModelError("", DbErrors.DescribeDelete(ex));
            return Page();
        }
    }

    private async Task<bool> LoadAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = CatalogDb.Cmd(conn, TitleSql, null, ("id", id));
        if (await cmd.ExecuteScalarAsync() is not string title) return false;
        Title = title;
        return true;
    }
}
