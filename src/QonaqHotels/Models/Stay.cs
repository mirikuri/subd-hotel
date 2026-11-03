namespace QonaqHotels.Models;

// Строка таблицы stays (db/schema.sql)
public class Stay
{
    public int Id { get; set; }
    public int BookingRoomId { get; set; }
    public DateTime ActualCheckIn { get; set; }
    public DateTime? ActualCheckOut { get; set; }
    public int EmployeeId { get; set; }
}
