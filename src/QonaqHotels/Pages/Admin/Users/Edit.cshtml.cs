using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Users;

public class EditModel : PageModel
{
    private readonly HotelDbContext _db;
    private readonly PasswordHasher<User> _hasher;
    public EditModel(HotelDbContext db, PasswordHasher<User> hasher)
    {
        _db     = db;
        _hasher = hasher;
    }

    public string EmployeeName { get; set; } = "";

    [BindProperty] public int Id { get; set; }
    [BindProperty] public string Login { get; set; } = "";
    [BindProperty] public string Role { get; set; } = "";
    [BindProperty] public bool IsActive { get; set; }
    [BindProperty] public string? NewPassword { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        await _db.Entry(user).Reference(u => u.Employee).LoadAsync();

        Id           = user.Id;
        Login        = user.Login;
        Role         = user.Role;
        IsActive     = user.IsActive;
        EmployeeName = user.Employee.FullName;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await _db.Users.FindAsync(Id);
        if (user == null) return NotFound();

        user.Login    = Login;
        user.Role     = Role;
        user.IsActive = IsActive;

        if (!string.IsNullOrWhiteSpace(NewPassword))
        {
            user.PasswordHash  = _hasher.HashPassword(user, NewPassword);
            user.FailedAttempts = 0;
            user.LockedUntil   = null;
        }

        await _db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
