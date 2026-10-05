using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Employees;

public class CreateModel : PageModel
{
    private readonly HotelDbContext _db;
    public CreateModel(HotelDbContext db) => _db = db;

    [BindProperty] public string FullName { get; set; } = "";
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public int HotelId { get; set; }
    [BindProperty] public int PositionId { get; set; }
    [BindProperty] public DateOnly HireDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public SelectList Hotels { get; set; } = null!;
    public SelectList Positions { get; set; } = null!;

    public void OnGet() => LoadLists();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { LoadLists(); return Page(); }

        _db.Employees.Add(new Employee
        {
            FullName   = FullName,
            Phone      = Phone,
            HotelId    = HotelId,
            PositionId = PositionId,
            HireDate   = HireDate
        });
        await _db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private void LoadLists()
    {
        Hotels    = new SelectList(_db.Hotels.OrderBy(h => h.Name).ToList(), "Id", "Name");
        Positions = new SelectList(_db.Positions.OrderBy(p => p.Name).ToList(), "Id", "Name");
    }
}
