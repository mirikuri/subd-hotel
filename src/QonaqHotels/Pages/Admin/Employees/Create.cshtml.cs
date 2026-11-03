using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Employees;

[Authorize(Policy = "RequireAdmin")]
public class CreateModel : PageModel
{
    private readonly Db _db;
    public CreateModel(Db db) => _db = db;

    [BindProperty, Required(ErrorMessage = "Укажите ФИО"),
     StringLength(150, MinimumLength = 2, ErrorMessage = "ФИО должно содержать от 2 до 150 символов")]   // employees.full_name varchar(150)
    public string FullName { get; set; } = "";

    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public int HotelId { get; set; }
    [BindProperty] public int PositionId { get; set; }
    [BindProperty] public DateOnly HireDate { get; set; } = Clock.Today;

    public SelectList Hotels { get; set; } = null!;
    public SelectList Positions { get; set; } = null!;

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        await LoadListsAsync(conn);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!string.IsNullOrWhiteSpace(Phone) &&
            !Regex.IsMatch(Phone.Trim(), @"^\+[0-9]{11,12}$"))
            ModelState.AddModelError(nameof(Phone), "Формат: +XXXXXXXXXXX (11–12 цифр после +)");

        if (HireDate > Clock.Today.AddDays(30))
            ModelState.AddModelError(nameof(HireDate), "Дата приёма не может быть более чем на 30 дней в будущем.");

        await using var conn = await _db.OpenAsync();

        if (ModelState.IsValid)
        {
            try
            {
                await using var cmd = new NpgsqlCommand("""
                    INSERT INTO employees (hotel_id, position_id, full_name, phone, hire_date)
                    VALUES (@h, @p, @fn, @phone, @d)
                    """, conn);
                cmd.Parameters.AddWithValue("h",     HotelId);
                cmd.Parameters.AddWithValue("p",     PositionId);
                cmd.Parameters.AddWithValue("fn",    FullName.Trim());
                cmd.Parameters.AddWithValue("phone", string.IsNullOrWhiteSpace(Phone) ? DBNull.Value : Phone.Trim());
                cmd.Parameters.AddWithValue("d",     HireDate);
                await cmd.ExecuteNonQueryAsync();

                return RedirectToPage("Index");
            }
            catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
            {
                ModelState.AddModelError("", DbErrors.Describe(ex));
            }
        }

        await LoadListsAsync(conn);
        return Page();
    }

    private async Task LoadListsAsync(NpgsqlConnection conn)
    {
        Hotels    = await AdminLists.SelectListAsync(conn, AdminLists.Hotels);
        Positions = await AdminLists.SelectListAsync(conn, AdminLists.Positions);
    }
}
