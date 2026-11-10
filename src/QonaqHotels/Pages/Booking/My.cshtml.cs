using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QonaqHotels.Services;

namespace QonaqHotels.Pages.Booking;

// Брони вошедшего гостя. id гостя — только из его входа, никогда из параметров запроса.
[Authorize(Policy = "RequireGuest")]
public class MyModel : PageModel
{
    private readonly SiteBookingService _bookings;
    public MyModel(SiteBookingService bookings) => _bookings = bookings;

    public List<GuestBookingRow> Bookings { get; private set; } = [];
    [TempData] public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (GuestClaims.GuestId(User) is not int guestId) return Forbid();
        Bookings = await _bookings.ListAsync(guestId);
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        if (GuestClaims.GuestId(User) is not int guestId) return Forbid();
        Message = await _bookings.CancelAsync(guestId, id)
            ? $"Заявка №{id} отменена."
            : "Бронь уже обработана, отменить нельзя.";
        return RedirectToPage();
    }
}
