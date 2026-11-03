using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Guests;

[Authorize(Policy = "RequireReceptionist")]
public class CreateModel : PageModel
{
    private readonly Db _db;
    public CreateModel(Db db) => _db = db;

    // Длины — как у столбцов guests: full_name varchar(150), document_number varchar(40), email varchar(120)
    [BindProperty, Required(ErrorMessage = "Укажите ФИО"),
     StringLength(150, ErrorMessage = "ФИО — не длиннее 150 символов")]
    public string FullName { get; set; } = "";
    [BindProperty, Required(ErrorMessage = "Укажите номер документа"),
     StringLength(40, ErrorMessage = "Номер документа — не длиннее 40 символов")]
    public string DocumentNumber { get; set; } = "";
    [BindProperty] public DateOnly? BirthDate { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty, StringLength(120, ErrorMessage = "Эл. почта — не длиннее 120 символов")]
    public string? Email { get; set; }
    [BindProperty] public int? CountryId { get; set; }

    public SelectList Countries { get; set; } = null!;

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        Countries = await AdminLists.SelectListAsync(conn, AdminLists.Countries);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!string.IsNullOrWhiteSpace(Phone) &&
            !Regex.IsMatch(Phone.Trim(), @"^\+[0-9]{11,12}$"))
            ModelState.AddModelError(nameof(Phone), "Формат: +XXXXXXXXXXX (11–12 цифр после +)");

        if (!string.IsNullOrWhiteSpace(Email) &&
            !new EmailAddressAttribute().IsValid(Email))
            ModelState.AddModelError(nameof(Email), "Некорректный адрес эл. почты.");

        if (BirthDate.HasValue && BirthDate.Value > Clock.Today)
            ModelState.AddModelError(nameof(BirthDate), "Дата рождения не может быть в будущем.");

        await using var conn = await _db.OpenAsync();

        if (ModelState.IsValid)
        {
            // Уникальность номера документа проверяет UNIQUE (guests_document_number_key)
            try
            {
                await using var cmd = new NpgsqlCommand("""
                    INSERT INTO guests (full_name, document_number, birth_date, phone, email, country_id)
                    VALUES (@fn, @dn, @bd, @ph, @em, @cid)
                    """, conn);
                cmd.Parameters.AddWithValue("fn",  FullName.Trim());
                cmd.Parameters.AddWithValue("dn",  DocumentNumber.Trim());
                cmd.Parameters.AddWithValue("bd",  (object?)BirthDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("ph",  string.IsNullOrWhiteSpace(Phone) ? DBNull.Value : Phone.Trim());
                cmd.Parameters.AddWithValue("em",  string.IsNullOrWhiteSpace(Email) ? DBNull.Value : Email.Trim());
                cmd.Parameters.AddWithValue("cid", (object?)CountryId ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();

                return RedirectToPage("Index");
            }
            catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
            {
                ModelState.AddModelError(
                    ex.ConstraintName == "guests_document_number_key" ? nameof(DocumentNumber) : "",
                    DbErrors.Describe(ex));
            }
        }

        Countries = await AdminLists.SelectListAsync(conn, AdminLists.Countries);
        return Page();
    }
}
