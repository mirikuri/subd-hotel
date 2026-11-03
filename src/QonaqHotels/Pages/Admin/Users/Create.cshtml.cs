using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Users;

[Authorize(Policy = "RequireAdmin")]
public class CreateModel : PageModel
{
    private readonly Db _db;
    private readonly PasswordHasher<User> _hasher;
    public CreateModel(Db db, PasswordHasher<User> hasher) { _db = db; _hasher = hasher; }

    [BindProperty] public int EmployeeId { get; set; }

    [BindProperty, Required(ErrorMessage = "Укажите логин"),
     StringLength(50, MinimumLength = 3, ErrorMessage = "Логин должен содержать от 3 до 50 символов")]
    public string Login { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Укажите пароль"),
     MinLength(8, ErrorMessage = "Пароль должен содержать минимум 8 символов")]
    public string Password { get; set; } = "";

    [BindProperty] public string Role { get; set; } = Roles.Reception;

    public SelectList Employees { get; set; } = null!;

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        await LoadListsAsync(conn);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!string.IsNullOrEmpty(Password))
        {
            if (!Regex.IsMatch(Password, "[A-Z]"))
                ModelState.AddModelError(nameof(Password), "Пароль должен содержать минимум одну заглавную букву.");
            else if (!Regex.IsMatch(Password, "[0-9]"))
                ModelState.AddModelError(nameof(Password), "Пароль должен содержать минимум одну цифру.");
        }

        await using var conn = await _db.OpenAsync();

        // «Admin» и «admin» — один логин: это гарантирует индекс users_login_lower_key.
        // Запрос здесь только чтобы показать ошибку у поля, а не общей строкой.
        if (ModelState.IsValid && await UserLogins.IsTakenAsync(conn, Login, exceptUserId: 0))
            ModelState.AddModelError(nameof(Login), "Такой логин уже занят.");

        if (ModelState.IsValid)
        {
            try
            {
                await using var cmd = new NpgsqlCommand("""
                    INSERT INTO users (employee_id, login, password_hash, role)
                    VALUES (@emp, @login, @hash, @role)
                    """, conn);
                cmd.Parameters.AddWithValue("emp",   EmployeeId);
                cmd.Parameters.AddWithValue("login", Login);
                cmd.Parameters.AddWithValue("hash",  _hasher.HashPassword(new User(), Password));
                cmd.Parameters.AddWithValue("role",  Role);
                await cmd.ExecuteNonQueryAsync();

                return RedirectToPage("Index");
            }
            catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
            {
                ModelState.AddModelError(
                    ex.ConstraintName is "users_login_key" or "users_login_lower_key" ? nameof(Login) : "",
                    DbErrors.Describe(ex));
            }
        }

        await LoadListsAsync(conn);
        return Page();
    }

    // Работающие сотрудники без учётной записи
    private async Task LoadListsAsync(NpgsqlConnection conn)
    {
        Employees = await AdminLists.SelectListAsync(conn, """
            SELECT e.id, e.full_name || ' — ' || h.name || ', ' || p.name
            FROM employees e
            JOIN hotels h ON h.id = e.hotel_id
            JOIN positions p ON p.id = e.position_id
            WHERE e.dismissal_date IS NULL
              AND NOT EXISTS (SELECT 1 FROM users u WHERE u.employee_id = e.id)
            ORDER BY e.full_name
            """);
    }
}
