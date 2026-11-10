using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Catalogs.Rooms;

public record RoomRow(int Id, string Hotel, string Number, short? Floor, string Type, string? View, string Status);

public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    [BindProperty(SupportsGet = true)] public int? Hotel { get; set; }
    [BindProperty(SupportsGet = true)] public int? Type { get; set; }
    [BindProperty(SupportsGet = true)] public short? Floor { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }

    public List<RoomRow> Rows { get; private set; } = [];
    public List<SelectListItem> Hotels { get; private set; } = [];
    public List<SelectListItem> Types { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        Hotels = await CatalogDb.OptionsAsync(conn, CatalogDb.HotelsSql);
        Types  = await CatalogDb.OptionsAsync(conn, CatalogDb.RoomTypesSql);
        Hotel ??= Hotels.Count > 0 ? int.Parse(Hotels[0].Value) : null;   // 240 номеров сразу — слишком длинно

        await using var cmd = CatalogDb.Cmd(conn, """
            SELECT r.id, h.name, r.room_number, r.floor, rt.name, r.view, r.status
            FROM rooms r JOIN hotels h ON h.id = r.hotel_id JOIN room_types rt ON rt.id = r.room_type_id
            WHERE (@h::int IS NULL OR r.hotel_id = @h) AND (@t::int IS NULL OR r.room_type_id = @t)
              AND (@f::smallint IS NULL OR r.floor = @f) AND (@s::text IS NULL OR r.status = @s)
            ORDER BY h.name, r.floor NULLS LAST, r.room_number
            """, null, ("h", Hotel), ("t", Type), ("f", Floor), ("s", string.IsNullOrEmpty(Status) ? null : Status));
        cmd.Parameters["h"].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer;
        cmd.Parameters["t"].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer;
        cmd.Parameters["f"].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Smallint;
        cmd.Parameters["s"].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            Rows.Add(new RoomRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetInt16(3),
                r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.GetString(6)));
    }
}
