using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Bookings;

// SQL, который нужен сразу нескольким страницам раздела «Брони».
public static class BookingQueries
{
    // Свободные номера отеля на период [dateFrom, dateTo).
    // Условие «свободен» — общее с сайтом: RoomSearch.FreeRoomCondition (совпадает с EXCLUDE-ограничением).
    // Статус брони здесь не смотрим: отмена брони освобождает номер через is_active = false.
    private static readonly string AvailableRoomsSql = $"""
        SELECT r.id::text,
               r.room_number || ' · ' || rt.name ||
               CASE WHEN r.floor IS NOT NULL THEN ' (эт. ' || r.floor || ')' ELSE '' END
        FROM rooms r
        JOIN room_types rt ON rt.id = r.room_type_id
        WHERE r.hotel_id = @hotelId
          AND {RoomSearch.FreeRoomCondition}
        ORDER BY r.floor NULLS LAST, r.room_number
        """;

    // excludeBookingId — бронь, которую редактируем: её собственный номер считаем свободным.
    // id в БД начинаются с 1, поэтому 0 значит «никого не исключать».
    public static async Task<List<(string Value, string Label)>> LoadAvailableRoomsAsync(
        NpgsqlConnection conn, int hotelId, DateOnly dateFrom, DateOnly dateTo, int excludeBookingId = 0)
    {
        await using var cmd = new NpgsqlCommand(AvailableRoomsSql, conn);
        cmd.Parameters.AddWithValue("hotelId", hotelId);
        RoomSearch.AddPeriod(cmd, dateFrom, dateTo, excludeBookingId);

        var list = new List<(string, string)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add((r.GetString(0), r.GetString(1)));
        return list;
    }

    // Карточка брони для страниц просмотра/удаления
    public static async Task<BookingRow?> LoadRowAsync(NpgsqlConnection conn, int id)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT b.id, g.full_name, h.name, b.check_in_plan, b.check_out_plan, b.status, b.source
            FROM bookings b
            JOIN guests g ON g.id = b.guest_id
            JOIN hotels h ON h.id = b.hotel_id
            WHERE b.id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        return new BookingRow
        {
            Id           = r.GetInt32(0),
            GuestName    = r.GetString(1),
            HotelName    = r.GetString(2),
            CheckInPlan  = r.GetFieldValue<DateOnly>(3),
            CheckOutPlan = r.GetFieldValue<DateOnly>(4),
            Status       = r.GetString(5),
            Source       = r.IsDBNull(6) ? null : r.GetString(6)
        };
    }
}
