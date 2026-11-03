using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Pages.Admin;

namespace QonaqHotels.Pages.Admin.Positions;

public class PositionRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
}

[Authorize(Policy = "RequireAdmin")]
public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    public List<PositionRow> Positions { get; set; } = [];
    public FilterBarModel FilterBar { get; set; } = null!;

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        Positions = await LoadPositions(conn, null, null);
        FilterBar = new FilterBarModel
        {
            FormId = "positions-filter",
            Fields =
            [
                new() { Type = "text",   Name = "q",    Placeholder = "Поиск по названию…" },
                new() { Type = "select", Name = "role", Placeholder = "Все роли",
                    Options = [("админ","Администратор"),("ресепшен","Ресепшен"),("бухгалтер","Бухгалтер")] }
            ]
        };
    }

    public async Task<IActionResult> OnGetSearchAsync(string? q, string? role)
    {
        await using var conn = await _db.OpenAsync();
        return Partial("_PositionsRows", await LoadPositions(conn, q, role));
    }

    private static async Task<List<PositionRow>> LoadPositions(NpgsqlConnection conn, string? q, string? role)
    {
        var safeQ = q is null ? null : "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        await using var cmd = new NpgsqlCommand(
            "SELECT id, name, role FROM positions " +
            "WHERE (@q::text IS NULL OR lower(name) LIKE lower(@q) ESCAPE '\\') " +
            "  AND (@role::text IS NULL OR role = @role) " +
            "ORDER BY name", conn);

        cmd.Parameters.AddWithValue("q",    (object?)safeQ ?? DBNull.Value);
        cmd.Parameters.AddWithValue("role", (object?)role  ?? DBNull.Value);

        var rows = new List<PositionRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new PositionRow { Id = r.GetInt32(0), Name = r.GetString(1), Role = r.GetString(2) });
        return rows;
    }
}
