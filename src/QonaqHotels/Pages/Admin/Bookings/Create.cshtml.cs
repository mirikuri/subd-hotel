using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Bookings;

public class CreateModel : PageModel
{
    private readonly HotelDbContext _db;
    public CreateModel(HotelDbContext db) => _db = db;

    [BindProperty] public int GuestId { get; set; }
    [BindProperty] public int HotelId { get; set; }
    [BindProperty] public DateOnly CheckInPlan { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [BindProperty] public DateOnly CheckOutPlan { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
    [BindProperty] public string? Source { get; set; }

    public SelectList Guests { get; set; } = null!;
    public SelectList Hotels { get; set; } = null!;

    public void OnGet() => LoadLists();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { LoadLists(); return Page(); }

        _db.Bookings.Add(new Booking
        {
            GuestId      = GuestId,
            HotelId      = HotelId,
            CheckInPlan  = CheckInPlan,
            CheckOutPlan = CheckOutPlan,
            Source       = Source,
            BookingDate  = DateTime.UtcNow,
            Status       = "новая"
        });
        await _db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private void LoadLists()
    {
        Guests = new SelectList(_db.Guests.OrderBy(g => g.FullName).ToList(), "Id", "FullName");
        Hotels = new SelectList(_db.Hotels.OrderBy(h => h.Name).ToList(), "Id", "Name");
    }
}
