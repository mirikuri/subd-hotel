using System.ComponentModel.DataAnnotations;
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
    [BindProperty, Required, StringLength(50, MinimumLength = 3)] public string Login { get; set; } = "";
    [BindProperty, Required, MinLength(8)] public string Password { get; set; } = "";
    [BindProperty] public string Role { get; set; } = "ресепшен";

    public SelectList Employees { get; set; } = null!;

    public void OnGet() => LoadLists();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { LoadLists(); return Page(); }

        var hash = _hasher.HashPassword(new User(), Password);

        using var conn = _db.Open();
        using var cmd = new NpgsqlCommand(
            "INSERT INTO users (employee_id, login, password_hash, role, is_active, failed_attempts) " +
            "VALUES (@emp, @login, @hash, @role, true, 0)", conn);
        cmd.Parameters.AddWithValue("emp", EmployeeId);
        cmd.Parameters.AddWithValue("login", Login);
        cmd.Parameters.AddWithValue("hash", hash);
        cmd.Parameters.AddWithValue("role", Role);
        await cmd.ExecuteNonQueryAsync();

        return RedirectToPage("Index");
    }

    private void LoadLists()
    {
        using var conn = _db.Open();
        var employees = new List<(int Id, string Name)>();
        using var cmd = new NpgsqlCommand(
            "SELECT e.id, e.full_name || ' — ' || h.name || ', ' || p.name " +
            "FROM employees e " +
            "JOIN hotels h ON h.id = e.hotel_id " +
            "JOIN positions p ON p.id = e.position_id " +
            "WHERE e.dismissal_date IS NULL " +
            "AND NOT EXISTS (SELECT 1 FROM users u WHERE u.employee_id = e.id) " +
            "ORDER BY e.full_name", conn);
        using var r = cmd.ExecuteReader();
        while (r.Read()) employees.Add((r.GetInt32(0), r.GetString(1)));
        Employees = new SelectList(employees.Select(x => new { x.Id, x.Name }), "Id", "Name");
    }
}
