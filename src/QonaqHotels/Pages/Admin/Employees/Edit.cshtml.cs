using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Employees;

public class EditModel : PageModel
{
    private readonly HotelDbContext _db;
    public EditModel(HotelDbContext db) => _db = db;

    [BindProperty] public int Id { get; set; }
    [BindProperty] public string FullName { get; set; } = "";
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public int HotelId { get; set; }
    [BindProperty] public int PositionId { get; set; }
    [BindProperty] public DateOnly HireDate { get; set; }
    [BindProperty] public DateOnly? DismissalDate { get; set; }

    public SelectList Hotels { get; set; } = null!;
    public SelectList Positions { get; set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var emp = await _db.Employees.FindAsync(id);
        if (emp == null) return NotFound();

        Id             = emp.Id;
        FullName       = emp.FullName;
        Phone          = emp.Phone;
        HotelId        = emp.HotelId;
        PositionId     = emp.PositionId;
        HireDate       = emp.HireDate;
        DismissalDate  = emp.DismissalDate;
        LoadLists();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { LoadLists(); return Page(); }

        var emp = await _db.Employees.FindAsync(Id);
        if (emp == null) return NotFound();

        emp.FullName      = FullName;
        emp.Phone         = Phone;
        emp.HotelId       = HotelId;
        emp.PositionId    = PositionId;
        emp.HireDate      = HireDate;
        emp.DismissalDate = DismissalDate;
        await _db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private void LoadLists()
    {
        Hotels    = new SelectList(_db.Hotels.OrderBy(h => h.Name).ToList(), "Id", "Name");
        Positions = new SelectList(_db.Positions.OrderBy(p => p.Name).ToList(), "Id", "Name");
    }
}
