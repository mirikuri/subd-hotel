using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using NpgsqlTypes;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Chess;

public class ChessRoom
{
    public int RoomId { get; set; }
    public string RoomNumber { get; set; } = "";
    public int? Floor { get; set; }   // rooms.floor допускает NULL
    public string TypeName { get; set; } = "";
    public List<ChessBooking> Bookings { get; set; } = [];
}

public class ChessBooking
{
    public int BookingId { get; set; }
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public string Status { get; set; } = "";
    public string GuestName { get; set; } = "";
}

public class ChessGrid
{
    public int HotelId { get; set; }
    public DateOnly WindowStart { get; set; }
    public DateOnly WindowEnd { get; set; }
    public List<ChessRoom> Rooms { get; set; } = [];
}

[Authorize(Policy = "RequireReceptionist")]
public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    public List<(string Value, string Label)> Hotels { get; set; } = [];
    public ChessGrid Grid { get; set; } = null!;
    public int DefaultHotelId { get; set; }
    public string[] ActiveStatuses { get; set; } = DefaultStatuses;

    // По умолчанию шахматка показывает брони, которые занимают номер сейчас или займут позже
    private static readonly string[] DefaultStatuses =
        [BookingStatus.New, BookingStatus.Confirmed, BookingStatus.CheckedIn];

    public async Task OnGetAsync(int? hotelId, DateOnly? dateFrom, string[]? statuses)
    {
        await using var conn = await _db.OpenAsync();
        Hotels = await AdminLists.OptionsAsync(conn, AdminLists.Hotels);
        DefaultHotelId = hotelId ?? (Hotels.Count > 0 ? int.Parse(Hotels[0].Value) : 1);
        var start = dateFrom ?? Clock.Today;
        ActiveStatuses = statuses?.Length > 0 ? statuses : DefaultStatuses;
        Grid = await LoadGrid(conn, DefaultHotelId, start, ActiveStatuses);
    }

    public async Task<IActionResult> OnGetGridAsync(int hotelId, DateOnly dateFrom, string[]? statuses)
    {
        await using var conn = await _db.OpenAsync();
        var grid = await LoadGrid(conn, hotelId, dateFrom, statuses?.Length > 0 ? statuses : null);
        return Partial("_Grid", grid);
    }

    private static async Task<ChessGrid> LoadGrid(
        NpgsqlConnection conn, int hotelId, DateOnly windowStart, string[]? statuses)
    {
        var windowEnd = windowStart.AddDays(14);

        const string sql = """
            SELECT r.id, r.room_number, r.floor, rt.name,
                   br.booking_id, br.date_from, br.date_to,
                   b.status, g.full_name
            FROM rooms r
            JOIN room_types rt ON rt.id = r.room_type_id
            LEFT JOIN booking_rooms br
                   ON br.room_id = r.id
                  AND br.is_active
                  AND br.date_to  > @windowStart
                  AND br.date_from < @windowEnd
            LEFT JOIN bookings b
                   ON b.id = br.booking_id
                  AND (@statuses::text[] IS NULL OR b.status = ANY(@statuses))
            LEFT JOIN guests g ON g.id = b.guest_id
            WHERE r.hotel_id = @hotelId
            ORDER BY r.floor NULLS LAST, r.room_number
            """;

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("hotelId",     hotelId);
        cmd.Parameters.AddWithValue("windowStart", windowStart);
        cmd.Parameters.AddWithValue("windowEnd",   windowEnd);
        cmd.Parameters.Add(new NpgsqlParameter("statuses", NpgsqlDbType.Array | NpgsqlDbType.Text)
            { Value = (object?)statuses ?? DBNull.Value });

        var rooms = new List<ChessRoom>();
        var roomIndex = new Dictionary<int, ChessRoom>();

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var roomId = r.GetInt32(0);
            if (!roomIndex.TryGetValue(roomId, out var room))
            {
                room = new ChessRoom
                {
                    RoomId     = roomId,
                    RoomNumber = r.GetString(1),
                    Floor      = r.IsDBNull(2) ? null : r.GetInt16(2),
                    TypeName   = r.GetString(3)
                };
                rooms.Add(room);
                roomIndex[roomId] = room;
            }

            // column 7 = b.status — null when booking filtered out or no booking
            if (!r.IsDBNull(7))
            {
                room.Bookings.Add(new ChessBooking
                {
                    BookingId = r.GetInt32(4),
                    DateFrom  = r.GetFieldValue<DateOnly>(5),
                    DateTo    = r.GetFieldValue<DateOnly>(6),
                    Status    = r.GetString(7),
                    GuestName = r.IsDBNull(8) ? "" : r.GetString(8)
                });
            }
        }

        return new ChessGrid
        {
            HotelId     = hotelId,
            WindowStart = windowStart,
            WindowEnd   = windowEnd,
            Rooms       = rooms
        };
    }
}
