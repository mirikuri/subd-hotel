using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Catalogs.Seasons;

public record SeasonRow(int Id, string Name, DateOnly Start, DateOnly End, long Rates, decimal? MinPrice, decimal? MaxPrice);

public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    public List<SeasonRow> Rows { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT s.id, s.name, s.date_start, s.date_end, count(r.id), min(r.price_per_night), max(r.price_per_night)
            FROM seasons s LEFT JOIN rates r ON r.season_id = s.id
            GROUP BY s.id ORDER BY s.date_start
            """, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            Rows.Add(new SeasonRow(r.GetInt32(0), r.GetString(1), r.GetFieldValue<DateOnly>(2), r.GetFieldValue<DateOnly>(3),
                r.GetInt64(4), r.IsDBNull(5) ? null : r.GetDecimal(5), r.IsDBNull(6) ? null : r.GetDecimal(6)));
    }
}
