using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Positions;

[Authorize(Policy = "RequireAdmin")]
public class CreateModel : PageModel
{
    private readonly Db _db;
    public CreateModel(Db db) => _db = db;

    [BindProperty, Required(ErrorMessage = "Укажите название должности"),
     StringLength(80, MinimumLength = 2, ErrorMessage = "Название должно содержать от 2 до 80 символов")]
    public string Name { get; set; } = "";
    [BindProperty] public string Role { get; set; } = Roles.Reception;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        // Уникальность названия проверяет UNIQUE (positions_name_key)
        try
        {
            await using var conn = await _db.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO positions (name, role) VALUES (@name, @role)", conn);
            cmd.Parameters.AddWithValue("name", Name.Trim());
            cmd.Parameters.AddWithValue("role", Role);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
        {
            ModelState.AddModelError(nameof(Name), DbErrors.Describe(ex));
            return Page();
        }

        return RedirectToPage("Index");
    }
}
