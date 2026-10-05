using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Users;

public class DeleteModel : PageModel
{
    private readonly HotelDbContext _db;
    public DeleteModel(HotelDbContext db) => _db = db;

    public User UserItem { get; set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var user = await _db.Users.Include(u => u.Employee).FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();
        UserItem = user;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.IsActive   = false;
        user.LockedUntil = DateTime.UtcNow.AddYears(100);
        await _db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
