using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Users;

public class CreateModel : PageModel
{
    private readonly HotelDbContext _db;
    private readonly PasswordHasher<User> _hasher;
    public CreateModel(HotelDbContext db, PasswordHasher<User> hasher)
    {
        _db     = db;
        _hasher = hasher;
    }

    [BindProperty] public int EmployeeId { get; set; }
    [BindProperty] public string Login { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string Role { get; set; } = "ресепшен";

    public SelectList Employees { get; set; } = null!;

    public async Task OnGetAsync() => await LoadListsAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { await LoadListsAsync(); return Page(); }

        var user = new User
        {
            EmployeeId = EmployeeId,
            Login      = Login,
            Role       = Role,
            IsActive   = true
        };
        user.PasswordHash = _hasher.HashPassword(user, Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private async Task LoadListsAsync()
    {
        var usedIds = await _db.Users.Select(u => u.EmployeeId).ToListAsync();
        var freeEmployees = await _db.Employees
            .Where(e => !usedIds.Contains(e.Id) && e.DismissalDate == null)
            .OrderBy(e => e.FullName)
            .ToListAsync();
        Employees = new SelectList(freeEmployees, "Id", "FullName");
    }
}
