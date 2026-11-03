namespace QonaqHotels.Models;

// Строка таблицы services (db/schema.sql)
public class Service
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public int HotelId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string? Unit { get; set; }
}
