using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Services;

public enum SiteBookingOutcome { Success, Invalid, GuestDisabled, TooManyRequests, NoRooms }

public record SiteBookingResult(SiteBookingOutcome Outcome, int? BookingId, string? Message);

public record GuestBookingRow(
    int Id, string HotelName, string RoomTypeName, string? RoomNumber,
    DateOnly From, DateOnly To, int Guests, decimal? Total, string Status);

// Заявки на бронь с сайта: гость выбирает отель и тип номера, конкретный номер назначается сразу.
// Защиту «один номер — одна бронь» по-прежнему даёт только EXCLUDE-ограничение booking_rooms:
// если два гостя одновременно взяли последний номер, второй INSERT дождётся первого и получит 23P01.
public class SiteBookingService
{
    public const int MaxOpenRequests = 3;   // необработанных заявок с сайта на одного гостя
    public const int MaxAttempts     = 3;   // сколько раз пробуем взять следующий свободный номер
    public const int MaxComment      = 500; // bookings.guest_comment varchar(500)

    private readonly Db _db;
    public SiteBookingService(Db db) => _db = db;

    public async Task<SiteBookingResult> SubmitAsync(
        int guestId, int hotelId, int roomTypeId, DateOnly from, DateOnly to, int guests, string? comment)
    {
        if (RoomSearch.Validate(from, to, guests, Clock.Today) is { } error)
            return new(SiteBookingOutcome.Invalid, null, error);

        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (comment?.Length > MaxComment)
            return new(SiteBookingOutcome.Invalid, null, $"Пожелания — не длиннее {MaxComment} символов.");

        await using var conn = await _db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // FOR UPDATE на строке гостя: его параллельные заявки выполняются по очереди,
        // поэтому счётчик необработанных заявок ниже не обойти одновременной отправкой.
        await using (var g = new NpgsqlCommand(
            "SELECT is_active FROM guests WHERE id = @g FOR UPDATE", conn, tx))
        {
            g.Parameters.AddWithValue("g", guestId);
            if (await g.ExecuteScalarAsync() is not true)
                return new(SiteBookingOutcome.GuestDisabled, null, "Учётная запись отключена.");
        }

        await using (var cnt = new NpgsqlCommand("""
            SELECT count(*) FROM bookings
            WHERE guest_id = @g AND status = @new AND source = 'сайт' AND check_in_plan >= @today
            """, conn, tx))
        {
            cnt.Parameters.AddWithValue("g",     guestId);
            cnt.Parameters.AddWithValue("new",   BookingStatus.New);
            cnt.Parameters.AddWithValue("today", Clock.Today);
            if ((long)(await cnt.ExecuteScalarAsync())! >= MaxOpenRequests)
                return new(SiteBookingOutcome.TooManyRequests, null, "У вас уже 3 необработанные заявки.");
        }

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            // Номер для этой попытки. Тип без свободных номеров — сразу «не осталось».
            var roomId = await RoomSearch.FirstFreeRoomAsync(conn, tx, hotelId, roomTypeId, from, to);
            if (roomId is null) break;

            // Точка сохранения: при 23P01 откатываем только эту попытку, блокировка гостя остаётся
            await tx.SaveAsync("pick");
            try
            {
                var bookingId = await InsertAsync(conn, tx, guestId, hotelId, roomId.Value, from, to, guests, comment);
                await tx.CommitAsync();
                return new(SiteBookingOutcome.Success, bookingId,
                    $"Заявка №{bookingId} отправлена, администратор свяжется с вами.");
            }
            catch (PostgresException ex) when (ex.SqlState is DbErrors.ExclusionViolation or DbErrors.DeadlockDetected)
            {
                // Номер только что занял кто-то другой — берём следующий.
                // 40P01: одновременные вставки под EXCLUDE ждали друг друга; откат к точке сохранения снимает и её.
                await tx.RollbackAsync("pick");
            }
        }

        return new(SiteBookingOutcome.NoRooms, null, "Свободных номеров этого типа на эти даты не осталось.");
    }

    private static async Task<int> InsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, int guestId, int hotelId, int roomId,
        DateOnly from, DateOnly to, int guests, string? comment)
    {
        int bookingId;
        // status не передаём — DEFAULT 'новая'
        await using (var b = new NpgsqlCommand("""
            INSERT INTO bookings (guest_id, hotel_id, check_in_plan, check_out_plan, source, guest_comment)
            VALUES (@g, @h, @f, @t, 'сайт', @c)
            RETURNING id
            """, conn, tx))
        {
            b.Parameters.AddWithValue("g", guestId);
            b.Parameters.AddWithValue("h", hotelId);
            b.Parameters.AddWithValue("f", from);
            b.Parameters.AddWithValue("t", to);
            b.Parameters.AddWithValue("c", (object?)comment ?? DBNull.Value);
            bookingId = (int)(await b.ExecuteScalarAsync())!;
        }

        await using var br = new NpgsqlCommand("""
            INSERT INTO booking_rooms (booking_id, room_id, hotel_id, date_from, date_to, guests_count)
            VALUES (@b, @r, @h, @f, @t, @n)
            """, conn, tx);
        br.Parameters.AddWithValue("b", bookingId);
        br.Parameters.AddWithValue("r", roomId);
        br.Parameters.AddWithValue("h", hotelId);
        br.Parameters.AddWithValue("f", from);
        br.Parameters.AddWithValue("t", to);
        br.Parameters.AddWithValue("n", (short)guests);
        await br.ExecuteNonQueryAsync();
        return bookingId;
    }

    // Брони гостя, новые сверху. Номер — последняя строка booking_rooms (у отменённой она неактивна).
    // Сумма — по текущим тарифам; null, если на какую-то ночь тарифа нет.
    public async Task<List<GuestBookingRow>> ListAsync(int guestId)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT b.id, h.name, rt.name, r.room_number, b.check_in_plan, b.check_out_plan,
                   coalesce(br.guests_count, 1), p.total, b.status
            FROM bookings b
            JOIN hotels h ON h.id = b.hotel_id
            LEFT JOIN LATERAL (
                SELECT x.room_id, x.guests_count FROM booking_rooms x
                WHERE x.booking_id = b.id ORDER BY x.is_active DESC, x.id DESC LIMIT 1
            ) br ON true
            LEFT JOIN rooms r       ON r.id = br.room_id
            LEFT JOIN room_types rt ON rt.id = r.room_type_id
            LEFT JOIN LATERAL (
                SELECT CASE WHEN count(*) = b.check_out_plan - b.check_in_plan
                            THEN sum(ra.price_per_night) END AS total
                FROM generate_series(b.check_in_plan, b.check_out_plan - 1, interval '1 day') AS g(d)
                JOIN seasons s ON g.d::date BETWEEN s.date_start AND s.date_end
                JOIN rates ra  ON ra.season_id = s.id AND ra.room_type_id = r.room_type_id
            ) p ON true
            WHERE b.guest_id = @g
            ORDER BY b.booking_date DESC
            """, conn);
        cmd.Parameters.AddWithValue("g", guestId);

        var list = new List<GuestBookingRow>();
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            list.Add(new GuestBookingRow(
                Id:           rd.GetInt32(0),
                HotelName:    rd.GetString(1),
                RoomTypeName: rd.IsDBNull(2) ? "—" : rd.GetString(2),
                RoomNumber:   rd.IsDBNull(3) ? null : rd.GetString(3),
                From:         rd.GetFieldValue<DateOnly>(4),
                To:           rd.GetFieldValue<DateOnly>(5),
                Guests:       rd.GetInt16(6),
                Total:        rd.IsDBNull(7) ? null : rd.GetDecimal(7),
                Status:       rd.GetString(8)));
        return list;
    }

    // Гость отменяет свою заявку, пока её не обработали (статус «новая»).
    // Проверка владельца и статуса — в самом UPDATE, а не отдельным SELECT.
    public async Task<bool> CancelAsync(int guestId, int bookingId)
    {
        await using var conn = await _db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var b = new NpgsqlCommand("""
            UPDATE bookings SET status = @cancelled, cancel_reason = 'Отменено гостем'
            WHERE id = @id AND guest_id = @g AND status = @new
            """, conn, tx))
        {
            b.Parameters.AddWithValue("cancelled", BookingStatus.Cancelled);
            b.Parameters.AddWithValue("new",       BookingStatus.New);
            b.Parameters.AddWithValue("id",        bookingId);
            b.Parameters.AddWithValue("g",         guestId);
            if (await b.ExecuteNonQueryAsync() == 0) return false;
        }

        // Отмена освобождает номер: строка остаётся в истории, EXCLUDE её больше не учитывает
        await using (var br = new NpgsqlCommand(
            "UPDATE booking_rooms SET is_active = false WHERE booking_id = @id", conn, tx))
        {
            br.Parameters.AddWithValue("id", bookingId);
            await br.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return true;
    }
}
