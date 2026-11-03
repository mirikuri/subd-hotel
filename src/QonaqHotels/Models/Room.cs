namespace QonaqHotels.Models;

// Строка таблицы rooms (db/schema.sql)
public class Room
{
    public int Id { get; set; }
    public int HotelId { get; set; }
    public int RoomTypeId { get; set; }
    public string RoomNumber { get; set; } = "";
    public short? Floor { get; set; }
    public string Status { get; set; } = "";
}
