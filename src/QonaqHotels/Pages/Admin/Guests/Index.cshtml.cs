using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Pages.Admin;

namespace QonaqHotels.Pages.Admin.Guests;

public class GuestRow
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string? Phone { get; set; }
    public string CountryName { get; set; } = "";
    public bool IsActive { get; set; }
}

[Authorize(Policy = "RequireReceptionist")]
public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    public List<GuestRow> Guests { get; set; } = [];
    public FilterBarModel FilterBar { get; set; } = null!;

    public async Task OnGetAsync()
    {
        using var conn = _db.Open();
        Guests = await LoadGuests(conn, null, null, null);
        var countries = await LoadCountryOptions(conn);
        FilterBar = new FilterBarModel
        {
            FormId = "guests-filter",
            Fields =
            [
                new() { Type = "text",   Name = "q",       Placeholder = "Поиск по имени или документу…" },
                new() { Type = "select", Name = "country", Placeholder = "Все страны", Options = countries },
                new() { Type = "select", Name = "active",  Placeholder = "Все статусы",
                    Options = [("1","Активен"),("0","Отключён")] }
            ]
        };
    }

    public async Task<IActionResult> OnGetSearchAsync(string? q, int? country, string? active)
    {
        using var conn = _db.Open();
        var rows = await LoadGuests(conn, q, country, active);
        return Partial("_GuestsRows", rows);
    }

    private static async Task<List<GuestRow>> LoadGuests(
        NpgsqlConnection conn, string? q, int? country, string? active)
    {
        var safeQ = q is null ? null : "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        using var cmd = new NpgsqlCommand(
            "SELECT g.id, g.full_name, g.document_number, g.phone, COALESCE(c.name, '—'), g.is_active " +
            "FROM guests g " +
            "LEFT JOIN countries c ON c.id = g.country_id " +
            "WHERE (@q::text IS NULL OR lower(g.full_name) LIKE lower(@q) ESCAPE '\\' " +
            "                       OR lower(g.document_number) LIKE lower(@q) ESCAPE '\\') " +
            "  AND (@country::int IS NULL OR g.country_id = @country) " +
            "  AND (@active::text IS NULL OR g.is_active = (@active = '1')) " +
            "ORDER BY g.full_name", conn);

        cmd.Parameters.AddWithValue("q",       (object?)safeQ   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("country", (object?)country ?? DBNull.Value);
        cmd.Parameters.AddWithValue("active",  (object?)active  ?? DBNull.Value);

        var rows = new List<GuestRow>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new GuestRow
            {
                Id             = r.GetInt32(0),
                FullName       = r.GetString(1),
                DocumentNumber = r.GetString(2),
                Phone          = r.IsDBNull(3) ? null : r.GetString(3),
                CountryName    = r.GetString(4),
                IsActive       = r.GetBoolean(5)
            });
        }
        return rows;
    }

    private static async Task<List<(string Value, string Label)>> LoadCountryOptions(NpgsqlConnection conn)
    {
        var list = new List<(string Value, string Label)>();
        using var cmd = new NpgsqlCommand("SELECT id, name FROM countries ORDER BY name", conn);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add((r.GetInt32(0).ToString(), r.GetString(1)));
        return list;
    }
}
