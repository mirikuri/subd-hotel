using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Guests;

[Authorize(Policy = "RequireReceptionist")]
public class EditModel : PageModel
{
    private readonly Db _db;
    public EditModel(Db db) => _db = db;

    [BindProperty] public int Id { get; set; }
    [BindProperty] public string FullName { get; set; } = "";
    [BindProperty] public string DocumentNumber { get; set; } = "";
    [BindProperty] public DateOnly? BirthDate { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public int? CountryId { get; set; }
    [BindProperty] public bool IsActive { get; set; }

    public SelectList Countries { get; set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        using var conn = _db.Open();
        using var cmd = new NpgsqlCommand(
            "SELECT id, full_name, document_number, birth_date, phone, email, country_id, is_active " +
            "FROM guests WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return NotFound();

        Id             = r.GetInt32(0);
        FullName       = r.GetString(1);
        DocumentNumber = r.GetString(2);
        BirthDate      = r.IsDBNull(3)  ? null : r.GetFieldValue<DateOnly>(3);
        Phone          = r.IsDBNull(4)  ? null : r.GetString(4);
        Email          = r.IsDBNull(5)  ? null : r.GetString(5);
        CountryId      = r.IsDBNull(6)  ? null : r.GetInt32(6);
        IsActive       = r.GetBoolean(7);

        r.Close();
        LoadLists(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { LoadListsNew(); return Page(); }

        using var conn = _db.Open();
        try
        {
            using var cmd = new NpgsqlCommand(
                "UPDATE guests SET full_name = @fn, document_number = @dn, birth_date = @bd, " +
                "phone = @ph, email = @em, country_id = @cid, is_active = @active " +
                "WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("fn",     FullName);
            cmd.Parameters.AddWithValue("dn",     DocumentNumber);
            cmd.Parameters.AddWithValue("bd",     (object?)BirthDate  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ph",     (object?)Phone      ?? DBNull.Value);
            cmd.Parameters.AddWithValue("em",     (object?)Email      ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cid",    (object?)CountryId  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("active", IsActive);
            cmd.Parameters.AddWithValue("id",     Id);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            ModelState.AddModelError(nameof(DocumentNumber), "Гость с таким номером документа уже существует.");
            LoadListsNew();
            return Page();
        }

        return RedirectToPage("Index");
    }

    private void LoadLists(NpgsqlConnection conn)
    {
        var list = new List<(int Id, string Name)>();
        using var cmd = new NpgsqlCommand("SELECT id, name FROM countries ORDER BY name", conn);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetInt32(0), r.GetString(1)));
        Countries = new SelectList(list.Select(x => new { x.Id, x.Name }), "Id", "Name", CountryId);
    }

    private void LoadListsNew()
    {
        using var conn = _db.Open();
        LoadLists(conn);
    }
}
