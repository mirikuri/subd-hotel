using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using QonaqHotels.Services;
using System.Security.Claims;

namespace QonaqHotels.Pages;

public class RegisterLoginModel : PageModel
{
    private readonly GuestAuthService _guestAuth;

    public RegisterLoginModel(GuestAuthService guestAuth) => _guestAuth = guestAuth;

    [BindProperty] public LoginInput  Login    { get; set; } = new();
    [BindProperty] public RegisterInput Register { get; set; } = new();

    public string? ErrorMessage    { get; set; }
    public string? SuccessMessage  { get; set; }
    public string  ActiveTab       { get; set; } = "login";

    public class LoginInput
    {
        [Required(ErrorMessage = "Введите логин")]
        [MaxLength(50)]
        public string LoginName { get; set; } = "";

        [Required(ErrorMessage = "Введите пароль")]
        public string Password { get; set; } = "";

        public bool RememberMe { get; set; }
    }

    public class RegisterInput
    {
        [Required(ErrorMessage = "Введите логин")]
        [MinLength(3, ErrorMessage = "Логин от 3 символов")]
        [MaxLength(50)]
        public string LoginName { get; set; } = "";

        [Required(ErrorMessage = "Введите пароль")]
        [MinLength(6, ErrorMessage = "Пароль от 6 символов")]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Укажите ФИО")]
        [MaxLength(150)]
        public string FullName { get; set; } = "";

        [Required(ErrorMessage = "Номер документа обязателен")]
        [MaxLength(40)]
        public string DocumentNumber { get; set; } = "";

        [MaxLength(20)]
        public string? Phone { get; set; }

        [EmailAddress(ErrorMessage = "Некорректный e-mail")]
        [MaxLength(120)]
        public string? Email { get; set; }
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect("/");
        return Page();
    }

    [EnableRateLimiting("login")]
    public async Task<IActionResult> OnPostLoginAsync()
    {
        ActiveTab = "login";

        ModelState.Clear();
        if (!TryValidateModel(Login, nameof(Login)))
            return Page();

        var result = await _guestAuth.AttemptLoginAsync(Login.LoginName, Login.Password);
        if (result.Outcome != GuestAuthOutcome.Success)
        {
            ErrorMessage = result.ErrorMessage;
            Login.Password = "";
            return Page();
        }

        await SignInGuestAsync(result.Guest!.Id, result.Guest.FullName, Login.RememberMe);
        return Redirect("/");
    }

    [EnableRateLimiting("login")]
    public async Task<IActionResult> OnPostRegisterAsync()
    {
        ActiveTab = "register";

        ModelState.Clear();
        if (!TryValidateModel(Register, nameof(Register)))
            return Page();

        var result = await _guestAuth.RegisterAsync(
            Register.LoginName,
            Register.Password,
            Register.FullName,
            Register.DocumentNumber,
            Register.Phone,
            Register.Email);

        if (result.Outcome != GuestAuthOutcome.Success)
        {
            ErrorMessage = result.ErrorMessage;
            Register.Password = "";
            return Page();
        }

        await SignInGuestAsync(result.Guest!.Id, result.Guest.FullName, isPersistent: false);
        return Redirect("/");
    }

    private async Task SignInGuestAsync(int guestId, string fullName, bool isPersistent)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "guest:" + guestId),
            new(ClaimTypes.Name,           fullName),
            new(ClaimTypes.Role,           "гость")
        };
        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var props = new AuthenticationProperties
        {
            IsPersistent = isPersistent,
            ExpiresUtc   = isPersistent
                ? DateTimeOffset.UtcNow.AddDays(30)
                : DateTimeOffset.UtcNow.AddHours(8)
        };
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            props);
    }
}
