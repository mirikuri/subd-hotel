using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace QonaqHotels.Pages.Admin;

[Authorize]
public class IndexModel : PageModel
{
    public string FullName { get; private set; } = "";
    public string Role     { get; private set; } = "";

    public void OnGet()
    {
        FullName = User.Identity?.Name ?? "Сотрудник";
        Role     = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
    }
}
