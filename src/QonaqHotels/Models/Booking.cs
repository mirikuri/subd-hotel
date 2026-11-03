namespace QonaqHotels.Models;

// Строка таблицы bookings (db/schema.sql)
public class Booking
{
    public int Id { get; set; }
    public int GuestId { get; set; }
    public int HotelId { get; set; }
    public DateTime BookingDate { get; set; }
    public DateOnly CheckInPlan { get; set; }
    public DateOnly CheckOutPlan { get; set; }
    public string Status { get; set; } = "";
    public string? Source { get; set; }
    public string? CancelReason { get; set; }
}
