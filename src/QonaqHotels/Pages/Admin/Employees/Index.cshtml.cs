using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Employees;

public class IndexModel : PageModel
{
    private readonly HotelDbContext _db;
    public IndexModel(HotelDbContext db) => _db = db;

    public List<Employee> Employees { get; set; } = [];

    public async Task OnGetAsync()
    {
        Employees = await _db.Employees
            .Include(e => e.Hotel)
            .Include(e => e.Position)
            .OrderBy(e => e.FullName)
            .ToListAsync();
    }
}
