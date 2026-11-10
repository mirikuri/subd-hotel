using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QonaqHotels.Data;
using QonaqHotels.Services;

namespace QonaqHotels.Pages.Booking;

// Подтверждение заявки: что гость бронирует и сколько это стоит.
// Цена пересчитывается из БД на каждом показе — значения из адреса не принимаются на веру.
[Authorize(Policy = "RequireGuest")]
public class RequestModel : PageModel
{
    private readonly Db _db;
    private readonly SiteBookingService _bookings;

    public RequestModel(Db db, SiteBookingService bookings)
    {
        _db = db;
        _bookings = bookings;
    }

    [BindProperty(SupportsGet = true)] public int HotelId { get; set; }
    [BindProperty(SupportsGet = true)] public int RoomTypeId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly To { get; set; }
    [BindProperty(SupportsGet = true)] public int Guests { get; set; } = 1;
    [BindProperty] public string? Comment { get; set; }

    public RoomOffer Offer { get; private set; } = null!;
    public string? Message { get; private set; }

    public async Task<IActionResult> OnGetAsync() =>
        await LoadOfferAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (GuestClaims.GuestId(User) is not int guestId) return Forbid();

        var result = await _bookings.SubmitAsync(guestId, HotelId, RoomTypeId, From, To, Guests, Comment);
        if (result.Outcome == SiteBookingOutcome.Success)
        {
            TempData["Message"] = result.Message;
            return RedirectToPage("My");
        }

        Message = result.Message;
        // Предложение могло исчезнуть (номера разобрали) — тогда показываем только сообщение
        if (!await LoadOfferAsync()) Offer = null!;
        return Page();
    }

    private async Task<bool> LoadOfferAsync()
    {
        if (RoomSearch.Validate(From, To, Guests, Clock.Today) != null) return false;
        await using var conn = await _db.OpenAsync();
        var offer = await RoomSearch.FindOfferAsync(conn, HotelId, RoomTypeId, From, To, Guests);
        if (offer is null) return false;
        Offer = offer;
        return true;
    }
}
