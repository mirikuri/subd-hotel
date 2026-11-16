using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Bookings;

public record OrderRow(int Id, string Service, string? Unit, decimal Quantity, decimal Price,
                       DateTime OrderedAt, string? Employee);

// Счёт брони: проживание по тарифам + заказанные услуги (service_orders) − скидка по карте лояльности.
// Здесь же услуги добавляются в бронь и удаляются из неё.
[Authorize(Policy = "RequireReceptionist")]
public class BillModel : PageModel
{
    private readonly Db _db;
    public BillModel(Db db) => _db = db;

    public BookingRow Booking { get; private set; } = null!;
    public List<OrderRow> Orders { get; private set; } = [];
    public List<(int Id, string Label)> Services { get; private set; } = [];

    public int Nights { get; private set; }            // ночей в брони
    public int NightsPriced { get; private set; }      // из них с тарифом
    public decimal RoomTotal { get; private set; }     // проживание по тарифам
    public string? LoyaltyLevel { get; private set; }
    public int DiscountPercent { get; private set; }

    public decimal ServicesTotal => Orders.Sum(o => o.Quantity * o.Price);
    public decimal Discount => Math.Round(RoomTotal * DiscountPercent / 100, 2);
    public decimal Total => RoomTotal - Discount + ServicesTotal;

    // Услуги можно заказывать, пока бронь не закрыта
    public bool CanOrder => Booking.Status is not (BookingStatus.Cancelled or BookingStatus.Completed);

    [TempData] public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await using var conn = await _db.OpenAsync();
        var booking = await BookingQueries.LoadRowAsync(conn, id);
        if (booking == null) return NotFound();
        Booking = booking;

        // Проживание: сумма тарифов по каждой ночи активных номеров брони.
        // Ночь без тарифа в сумму не входит — тогда nights_priced < nights.
        await using (var cmd = new NpgsqlCommand("""
            SELECT count(*)::int, count(ra.id)::int, coalesce(sum(ra.price_per_night), 0)
            FROM booking_rooms br
            JOIN rooms r ON r.id = br.room_id
            CROSS JOIN generate_series(br.date_from, br.date_to - 1, interval '1 day') AS n(d)
            LEFT JOIN seasons s ON n.d::date BETWEEN s.date_start AND s.date_end
            LEFT JOIN rates ra  ON ra.season_id = s.id AND ra.room_type_id = r.room_type_id
            WHERE br.booking_id = @id AND br.is_active
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            Nights       = r.GetInt32(0);
            NightsPriced = r.GetInt32(1);
            RoomTotal    = r.GetDecimal(2);
        }

        // Скидка на проживание по уровню карты гостя
        await using (var cmd = new NpgsqlCommand("""
            SELECT lc.level
            FROM bookings b JOIN loyalty_cards lc ON lc.guest_id = b.guest_id
            WHERE b.id = @id
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            LoyaltyLevel = await cmd.ExecuteScalarAsync() as string;
            DiscountPercent = LoyaltyLevel switch { "silver" => 5, "gold" => 10, "platinum" => 15, _ => 0 };
        }

        await using (var cmd = new NpgsqlCommand("""
            SELECT so.id, s.name, s.unit, so.quantity, so.price_at_order, so.order_date, e.full_name
            FROM service_orders so
            JOIN services s       ON s.id = so.service_id
            LEFT JOIN employees e ON e.id = so.employee_id
            WHERE so.booking_id = @id
            ORDER BY so.order_date, so.id
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Orders.Add(new OrderRow(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                    r.GetDecimal(3), r.GetDecimal(4), r.GetDateTime(5), r.IsDBNull(6) ? null : r.GetString(6)));
        }

        // Услуги отеля этой брони, которые сейчас продаются
        await using (var cmd = new NpgsqlCommand("""
            SELECT s.id, c.name || ' · ' || s.name || ' — $' || s.price || coalesce(' / ' || s.unit, '')
            FROM services s
            JOIN service_categories c ON c.id = s.category_id
            JOIN bookings b           ON b.hotel_id = s.hotel_id
            WHERE b.id = @id AND s.is_active
            ORDER BY c.name, s.name
            """, conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) Services.Add((r.GetInt32(0), r.GetString(1)));
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(int id, int serviceId, decimal quantity)
    {
        if (quantity <= 0 || quantity > 1000)
        {
            Error = "Количество — от 0 до 1000.";
            return RedirectToPage(new { id });
        }

        // Кто оформил заказ; у service_orders.employee_id допускается NULL
        int? userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var u) ? u : null;

        await using var conn = await _db.OpenAsync();

        // Цена копируется в price_at_order: позже цену услуги могут изменить, а счёт меняться не должен.
        // Услуга берётся только из отеля брони и только продающаяся; бронь не должна быть закрыта.
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO service_orders (booking_id, service_id, employee_id, quantity, price_at_order)
            SELECT b.id, s.id, (SELECT employee_id FROM users WHERE id = @user), @q, s.price
            FROM bookings b
            JOIN services s ON s.hotel_id = b.hotel_id
            WHERE b.id = @id AND s.id = @sid AND s.is_active
              AND b.status NOT IN (@cancelled, @completed)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("sid", serviceId);
        cmd.Parameters.AddWithValue("q", quantity);
        cmd.Parameters.AddWithValue("user", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cancelled", BookingStatus.Cancelled);
        cmd.Parameters.AddWithValue("completed", BookingStatus.Completed);

        if (await cmd.ExecuteNonQueryAsync() == 0)
            Error = "Услугу добавить нельзя: бронь закрыта либо услуга не из этого отеля или снята с продажи.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id, int orderId)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            DELETE FROM service_orders so
            USING bookings b
            WHERE so.id = @oid AND so.booking_id = @id AND b.id = so.booking_id
              AND b.status NOT IN (@cancelled, @completed)
            """, conn);
        cmd.Parameters.AddWithValue("oid", orderId);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("cancelled", BookingStatus.Cancelled);
        cmd.Parameters.AddWithValue("completed", BookingStatus.Completed);

        if (await cmd.ExecuteNonQueryAsync() == 0)
            Error = "Заказ не найден или бронь уже закрыта.";
        return RedirectToPage(new { id });
    }
}
