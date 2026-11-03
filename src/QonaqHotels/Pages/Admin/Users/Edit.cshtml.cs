using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using NpgsqlTypes;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Users;

[Authorize(Policy = "RequireAdmin")]
public class EditModel : PageModel
{
    private readonly Db _db;
    private readonly PasswordHasher<User> _hasher;
    public EditModel(Db db, PasswordHasher<User> hasher) { _db = db; _hasher = hasher; }

    public string EmployeeName { get; set; } = "";

    [BindProperty] public int Id { get; set; }
    [BindProperty, Required(ErrorMessage = "Укажите логин"),
     StringLength(50, MinimumLength = 3, ErrorMessage = "Логин должен содержать от 3 до 50 символов")]
    public string Login { get; set; } = "";
    [BindProperty] public string Role { get; set; } = "";
    [BindProperty] public bool IsActive { get; set; }
    [BindProperty] public string? NewPassword { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT u.id, u.login, u.role, u.is_active, e.full_name
            FROM users u
            JOIN employees e ON e.id = u.employee_id
            WHERE u.id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return NotFound();

        Id           = r.GetInt32(0);
        Login        = r.GetString(1);
        Role         = r.GetString(2);
        IsActive     = r.GetBoolean(3);
        EmployeeName = r.GetString(4);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!string.IsNullOrWhiteSpace(NewPassword))
        {
            if (NewPassword.Length < 8)
                ModelState.AddModelError(nameof(NewPassword), "Пароль должен содержать минимум 8 символов.");
            else if (!Regex.IsMatch(NewPassword, "[A-Z]"))
                ModelState.AddModelError(nameof(NewPassword), "Пароль должен содержать минимум одну заглавную букву.");
            else if (!Regex.IsMatch(NewPassword, "[0-9]"))
                ModelState.AddModelError(nameof(NewPassword), "Пароль должен содержать минимум одну цифру.");
        }

        await using var conn = await _db.OpenAsync();

        if (ModelState.IsValid && await UserLogins.IsTakenAsync(conn, Login, exceptUserId: Id))
            ModelState.AddModelError(nameof(Login), "Такой логин уже занят.");

        if (ModelState.IsValid)
        {
            string? newHash = string.IsNullOrWhiteSpace(NewPassword)
                ? null
                : _hasher.HashPassword(new User(), NewPassword);

            // Один UPDATE без предварительного SELECT: счётчик неудачных входов и блокировку
            // вычисляет сама БД из текущих значений строки. Раньше код читал их, а потом
            // записывал обратно — неудачный вход между чтением и записью терялся.
            // Сброс блокировки: при смене пароля или при включении заблокированной учётки.
            try
            {
                await using var upd = new NpgsqlCommand("""
                    UPDATE users
                    SET login           = @login,
                        role            = @role,
                        is_active       = @active,
                        password_hash   = COALESCE(@hash, password_hash),
                        failed_attempts = CASE WHEN @hash IS NOT NULL OR (@active AND locked_until > now())
                                               THEN 0 ELSE failed_attempts END,
                        locked_until    = CASE WHEN @hash IS NOT NULL OR (@active AND locked_until > now())
                                               THEN NULL ELSE locked_until END
                    WHERE id = @id
                    """, conn);
                upd.Parameters.AddWithValue("login",  Login);
                upd.Parameters.AddWithValue("role",   Role);
                upd.Parameters.AddWithValue("active", IsActive);
                upd.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Varchar) { Value = (object?)newHash ?? DBNull.Value });
                upd.Parameters.AddWithValue("id",     Id);

                return await upd.ExecuteNonQueryAsync() == 0 ? NotFound() : RedirectToPage("Index");
            }
            catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
            {
                ModelState.AddModelError(
                    ex.ConstraintName is "users_login_key" or "users_login_lower_key" ? nameof(Login) : "",
                    DbErrors.Describe(ex));
            }
        }

        // Повторный показ формы: имя сотрудника — из БД
        await using var q = new NpgsqlCommand(
            "SELECT e.full_name FROM users u JOIN employees e ON e.id = u.employee_id WHERE u.id = @id", conn);
        q.Parameters.AddWithValue("id", Id);
        if (await q.ExecuteScalarAsync() is not string name) return NotFound();
        EmployeeName = name;
        return Page();
    }
}
