using System.ComponentModel.DataAnnotations;
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

    [BindProperty, Required] public string FullName { get; set; } = "";
    [BindProperty, Required] public string DocumentNumber { get; set; } = "";
    [BindProperty] public DateOnly? BirthDate { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public int? CountryId { get; set; }

    public SelectList Countries { get; set; } = null!;

    public void OnGet() => LoadLists();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { LoadLists(); return Page(); }

        using var conn = _db.Open();
        try
        {
            using var cmd = new NpgsqlCommand(
                "INSERT INTO guests (full_name, document_number, birth_date, phone, email, country_id) " +
                "VALUES (@fn, @dn, @bd, @ph, @em, @cid)", conn);
            cmd.Parameters.AddWithValue("fn",  FullName);
            cmd.Parameters.AddWithValue("dn",  DocumentNumber);
            cmd.Parameters.AddWithValue("bd",  (object?)BirthDate  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ph",  (object?)Phone      ?? DBNull.Value);
            cmd.Parameters.AddWithValue("em",  (object?)Email      ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cid", (object?)CountryId  ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            ModelState.AddModelError(nameof(DocumentNumber), "Гость с таким номером документа уже существует.");
            LoadLists();
            return Page();
        }

        return RedirectToPage("Index");
    }

    private void LoadLists()
    {
        using var conn = _db.Open();
        var list = new List<(int Id, string Name)>();
        using var cmd = new NpgsqlCommand("SELECT id, name FROM countries ORDER BY name", conn);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetInt32(0), r.GetString(1)));
        Countries = new SelectList(list.Select(x => new { x.Id, x.Name }), "Id", "Name");
    }
}
