namespace QonaqHotels.Models;

// Строка таблицы amenities (db/schema.sql)
public class Amenity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Icon { get; set; }   // имя значка Bootstrap Icons
}
