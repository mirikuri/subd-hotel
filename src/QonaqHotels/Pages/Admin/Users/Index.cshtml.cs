using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Users;

public class IndexModel : PageModel
{
    private readonly HotelDbContext _db;
    public IndexModel(HotelDbContext db) => _db = db;

    public List<User> Users { get; set; } = [];

    public async Task OnGetAsync()
    {
        Users = await _db.Users
            .Include(u => u.Employee)
            .OrderBy(u => u.Employee.FullName)
            .ToListAsync();
    }
}
