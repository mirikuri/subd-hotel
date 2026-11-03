namespace QonaqHotels.Models;

// Строка таблицы booking_rooms (db/schema.sql)
public class BookingRoom
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public int RoomId { get; set; }
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public short GuestsCount { get; set; }
    public bool IsActive { get; set; }
}
