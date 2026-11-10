using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Catalogs.Services;

public record ServiceRow(int Id, string Hotel, string Category, string Name, decimal Price, string? Unit, bool IsActive);

public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    [BindProperty(SupportsGet = true)] public int? Hotel { get; set; }
    [BindProperty(SupportsGet = true)] public int? Category { get; set; }

    public List<ServiceRow> Rows { get; private set; } = [];
    public List<SelectListItem> Hotels { get; private set; } = [];
    public List<SelectListItem> Categories { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        Hotels     = await CatalogDb.OptionsAsync(conn, CatalogDb.HotelsSql);
        Categories = await CatalogDb.OptionsAsync(conn, CatalogDb.CategoriesSql);

        await using var cmd = new NpgsqlCommand("""
            SELECT s.id, h.name, c.name, s.name, s.price, s.unit, s.is_active
            FROM services s JOIN hotels h ON h.id = s.hotel_id JOIN service_categories c ON c.id = s.category_id
            WHERE (@h::int IS NULL OR s.hotel_id = @h) AND (@c::int IS NULL OR s.category_id = @c)
            ORDER BY h.name, c.name, s.name
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter("h", NpgsqlTypes.NpgsqlDbType.Integer) { Value = (object?)Hotel ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("c", NpgsqlTypes.NpgsqlDbType.Integer) { Value = (object?)Category ?? DBNull.Value });
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            Rows.Add(new ServiceRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDecimal(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetBoolean(6)));
    }

    // Новая категория услуг — только админ ([Authorize] на обработчике не работает, проверяем сами)
    public async Task<IActionResult> OnPostAddCategoryAsync(string name)
    {
        if (!User.IsInRole(Roles.Admin)) return Forbid();
        if (string.IsNullOrWhiteSpace(name)) return RedirectToPage();
        await using var conn = await _db.OpenAsync();
        try
        {
            await CatalogDb.ExecAsync(conn, "INSERT INTO service_categories (name) VALUES (@n)", null, ("n", name.Trim()));
            return RedirectToPage();
        }
        catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
        {
            ModelState.AddModelError("", DbErrors.Describe(ex));
            await OnGetAsync();
            return Page();
        }
    }
}
