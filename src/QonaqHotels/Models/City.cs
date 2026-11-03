namespace QonaqHotels.Models;

// Строка таблицы cities (db/schema.sql)
public class City
{
    public int Id { get; set; }
    public int CountryId { get; set; }
    public string Name { get; set; } = "";
}
