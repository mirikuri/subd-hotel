using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Catalogs.Hotels;

// Добавление (без id) и правка отеля — только админ
[Authorize(Policy = "RequireAdmin")]
public class EditModel : PageModel
{
    private readonly Db _db;
    public EditModel(Db db) => _db = db;

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }

    [BindProperty, Required(ErrorMessage = "Укажите название"), StringLength(150)] public string Name { get; set; } = "";
    [BindProperty] public int CityId { get; set; }
    [BindProperty, Required(ErrorMessage = "Укажите адрес"), StringLength(250)] public string Address { get; set; } = "";
    [BindProperty, Range(1, 5, ErrorMessage = "Звёзд — от 1 до 5")] public short Stars { get; set; } = 4;
    [BindProperty, StringLength(20)] public string? Phone { get; set; }
    [BindProperty, EmailAddress(ErrorMessage = "Некорректный e-mail"), StringLength(120)] public string? Email { get; set; }
    [BindProperty] public TimeOnly CheckInTime { get; set; } = new(14, 0);
    [BindProperty] public TimeOnly CheckOutTime { get; set; } = new(12, 0);
    [BindProperty] public string? Description { get; set; }

    public List<SelectListItem> Cities { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        if (Id is int id)
        {
            await using var cmd = CatalogDb.Cmd(conn, """
                SELECT name, city_id, address, stars, phone, email, check_in_time, check_out_time, description
                FROM hotels WHERE id = @id
                """, null, ("id", id));
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            Name = r.GetString(0); CityId = r.GetInt32(1); Address = r.GetString(2); Stars = r.GetInt16(3);
            Phone = r.IsDBNull(4) ? null : r.GetString(4); Email = r.IsDBNull(5) ? null : r.GetString(5);
            CheckInTime = r.GetFieldValue<TimeOnly>(6); CheckOutTime = r.GetFieldValue<TimeOnly>(7);
            Description = r.IsDBNull(8) ? null : r.GetString(8);
        }
        Cities = await CatalogDb.OptionsAsync(conn, CatalogDb.CitiesSql);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = await _db.OpenAsync();
        if (ModelState.IsValid)
        {
            try
            {
                (string, object?)[] ps =
                [
                    ("id", Id), ("name", Name.Trim()), ("city", CityId), ("address", Address.Trim()), ("stars", Stars),
                    ("phone", Blank(Phone)), ("email", Blank(Email)), ("cin", CheckInTime), ("cout", CheckOutTime),
                    ("descr", Blank(Description))
                ];
                var sql = Id is null
                    ? """
                      INSERT INTO hotels (name, city_id, address, stars, phone, email, check_in_time, check_out_time, description)
                      VALUES (@name, @city, @address, @stars, @phone, @email, @cin, @cout, @descr)
                      """
                    : """
                      UPDATE hotels SET name = @name, city_id = @city, address = @address, stars = @stars,
                             phone = @phone, email = @email, check_in_time = @cin, check_out_time = @cout,
                             description = @descr
                      WHERE id = @id
                      """;
                if (await CatalogDb.ExecAsync(conn, sql, null, ps) == 0) return NotFound();
                return RedirectToPage("Index");
            }
            catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
            {
                ModelState.AddModelError("", DbErrors.Describe(ex));
            }
        }
        Cities = await CatalogDb.OptionsAsync(conn, CatalogDb.CitiesSql);
        return Page();
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
