using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Catalogs.RoomTypes;

public record RoomTypeRow(int Id, string Name, short Capacity, decimal? Area, string? Bed, string Amenities, long Rooms);
public record AmenityRow(int Id, string Name, string? Icon, long Used);

// Типы номеров и справочник удобств. Просмотр — все сотрудники, правка удобств — админ (обработчики ниже).
public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    public List<RoomTypeRow> Rows { get; private set; } = [];
    public List<AmenityRow> Amenities { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        await using (var cmd = new NpgsqlCommand("""
            SELECT rt.id, rt.name, rt.capacity, rt.area_m2, rt.bed_type,
                   coalesce((SELECT string_agg(a.name, ', ' ORDER BY a.name) FROM room_type_amenities x
                             JOIN amenities a ON a.id = x.amenity_id WHERE x.room_type_id = rt.id), ''),
                   (SELECT count(*) FROM rooms r WHERE r.room_type_id = rt.id)
            FROM room_types rt ORDER BY rt.name
            """, conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                Rows.Add(new RoomTypeRow(r.GetInt32(0), r.GetString(1), r.GetInt16(2),
                    r.IsDBNull(3) ? null : r.GetDecimal(3), r.IsDBNull(4) ? null : r.GetString(4),
                    r.GetString(5), r.GetInt64(6)));

        await using (var cmd = new NpgsqlCommand("""
            SELECT a.id, a.name, a.icon, (SELECT count(*) FROM room_type_amenities x WHERE x.amenity_id = a.id)
            FROM amenities a ORDER BY a.name
            """, conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                Amenities.Add(new AmenityRow(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetInt64(3)));
    }

    // Удобства: добавить / удалить. Удалить привязанное к типу не даст FK (ON DELETE RESTRICT).
    // [Authorize] на обработчике Razor Pages не работает — роль проверяем сами
    public async Task<IActionResult> OnPostAddAmenityAsync(string name, string? icon)
    {
        if (!User.IsInRole(Roles.Admin)) return Forbid();
        if (string.IsNullOrWhiteSpace(name)) return RedirectToPage();
        return await RunAsync("INSERT INTO amenities (name, icon) VALUES (@n, @i)",
            ("n", name.Trim()), ("i", string.IsNullOrWhiteSpace(icon) ? null : icon.Trim()));
    }

    public async Task<IActionResult> OnPostDeleteAmenityAsync(int id) =>
        User.IsInRole(Roles.Admin) ? await RunAsync("DELETE FROM amenities WHERE id = @id", ("id", id)) : Forbid();

    private async Task<IActionResult> RunAsync(string sql, params (string, object?)[] ps)
    {
        await using var conn = await _db.OpenAsync();
        try
        {
            await CatalogDb.ExecAsync(conn, sql, null, ps);
            return RedirectToPage();
        }
        catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
        {
            ModelState.AddModelError("", ex.SqlState == DbErrors.ForeignKeyViolation
                ? "Нельзя удалить: удобство указано у типов номеров."
                : DbErrors.Describe(ex));
            await OnGetAsync();
            return Page();
        }
    }
}
