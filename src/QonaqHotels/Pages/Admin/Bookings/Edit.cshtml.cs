using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using NpgsqlTypes;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Bookings;

[Authorize(Policy = "RequireReceptionist")]
public class EditModel : PageModel
{
    private readonly Db _db;
    public EditModel(Db db) => _db = db;

    public string GuestName { get; set; } = "";
    public string HotelName { get; set; } = "";
    public int? AssignedRoomId { get; set; }
    public List<(string Value, string Label)> AvailableRooms { get; set; } = [];

    [BindProperty] public int Id { get; set; }
    [BindProperty] public int HotelId { get; set; }
    [BindProperty] public DateOnly CheckInPlan { get; set; }
    [BindProperty] public DateOnly CheckOutPlan { get; set; }
    [BindProperty] public string Status { get; set; } = "";
    [BindProperty] public string? Source { get; set; }
    [BindProperty] public string? CancelReason { get; set; }
    [BindProperty] public int? RoomId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await using var conn = await _db.OpenAsync();

        await using (var cmd = new NpgsqlCommand("""
            SELECT b.id, b.check_in_plan, b.check_out_plan, b.status, b.source, b.cancel_reason,
                   g.full_name, h.name, b.hotel_id,
                   (SELECT br.room_id FROM booking_rooms br
                    WHERE br.booking_id = b.id AND br.is_active
                    ORDER BY br.id LIMIT 1) AS room_id
            FROM bookings b
            JOIN guests g ON g.id = b.guest_id
            JOIN hotels h ON h.id = b.hotel_id
            WHERE b.id = @id
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();

            Id             = r.GetInt32(0);
            CheckInPlan    = r.GetFieldValue<DateOnly>(1);
            CheckOutPlan   = r.GetFieldValue<DateOnly>(2);
            Status         = r.GetString(3);
            Source         = r.IsDBNull(4) ? null : r.GetString(4);
            CancelReason   = r.IsDBNull(5) ? null : r.GetString(5);
            GuestName      = r.GetString(6);
            HotelName      = r.GetString(7);
            HotelId        = r.GetInt32(8);
            AssignedRoomId = r.IsDBNull(9) ? null : r.GetInt32(9);
            RoomId         = AssignedRoomId;
        }

        AvailableRooms = await BookingQueries.LoadAvailableRoomsAsync(
            conn, HotelId, CheckInPlan, CheckOutPlan, excludeBookingId: id);
        return Page();
    }

    // AJAX: свободные номера при смене дат (собственный номер брони считается свободным)
    public async Task<IActionResult> OnGetAvailableRoomsAsync(int id, int hotelId, DateOnly dateFrom, DateOnly dateTo)
    {
        if (dateTo <= dateFrom)
            return new JsonResult(Array.Empty<object>());

        await using var conn = await _db.OpenAsync();
        var rooms = await BookingQueries.LoadAvailableRoomsAsync(conn, hotelId, dateFrom, dateTo, excludeBookingId: id);
        return new JsonResult(rooms.Select(r => new { value = r.Value, label = r.Label }));
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (CheckOutPlan <= CheckInPlan)
            ModelState.AddModelError(nameof(CheckOutPlan), "Дата выезда должна быть позже даты заезда.");

        if (Status == BookingStatus.Cancelled && string.IsNullOrWhiteSpace(CancelReason))
            ModelState.AddModelError(nameof(CancelReason), "Укажите причину отмены.");

        await using var conn = await _db.OpenAsync();

        if (!ModelState.IsValid)
            return await RedisplayAsync(conn);

        // Бронь и её номер меняются вместе: либо всё, либо ничего
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            // FOR UPDATE блокирует строку брони до конца транзакции:
            // два одновременных сохранения одной брони выполнятся по очереди, а не вперемешку.
            // Отель берём из БД, а не из скрытого поля формы.
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT hotel_id FROM bookings WHERE id = @id FOR UPDATE", conn, tx))
            {
                lockCmd.Parameters.AddWithValue("id", Id);
                if (await lockCmd.ExecuteScalarAsync() is not int hotelId) return NotFound();
                HotelId = hotelId;
            }

            await using (var cmd = new NpgsqlCommand("""
                UPDATE bookings
                SET check_in_plan = @ci, check_out_plan = @co, status = @s,
                    source = @src, cancel_reason = @cr
                WHERE id = @id
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("ci",  CheckInPlan);
                cmd.Parameters.AddWithValue("co",  CheckOutPlan);
                cmd.Parameters.AddWithValue("s",   Status);
                cmd.Parameters.AddWithValue("src", string.IsNullOrEmpty(Source) ? DBNull.Value : Source);
                cmd.Parameters.AddWithValue("cr",  Status == BookingStatus.Cancelled ? CancelReason! : DBNull.Value);
                cmd.Parameters.AddWithValue("id",  Id);
                await cmd.ExecuteNonQueryAsync();
            }

            // Отмена освобождает номер: строка booking_rooms остаётся в истории с is_active = false,
            // и EXCLUDE-ограничение перестаёт её учитывать.
            int? keepRoomId = Status == BookingStatus.Cancelled ? null : RoomId;

            if (!await SyncRoomAsync(conn, tx, keepRoomId))
            {
                await tx.RollbackAsync();
                ModelState.AddModelError(nameof(RoomId), "Номер не найден в отеле брони или находится на ремонте.");
                return await RedisplayAsync(conn);
            }

            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
        {
            await tx.RollbackAsync();
            ModelState.AddModelError(
                ex.SqlState == DbErrors.ExclusionViolation ? nameof(RoomId) : "",
                DbErrors.Describe(ex));
            return await RedisplayAsync(conn);
        }

        return RedirectToPage("Index");
    }

    // Приводит booking_rooms к одному активному номеру keepRoomId на даты брони (или ни к одному).
    // false — номер нельзя назначить (чужой отель или ремонт).
    private async Task<bool> SyncRoomAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int? keepRoomId)
    {
        // 1. Снимаем все активные номера брони, кроме того, который остаётся
        await using (var off = new NpgsqlCommand("""
            UPDATE booking_rooms SET is_active = false
            WHERE booking_id = @id AND is_active AND room_id IS DISTINCT FROM @keep
            """, conn, tx))
        {
            off.Parameters.AddWithValue("id", Id);
            off.Parameters.Add(new NpgsqlParameter("keep", NpgsqlDbType.Integer) { Value = (object?)keepRoomId ?? DBNull.Value });
            await off.ExecuteNonQueryAsync();
        }

        if (keepRoomId is null) return true;

        // 2. Тот же номер — переносим даты в существующей строке (история не плодится)
        await using (var move = new NpgsqlCommand("""
            UPDATE booking_rooms SET date_from = @df, date_to = @dt
            WHERE booking_id = @id AND is_active AND room_id = @rid
            """, conn, tx))
        {
            move.Parameters.AddWithValue("id",  Id);
            move.Parameters.AddWithValue("rid", keepRoomId.Value);
            move.Parameters.AddWithValue("df",  CheckInPlan);
            move.Parameters.AddWithValue("dt",  CheckOutPlan);
            if (await move.ExecuteNonQueryAsync() > 0) return true;
        }

        // 3. Новый номер — только из отеля брони и не на ремонте
        await using var add = new NpgsqlCommand("""
            INSERT INTO booking_rooms (booking_id, room_id, date_from, date_to)
            SELECT @id, r.id, @df, @dt
            FROM rooms r
            WHERE r.id = @rid AND r.hotel_id = @h AND r.status <> @repair
            """, conn, tx);
        add.Parameters.AddWithValue("id",     Id);
        add.Parameters.AddWithValue("rid",    keepRoomId.Value);
        add.Parameters.AddWithValue("h",      HotelId);
        add.Parameters.AddWithValue("repair", RoomStatus.Repair);
        add.Parameters.AddWithValue("df",     CheckInPlan);
        add.Parameters.AddWithValue("dt",     CheckOutPlan);
        return await add.ExecuteNonQueryAsync() > 0;
    }

    // Повторный показ формы с ошибками: шапку берём из БД, выбор номера сохраняем
    private async Task<IActionResult> RedisplayAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand("""
            SELECT g.full_name, h.name, b.hotel_id
            FROM bookings b
            JOIN guests g ON g.id = b.guest_id
            JOIN hotels h ON h.id = b.hotel_id
            WHERE b.id = @id
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            GuestName = r.GetString(0);
            HotelName = r.GetString(1);
            HotelId   = r.GetInt32(2);
        }

        AssignedRoomId = RoomId;
        if (CheckOutPlan > CheckInPlan)
            AvailableRooms = await BookingQueries.LoadAvailableRoomsAsync(
                conn, HotelId, CheckInPlan, CheckOutPlan, excludeBookingId: Id);
        return Page();
    }
}
