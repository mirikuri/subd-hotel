using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Bookings;

[Authorize(Policy = "RequireReceptionist")]
public class CreateModel : PageModel
{
    private readonly Db _db;
    public CreateModel(Db db) => _db = db;

    [BindProperty] public int GuestId { get; set; }
    [BindProperty] public int HotelId { get; set; }
    [BindProperty] public DateOnly CheckInPlan { get; set; } = Clock.Today;
    [BindProperty] public DateOnly CheckOutPlan { get; set; } = Clock.Today.AddDays(1);
    [BindProperty] public string? Source { get; set; }
    [BindProperty] public int? RoomId { get; set; }

    public SelectList Guests { get; set; } = null!;
    public SelectList Hotels { get; set; } = null!;
    public List<(string Value, string Label)> AvailableRooms { get; set; } = [];

    // Шахматка открывает форму с уже выбранными номером, отелем и датами
    public async Task OnGetAsync(int? roomId, int? hotelId, DateOnly? dateFrom, DateOnly? dateTo)
    {
        if (hotelId.HasValue)  HotelId      = hotelId.Value;
        if (dateFrom.HasValue) CheckInPlan  = dateFrom.Value;
        if (dateTo.HasValue)   CheckOutPlan = dateTo.Value;
        if (roomId.HasValue)   RoomId       = roomId.Value;

        await using var conn = await _db.OpenAsync();
        await LoadFormAsync(conn);
    }

    // AJAX: свободные номера при смене отеля или дат
    public async Task<IActionResult> OnGetAvailableRoomsAsync(int hotelId, DateOnly dateFrom, DateOnly dateTo)
    {
        if (dateTo <= dateFrom)
            return new JsonResult(Array.Empty<object>());

        await using var conn = await _db.OpenAsync();
        var rooms = await BookingQueries.LoadAvailableRoomsAsync(conn, hotelId, dateFrom, dateTo);
        return new JsonResult(rooms.Select(r => new { value = r.Value, label = r.Label }));
    }

    // AJAX: занятые периоды выбранного номера (для подсветки в календаре).
    // Те же строки, что проверяет EXCLUDE-ограничение: активные booking_rooms этого номера.
    public async Task<IActionResult> OnGetOccupiedDatesAsync(int roomId)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT date_from, date_to
            FROM booking_rooms
            WHERE room_id = @roomId
              AND is_active
            ORDER BY date_from
            """, conn);
        cmd.Parameters.AddWithValue("roomId", roomId);

        var ranges = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            ranges.Add(new
            {
                from = r.GetFieldValue<DateOnly>(0).ToString("yyyy-MM-dd"),
                to   = r.GetFieldValue<DateOnly>(1).ToString("yyyy-MM-dd")
            });
        }
        return new JsonResult(ranges);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var today   = Clock.Today;
        var maxDate = today.AddYears(1);

        if (CheckOutPlan <= CheckInPlan)
            ModelState.AddModelError(nameof(CheckOutPlan), "Дата выезда должна быть позже даты заезда.");
        if (CheckInPlan < today || CheckOutPlan < today)
            ModelState.AddModelError("", "Нельзя бронировать прошедшие даты.");
        if (CheckInPlan > maxDate || CheckOutPlan > maxDate)
            ModelState.AddModelError("", "Бронирование возможно максимум за год вперёд.");

        await using var conn = await _db.OpenAsync();

        if (!ModelState.IsValid)
            return await RedisplayAsync(conn);

        // Бронь и её номер — одна логическая операция: либо записываются обе строки, либо ни одной.
        // Без транзакции при занятом номере осталась бы бронь без номера.
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            // booking_date и status не передаём — их ставят DEFAULT now() и DEFAULT 'новая' в схеме
            int bookingId;
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO bookings (guest_id, hotel_id, check_in_plan, check_out_plan, source)
                VALUES (@g, @h, @ci, @co, @src)
                RETURNING id
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("g",   GuestId);
                cmd.Parameters.AddWithValue("h",   HotelId);
                cmd.Parameters.AddWithValue("ci",  CheckInPlan);
                cmd.Parameters.AddWithValue("co",  CheckOutPlan);
                cmd.Parameters.AddWithValue("src", string.IsNullOrEmpty(Source) ? DBNull.Value : Source);
                bookingId = (int)(await cmd.ExecuteScalarAsync())!;
            }

            if (RoomId.HasValue)
            {
                // Номер берём только из отеля брони и не на ремонте — иначе 0 строк.
                // Пересечение с другими бронями проверит EXCLUDE-ограничение (ошибка 23P01).
                await using var rc = new NpgsqlCommand("""
                    INSERT INTO booking_rooms (booking_id, room_id, date_from, date_to)
                    SELECT @bid, r.id, @df, @dt
                    FROM rooms r
                    WHERE r.id = @rid AND r.hotel_id = @h AND r.status <> @repair
                    """, conn, tx);
                rc.Parameters.AddWithValue("bid",    bookingId);
                rc.Parameters.AddWithValue("rid",    RoomId.Value);
                rc.Parameters.AddWithValue("h",      HotelId);
                rc.Parameters.AddWithValue("repair", RoomStatus.Repair);
                rc.Parameters.AddWithValue("df",     CheckInPlan);
                rc.Parameters.AddWithValue("dt",     CheckOutPlan);

                if (await rc.ExecuteNonQueryAsync() == 0)
                {
                    await tx.RollbackAsync();
                    ModelState.AddModelError(nameof(RoomId), "Номер не найден в выбранном отеле или находится на ремонте.");
                    return await RedisplayAsync(conn);
                }
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

    private async Task<IActionResult> RedisplayAsync(NpgsqlConnection conn)
    {
        await LoadFormAsync(conn);
        return Page();
    }

    private async Task LoadFormAsync(NpgsqlConnection conn)
    {
        Guests = await AdminLists.SelectListAsync(conn, AdminLists.Guests);
        Hotels = await AdminLists.SelectListAsync(conn, AdminLists.Hotels);

        if (HotelId > 0 && CheckOutPlan > CheckInPlan)
            AvailableRooms = await BookingQueries.LoadAvailableRoomsAsync(conn, HotelId, CheckInPlan, CheckOutPlan);
    }
}
