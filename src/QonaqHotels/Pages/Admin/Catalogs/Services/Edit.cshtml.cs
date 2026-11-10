using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Catalogs.Services;

// Бухгалтер меняет только цену и «продаётся», админ — всё и добавляет новые услуги
[Authorize(Policy = "RequireAccountant")]
public class EditModel : PageModel
{
    private readonly Db _db;
    public EditModel(Db db) => _db = db;

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public int HotelId { get; set; }
    [BindProperty] public int CategoryId { get; set; }
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public decimal Price { get; set; }
    [BindProperty] public string? Unit { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;

    public bool IsAdmin => User.IsInRole(Roles.Admin);
    public List<SelectListItem> Hotels { get; private set; } = [];
    public List<SelectListItem> Categories { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is null && !IsAdmin) return Forbid();
        await using var conn = await _db.OpenAsync();
        if (Id is int id && !await LoadAsync(conn, id)) return NotFound();
        await LoadListsAsync(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Id is null && !IsAdmin) return Forbid();
        if (Price < 0) ModelState.AddModelError(nameof(Price), "Цена не может быть отрицательной.");
        if (IsAdmin && string.IsNullOrWhiteSpace(Name)) ModelState.AddModelError(nameof(Name), "Укажите название.");

        await using var conn = await _db.OpenAsync();
        if (ModelState.IsValid)
        {
            try
            {
                int changed;
                if (!IsAdmin)
                    changed = await CatalogDb.ExecAsync(conn,
                        "UPDATE services SET price = @p, is_active = @a WHERE id = @id", null,
                        ("p", Price), ("a", IsActive), ("id", Id));
                else
                {
                    (string, object?)[] ps =
                    [
                        ("id", Id), ("h", HotelId), ("c", CategoryId), ("n", Name.Trim()),
                        ("d", string.IsNullOrWhiteSpace(Description) ? null : Description.Trim()),
                        ("p", Price), ("u", string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim()), ("a", IsActive)
                    ];
                    changed = await CatalogDb.ExecAsync(conn, Id is null
                        ? "INSERT INTO services (hotel_id, category_id, name, description, price, unit, is_active) VALUES (@h, @c, @n, @d, @p, @u, @a)"
                        : "UPDATE services SET hotel_id = @h, category_id = @c, name = @n, description = @d, price = @p, unit = @u, is_active = @a WHERE id = @id",
                        null, ps);
                }
                if (changed == 0) return NotFound();
                return RedirectToPage("Index");
            }
            catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
            {
                ModelState.AddModelError("", DbErrors.Describe(ex));
            }
        }
        if (!IsAdmin && Id is int id) { var (p, a) = (Price, IsActive); await LoadAsync(conn, id); (Price, IsActive) = (p, a); }
        await LoadListsAsync(conn);
        return Page();
    }

    private async Task<bool> LoadAsync(NpgsqlConnection conn, int id)
    {
        await using var cmd = CatalogDb.Cmd(conn,
            "SELECT hotel_id, category_id, name, description, price, unit, is_active FROM services WHERE id = @id", null, ("id", id));
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return false;
        HotelId = r.GetInt32(0); CategoryId = r.GetInt32(1); Name = r.GetString(2);
        Description = r.IsDBNull(3) ? null : r.GetString(3); Price = r.GetDecimal(4);
        Unit = r.IsDBNull(5) ? null : r.GetString(5); IsActive = r.GetBoolean(6);
        return true;
    }

    private async Task LoadListsAsync(NpgsqlConnection conn)
    {
        Hotels     = await CatalogDb.OptionsAsync(conn, CatalogDb.HotelsSql);
        Categories = await CatalogDb.OptionsAsync(conn, CatalogDb.CategoriesSql);
    }
}
