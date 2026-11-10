using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Catalogs.RoomTypes;

[Authorize(Policy = "RequireAdmin")]
public class EditModel : PageModel
{
    private readonly Db _db;
    public EditModel(Db db) => _db = db;

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty, Required(ErrorMessage = "Укажите название"), StringLength(80)] public string Name { get; set; } = "";
    [BindProperty, Range(1, 20, ErrorMessage = "Вместимость — от 1 до 20")] public short Capacity { get; set; } = 2;
    [BindProperty] public decimal? AreaM2 { get; set; }
    [BindProperty] public string? BedType { get; set; }
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public int[] AmenityIds { get; set; } = [];

    public List<Amenity> AllAmenities { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        if (Id is int id)
        {
            await using (var cmd = CatalogDb.Cmd(conn,
                "SELECT name, capacity, area_m2, bed_type, description FROM room_types WHERE id = @id", null, ("id", id)))
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return NotFound();
                Name = r.GetString(0); Capacity = r.GetInt16(1);
                AreaM2 = r.IsDBNull(2) ? null : r.GetDecimal(2); BedType = r.IsDBNull(3) ? null : r.GetString(3);
                Description = r.IsDBNull(4) ? null : r.GetString(4);
            }
            await using (var cmd = CatalogDb.Cmd(conn,
                "SELECT amenity_id FROM room_type_amenities WHERE room_type_id = @id", null, ("id", id)))
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                var ids = new List<int>();
                while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
                AmenityIds = [.. ids];
            }
        }
        await LoadAmenitiesAsync(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = await _db.OpenAsync();
        if (ModelState.IsValid)
        {
            // Тип и его удобства — одной транзакцией
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                (string, object?)[] ps =
                [
                    ("id", Id), ("name", Name.Trim()), ("cap", Capacity), ("area", AreaM2),
                    ("bed", string.IsNullOrEmpty(BedType) ? null : BedType),
                    ("descr", string.IsNullOrWhiteSpace(Description) ? null : Description.Trim())
                ];
                await using var save = CatalogDb.Cmd(conn, Id is null
                    ? "INSERT INTO room_types (name, capacity, area_m2, bed_type, description) VALUES (@name, @cap, @area, @bed, @descr) RETURNING id"
                    : "UPDATE room_types SET name = @name, capacity = @cap, area_m2 = @area, bed_type = @bed, description = @descr WHERE id = @id RETURNING id",
                    tx, ps);
                if (await save.ExecuteScalarAsync() is not int id) return NotFound();

                await CatalogDb.ExecAsync(conn,
                    "DELETE FROM room_type_amenities WHERE room_type_id = @id AND amenity_id <> ALL(@ids)", tx,
                    ("id", id), ("ids", AmenityIds));
                await CatalogDb.ExecAsync(conn, """
                    INSERT INTO room_type_amenities (room_type_id, amenity_id)
                    SELECT @id, unnest(@ids) ON CONFLICT DO NOTHING
                    """, tx, ("id", id), ("ids", AmenityIds));

                await tx.CommitAsync();
                return RedirectToPage("Index");
            }
            catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
            {
                await tx.RollbackAsync();
                ModelState.AddModelError("", DbErrors.Describe(ex));
            }
        }
        await LoadAmenitiesAsync(conn);
        return Page();
    }

    private async Task LoadAmenitiesAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand("SELECT id, name, icon FROM amenities ORDER BY name", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            AllAmenities.Add(new Amenity { Id = r.GetInt32(0), Name = r.GetString(1), Icon = r.IsDBNull(2) ? null : r.GetString(2) });
    }
}
