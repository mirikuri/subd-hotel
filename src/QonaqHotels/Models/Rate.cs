namespace QonaqHotels.Models;

// Строка таблицы rates (db/schema.sql)
public class Rate
{
    public int Id { get; set; }
    public int RoomTypeId { get; set; }
    public int SeasonId { get; set; }
    public decimal PricePerNight { get; set; }
}
