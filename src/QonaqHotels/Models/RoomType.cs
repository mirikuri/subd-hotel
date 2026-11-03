namespace QonaqHotels.Models;

// Строка таблицы room_types (db/schema.sql)
public class RoomType
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public short Capacity { get; set; }
    public string? Description { get; set; }
}
