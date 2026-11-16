using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
        await using var conn = await _db.OpenAsync();
        Guests = TakePage(await LoadGuests(conn, null, null, null, page: 1), page: 1);
        var countries = await AdminLists.OptionsAsync(conn, AdminLists.Countries);
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

    public async Task<IActionResult> OnGetSearchAsync(string? q, int? country, string? active, int pageNo = 1)   // «page» в Razor Pages занято: это путь страницы
    {
        if (pageNo < 1) pageNo = 1;
        await using var conn = await _db.OpenAsync();
        var rows = await LoadGuests(conn, q, country, active, pageNo);
        // PageModel.Partial() создаёт пустой ViewData, а кнопке «Показать ещё» нужен ViewData["NextPage"],
        // поэтому результат собираем сами — с копией ViewData страницы.
        return new PartialViewResult
        {
            ViewName = "_GuestsRows",
            ViewData = new ViewDataDictionary<IEnumerable<GuestRow>>(ViewData, TakePage(rows, pageNo))
        };
    }

    // Список показывается порциями по PageSize строк (кнопка «Показать ещё»)
    public const int PageSize = 50;

    // Запрос берёт на одну строку больше порции: если она пришла — дальше есть ещё
    private List<GuestRow> TakePage(List<GuestRow> rows, int page)
    {
        if (rows.Count > PageSize)
        {
            rows.RemoveAt(PageSize);
            ViewData["NextPage"] = page + 1;
        }
        return rows;
    }

    private static async Task<List<GuestRow>> LoadGuests(
        NpgsqlConnection conn, string? q, int? country, string? active, int page)
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
            "ORDER BY g.full_name, g.id " +
            "LIMIT @limit OFFSET @offset", conn);

        cmd.Parameters.AddWithValue("q",       (object?)safeQ   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("country", (object?)country ?? DBNull.Value);
        cmd.Parameters.AddWithValue("active",  (object?)active  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("limit",   PageSize + 1);
        cmd.Parameters.AddWithValue("offset",  (page - 1) * PageSize);

        var rows = new List<GuestRow>();
        await using var r = await cmd.ExecuteReaderAsync();
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
}
