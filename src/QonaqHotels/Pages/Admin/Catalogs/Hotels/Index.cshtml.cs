using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Catalogs.Hotels;

public record HotelRow(int Id, string Name, string City, short Stars, string Address, string? Phone,
                       TimeOnly CheckIn, TimeOnly CheckOut, long Rooms);

// Просмотр — все сотрудники (папка /Admin), правка — только админ (Edit/Delete)
public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public List<HotelRow> Rows { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = CatalogDb.Cmd(conn, """
            SELECT h.id, h.name, c.name, h.stars, h.address, h.phone, h.check_in_time, h.check_out_time,
                   (SELECT count(*) FROM rooms r WHERE r.hotel_id = h.id)
            FROM hotels h JOIN cities c ON c.id = h.city_id
            WHERE @q::text IS NULL OR lower(h.name || ' ' || c.name) LIKE lower(@q) ESCAPE '\'
            ORDER BY h.name
            """, null, ("q", CatalogDb.Like(Q)));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            Rows.Add(new HotelRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt16(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetFieldValue<TimeOnly>(6), r.GetFieldValue<TimeOnly>(7),
                r.GetInt64(8)));
    }
}
