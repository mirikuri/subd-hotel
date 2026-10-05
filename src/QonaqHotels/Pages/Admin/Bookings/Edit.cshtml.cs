using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Bookings;

public class EditModel : PageModel
{
    private readonly HotelDbContext _db;
    public EditModel(HotelDbContext db) => _db = db;

    public string GuestName { get; set; } = "";
    public string HotelName { get; set; } = "";

    [BindProperty] public int Id { get; set; }
    [BindProperty] public DateOnly CheckInPlan { get; set; }
    [BindProperty] public DateOnly CheckOutPlan { get; set; }
    [BindProperty] public string Status { get; set; } = "";
    [BindProperty] public string? Source { get; set; }
    [BindProperty] public string? CancelReason { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var b = await _db.Bookings.FindAsync(id);
        if (b == null) return NotFound();

        await _db.Entry(b).Reference(x => x.Guest).LoadAsync();
        await _db.Entry(b).Reference(x => x.Hotel).LoadAsync();

        Id           = b.Id;
        CheckInPlan  = b.CheckInPlan;
        CheckOutPlan = b.CheckOutPlan;
        Status       = b.Status;
        Source       = b.Source;
        CancelReason = b.CancelReason;
        GuestName    = b.Guest.FullName;
        HotelName    = b.Hotel.Name;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (CheckOutPlan <= CheckInPlan)
            ModelState.AddModelError("CheckOutPlan", "Дата выезда должна быть позже даты заезда.");

        if (Status == "отменена" && string.IsNullOrWhiteSpace(CancelReason))
            ModelState.AddModelError("CancelReason", "Укажите причину отмены.");

        if (!ModelState.IsValid)
        {
            var bv = await _db.Bookings.FindAsync(Id);
            if (bv != null) { await _db.Entry(bv).Reference(x => x.Guest).LoadAsync(); await _db.Entry(bv).Reference(x => x.Hotel).LoadAsync(); GuestName = bv.Guest.FullName; HotelName = bv.Hotel.Name; }
            return Page();
        }

        var b = await _db.Bookings.FindAsync(Id);
        if (b == null) return NotFound();

        b.CheckInPlan  = CheckInPlan;
        b.CheckOutPlan = CheckOutPlan;
        b.Status       = Status;
        b.Source       = Source;
        b.CancelReason = Status == "отменена" ? CancelReason : null;
        await _db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
