using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Bookings;

public class IndexModel : PageModel
{
    private readonly HotelDbContext _db;
    public IndexModel(HotelDbContext db) => _db = db;

    public List<Booking> Bookings { get; set; } = [];

    public async Task OnGetAsync()
    {
        Bookings = await _db.Bookings
            .Include(b => b.Guest)
            .Include(b => b.Hotel)
            .OrderByDescending(b => b.BookingDate)
            .ToListAsync();
    }
}
