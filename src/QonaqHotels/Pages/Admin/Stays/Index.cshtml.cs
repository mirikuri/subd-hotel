using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QonaqHotels.Data;
using QonaqHotels.Services;

namespace QonaqHotels.Pages.Admin.Stays;

[Authorize(Policy = "RequireReceptionist")]
public class IndexModel : PageModel
{
    private readonly Db _db;
    private readonly StayService _stays;
    public IndexModel(Db db, StayService stays) { _db = db; _stays = stays; }

    [BindProperty(SupportsGet = true)] public int? Hotel { get; set; }
    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public List<(string Value, string Label)> Hotels { get; private set; } = [];
    public List<ArrivalRow> Arrivals { get; private set; } = [];
    public List<InHouseRow> InHouse { get; private set; } = [];
    public List<CleaningRow> Cleaning { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await using (var conn = await _db.OpenAsync())
            Hotels = await AdminLists.OptionsAsync(conn, AdminLists.Hotels);
        Hotel ??= Hotels.Count > 0 ? int.Parse(Hotels[0].Value) : null;
        if (Hotel is not { } h) return;

        Arrivals = await _stays.ArrivalsAsync(h, Clock.Today);
        InHouse  = await _stays.InHouseAsync(h);
        Cleaning = await _stays.CleaningAsync(h);
    }

    public async Task<IActionResult> OnPostCheckInAsync(int bookingRoomId)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Forbid();
        Error = await _stays.CheckInAsync(bookingRoomId, userId, Clock.Today);
        if (Error == null) Message = "Гость заселён.";
        return RedirectToPage(new { Hotel });
    }

    public async Task<IActionResult> OnPostCheckOutAsync(int stayId)
    {
        Error = await _stays.CheckOutAsync(stayId);
        if (Error == null) Message = "Гость выселен, номер передан на уборку.";
        return RedirectToPage(new { Hotel });
    }

    public async Task<IActionResult> OnPostCleanAsync(int roomId)
    {
        if (await _stays.MarkCleanAsync(roomId)) Message = "Номер убран и снова свободен.";
        else Error = "Номер уже не на уборке.";
        return RedirectToPage(new { Hotel });
    }
}
