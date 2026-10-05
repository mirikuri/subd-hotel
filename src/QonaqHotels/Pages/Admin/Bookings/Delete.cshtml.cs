using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Bookings;

public class DeleteModel : PageModel
{
    private readonly HotelDbContext _db;
    public DeleteModel(HotelDbContext db) => _db = db;

    public Booking Booking { get; set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var b = await _db.Bookings
            .Include(x => x.Guest)
            .Include(x => x.Hotel)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound();
        Booking = b;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var b = await _db.Bookings.FindAsync(id);
        if (b == null) return NotFound();

        if (b.Status != "новая")
        {
            ModelState.AddModelError("", "Удалить можно только бронь со статусом «новая».");
            await _db.Entry(b).Reference(x => x.Guest).LoadAsync();
            await _db.Entry(b).Reference(x => x.Hotel).LoadAsync();
            Booking = b;
            return Page();
        }

        _db.Bookings.Remove(b);
        await _db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
