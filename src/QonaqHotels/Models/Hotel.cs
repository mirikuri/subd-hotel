namespace QonaqHotels.Models;

// Строка таблицы hotels (db/schema.sql)
public class Hotel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int CityId { get; set; }
    public string Address { get; set; } = "";
    public short Stars { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public TimeOnly CheckInTime { get; set; } = new(14, 0);
    public TimeOnly CheckOutTime { get; set; } = new(12, 0);
    public string? Description { get; set; }
    // заполняется JOIN-запросом, не хранится в БД
    public string CityName { get; set; } = "";
}
