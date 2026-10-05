using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Employees;

public class DeleteModel : PageModel
{
    private readonly HotelDbContext _db;
    public DeleteModel(HotelDbContext db) => _db = db;

    public Employee Employee { get; set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var emp = await _db.Employees.FindAsync(id);
        if (emp == null) return NotFound();
        Employee = emp;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var emp = await _db.Employees.FindAsync(id);
        if (emp == null) return NotFound();

        emp.DismissalDate = DateOnly.FromDateTime(DateTime.Today);
        await _db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
