using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Guests;

[Authorize(Policy = "RequireReceptionist")]
public class DeleteModel : PageModel
{
    private readonly Db _db;
    public DeleteModel(Db db) => _db = db;

    public string GuestName { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public int BookingCount { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        using var conn = _db.Open();
        await LoadGuest(conn, id);
        if (GuestName == "") return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        using var conn = _db.Open();
        await LoadGuest(conn, id);
        if (GuestName == "") return NotFound();

        if (BookingCount > 0)
        {
            ModelState.AddModelError("", $"Нельзя удалить гостя — у него {BookingCount} брон(ей). Сначала удалите брони.");
            return Page();
        }

        using var del = new NpgsqlCommand("DELETE FROM guests WHERE id = @id", conn);
        del.Parameters.AddWithValue("id", id);
        await del.ExecuteNonQueryAsync();
        return RedirectToPage("Index");
    }

    private async Task LoadGuest(NpgsqlConnection conn, int id)
    {
        using var cmd = new NpgsqlCommand(
            "SELECT g.full_name, g.document_number, COUNT(b.id) " +
            "FROM guests g " +
            "LEFT JOIN bookings b ON b.guest_id = g.id " +
            "WHERE g.id = @id " +
            "GROUP BY g.full_name, g.document_number", conn);
        cmd.Parameters.AddWithValue("id", id);
        using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            GuestName      = r.GetString(0);
            DocumentNumber = r.GetString(1);
            BookingCount   = (int)r.GetInt64(2);
        }
    }
}
