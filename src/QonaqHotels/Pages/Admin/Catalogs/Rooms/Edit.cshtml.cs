using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Catalogs.Rooms;

// Ресепшен меняет только статус номера (уборка, ремонт), админ — всё.
// Остальные поля от не-админа игнорируются на сервере, а не только скрываются в форме.
[Authorize(Policy = "RequireReceptionist")]
public class EditModel : PageModel
{
    private readonly Db _db;
    public EditModel(Db db) => _db = db;

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public int HotelId { get; set; }
    [BindProperty] public int RoomTypeId { get; set; }
    [BindProperty] public string RoomNumber { get; set; } = "";
    [BindProperty] public short? Floor { get; set; }
    [BindProperty] public string? View { get; set; }
    [BindProperty] public string Status { get; set; } = RoomStatus.Free;

    public bool IsAdmin => User.IsInRole(Roles.Admin);
    public List<SelectListItem> Hotels { get; private set; } = [];
    public List<SelectListItem> Types { get; private set; } = [];

    // Статусы, которые ставят руками. «занят» — признак заселения, его здесь не трогаем.
    public static readonly string[] EditableStatuses = [RoomStatus.Free, RoomStatus.Cleaning, RoomStatus.Repair];

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is null && !IsAdmin) return Forbid();
        await using var conn = await _db.OpenAsync();
        if (Id is int id)
        {
            await using var cmd = CatalogDb.Cmd(conn,
                "SELECT hotel_id, room_type_id, room_number, floor, view, status FROM rooms WHERE id = @id", null, ("id", id));
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            HotelId = r.GetInt32(0); RoomTypeId = r.GetInt32(1); RoomNumber = r.GetString(2);
            Floor = r.IsDBNull(3) ? null : r.GetInt16(3); View = r.IsDBNull(4) ? null : r.GetString(4); Status = r.GetString(5);
        }
        await LoadListsAsync(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Id is null && !IsAdmin) return Forbid();
        if (!IsAdmin && !EditableStatuses.Contains(Status))
            ModelState.AddModelError(nameof(Status), "Недопустимый статус.");
        if (IsAdmin && string.IsNullOrWhiteSpace(RoomNumber))
            ModelState.AddModelError(nameof(RoomNumber), "Укажите номер.");

        await using var conn = await _db.OpenAsync();
        if (ModelState.IsValid)
        {
            try
            {
                int changed;
                if (!IsAdmin)
                    changed = await CatalogDb.ExecAsync(conn,
                        "UPDATE rooms SET status = @s WHERE id = @id", null, ("s", Status), ("id", Id));
                else
                {
                    (string, object?)[] ps =
                    [
                        ("id", Id), ("h", HotelId), ("t", RoomTypeId), ("n", RoomNumber.Trim()), ("f", Floor),
                        ("v", string.IsNullOrEmpty(View) ? null : View), ("s", Status)
                    ];
                    changed = await CatalogDb.ExecAsync(conn, Id is null
                        ? "INSERT INTO rooms (hotel_id, room_type_id, room_number, floor, view, status) VALUES (@h, @t, @n, @f, @v, @s)"
                        : "UPDATE rooms SET hotel_id = @h, room_type_id = @t, room_number = @n, floor = @f, view = @v, status = @s WHERE id = @id",
                        null, ps);
                }
                if (changed == 0) return NotFound();
                return RedirectToPage("Index", new { hotel = IsAdmin ? HotelId : (int?)null });
            }
            catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
            {
                ModelState.AddModelError("", DbErrors.Describe(ex));
            }
        }
        await LoadListsAsync(conn);
        return Page();
    }

    private async Task LoadListsAsync(NpgsqlConnection conn)
    {
        Hotels = await CatalogDb.OptionsAsync(conn, CatalogDb.HotelsSql);
        Types  = await CatalogDb.OptionsAsync(conn, CatalogDb.RoomTypesSql);
    }
}
