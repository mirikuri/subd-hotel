using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Services;

public record ArrivalRow(int BookingRoomId, int BookingId, string Guest, string Room, string RoomType,
                         DateOnly From, DateOnly To, short Guests, string Status);
public record InHouseRow(int StayId, int BookingId, string Guest, string Room, string RoomType,
                         DateTime CheckedInAt, DateOnly PlannedOut, string Employee);
public record CleaningRow(int RoomId, string Room, string RoomType);

// Заселение и выезд. Факт проживания — строка stays; статус брони и статус номера
// меняются в той же транзакции, поэтому «заселён» в брони всегда означает открытое заселение.
//
//   заселить:  stays += строка, bookings.status = 'заселён',   rooms.status = 'занят'
//   выселить:  stays.actual_check_out = now(), rooms.status = 'уборка',
//              bookings.status = 'завершена', когда выехали из всех номеров брони
//   убрано:    rooms.status = 'свободен'
public class StayService
{
    private readonly Db _db;
    public StayService(Db db) => _db = db;

    // Кого можно заселить сегодня: бронь «новая»/«подтверждена», сегодня входит в её даты
    public async Task<List<ArrivalRow>> ArrivalsAsync(int hotelId, DateOnly today)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT br.id, b.id, g.full_name, r.room_number, rt.name,
                   br.date_from, br.date_to, br.guests_count, b.status
            FROM booking_rooms br
            JOIN bookings b    ON b.id = br.booking_id
            JOIN guests g      ON g.id = b.guest_id
            JOIN rooms r       ON r.id = br.room_id
            JOIN room_types rt ON rt.id = r.room_type_id
            WHERE br.hotel_id = @h AND br.is_active
              AND b.status IN (@new, @confirmed)
              AND br.date_from <= @today AND @today < br.date_to
            ORDER BY r.room_number
            """, conn);
        cmd.Parameters.AddWithValue("h", hotelId);
        cmd.Parameters.AddWithValue("today", today);
        cmd.Parameters.AddWithValue("new", BookingStatus.New);
        cmd.Parameters.AddWithValue("confirmed", BookingStatus.Confirmed);

        var list = new List<ArrivalRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new ArrivalRow(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetFieldValue<DateOnly>(5), r.GetFieldValue<DateOnly>(6), r.GetInt16(7), r.GetString(8)));
        return list;
    }

    // Кто сейчас проживает: открытые заселения
    public async Task<List<InHouseRow>> InHouseAsync(int hotelId)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT s.id, b.id, g.full_name, r.room_number, rt.name, s.actual_check_in, br.date_to, e.full_name
            FROM stays s
            JOIN booking_rooms br ON br.id = s.booking_room_id
            JOIN bookings b       ON b.id = br.booking_id
            JOIN guests g         ON g.id = b.guest_id
            JOIN rooms r          ON r.id = br.room_id
            JOIN room_types rt    ON rt.id = r.room_type_id
            JOIN employees e      ON e.id = s.employee_id
            WHERE br.hotel_id = @h AND s.actual_check_out IS NULL
            ORDER BY br.date_to, r.room_number
            """, conn);
        cmd.Parameters.AddWithValue("h", hotelId);

        var list = new List<InHouseRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new InHouseRow(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetDateTime(5), r.GetFieldValue<DateOnly>(6), r.GetString(7)));
        return list;
    }

    public async Task<List<CleaningRow>> CleaningAsync(int hotelId)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT r.id, r.room_number, rt.name
            FROM rooms r JOIN room_types rt ON rt.id = r.room_type_id
            WHERE r.hotel_id = @h AND r.status = @cleaning
            ORDER BY r.room_number
            """, conn);
        cmd.Parameters.AddWithValue("h", hotelId);
        cmd.Parameters.AddWithValue("cleaning", RoomStatus.Cleaning);

        var list = new List<CleaningRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new CleaningRow(r.GetInt32(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    // null — заселён; иначе текст, почему нельзя. userId — users.id сотрудника, который оформляет.
    public async Task<string?> CheckInAsync(int bookingRoomId, int userId, DateOnly today)
    {
        await using var conn = await _db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            // Условия заселения — в самом INSERT: если бронь уже заселили, отменили или
            // дата не подошла, вставится 0 строк. Повторное заселение ловит ещё и индекс stays_one_open_key.
            int? roomId = null, bookingId = null;
            await using (var ins = new NpgsqlCommand("""
                WITH new_stay AS (
                    INSERT INTO stays (booking_room_id, employee_id)
                    SELECT br.id, u.employee_id
                    FROM booking_rooms br
                    JOIN bookings b ON b.id = br.booking_id
                    JOIN rooms r    ON r.id = br.room_id
                    JOIN users u    ON u.id = @user
                    WHERE br.id = @br AND br.is_active
                      AND b.status IN (@new, @confirmed)
                      AND br.date_from <= @today AND @today < br.date_to
                      AND r.status <> @repair
                    RETURNING booking_room_id
                )
                SELECT br.room_id, br.booking_id
                FROM new_stay JOIN booking_rooms br ON br.id = new_stay.booking_room_id
                """, conn, tx))
            {
                ins.Parameters.AddWithValue("br", bookingRoomId);
                ins.Parameters.AddWithValue("user", userId);
                ins.Parameters.AddWithValue("today", today);
                ins.Parameters.AddWithValue("new", BookingStatus.New);
                ins.Parameters.AddWithValue("confirmed", BookingStatus.Confirmed);
                ins.Parameters.AddWithValue("repair", RoomStatus.Repair);
                await using var r = await ins.ExecuteReaderAsync();
                if (await r.ReadAsync()) { roomId = r.GetInt32(0); bookingId = r.GetInt32(1); }
            }

            if (roomId is null)
            {
                await tx.RollbackAsync();
                return "Заселить нельзя: бронь уже обработана, отменена, номер на ремонте или сегодня не входит в даты брони.";
            }

            await ExecAsync(conn, tx, "UPDATE bookings SET status = @s WHERE id = @id",
                ("s", BookingStatus.CheckedIn), ("id", bookingId.Value));
            await ExecAsync(conn, tx, "UPDATE rooms SET status = @s WHERE id = @id",
                ("s", RoomStatus.Occupied), ("id", roomId.Value));
            await tx.CommitAsync();
            return null;
        }
        catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
        {
            await tx.RollbackAsync();
            return DbErrors.Describe(ex);
        }
    }

    // null — выселен; иначе текст ошибки
    public async Task<string?> CheckOutAsync(int stayId)
    {
        await using var conn = await _db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        int roomId, bookingId;
        await using (var upd = new NpgsqlCommand("""
            WITH closed AS (
                UPDATE stays SET actual_check_out = localtimestamp
                WHERE id = @id AND actual_check_out IS NULL
                RETURNING booking_room_id
            )
            SELECT br.room_id, br.booking_id
            FROM closed JOIN booking_rooms br ON br.id = closed.booking_room_id
            """, conn, tx))
        {
            upd.Parameters.AddWithValue("id", stayId);
            await using var r = await upd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                await r.CloseAsync();
                await tx.RollbackAsync();
                return "Гость уже выселен.";
            }
            roomId = r.GetInt32(0); bookingId = r.GetInt32(1);
        }

        await ExecAsync(conn, tx, "UPDATE rooms SET status = @s WHERE id = @id",
            ("s", RoomStatus.Cleaning), ("id", roomId));

        // Бронь завершена, когда не осталось открытых заселений ни в одном её номере
        await ExecAsync(conn, tx, """
            UPDATE bookings b SET status = @s
            WHERE b.id = @id
              AND NOT EXISTS (
                  SELECT 1 FROM stays s
                  JOIN booking_rooms br ON br.id = s.booking_room_id
                  WHERE br.booking_id = b.id AND s.actual_check_out IS NULL)
            """, ("s", BookingStatus.Completed), ("id", bookingId));

        await tx.CommitAsync();
        return null;
    }

    // Уборка закончена — номер снова «свободен». false: номер не на уборке.
    public async Task<bool> MarkCleanAsync(int roomId)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE rooms SET status = @free WHERE id = @id AND status = @cleaning", conn);
        cmd.Parameters.AddWithValue("free", RoomStatus.Free);
        cmd.Parameters.AddWithValue("cleaning", RoomStatus.Cleaning);
        cmd.Parameters.AddWithValue("id", roomId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql,
                                        params (string, object)[] ps)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }
}
