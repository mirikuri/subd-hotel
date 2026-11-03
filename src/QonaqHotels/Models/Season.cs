namespace QonaqHotels.Models;

// Строка таблицы seasons (db/schema.sql)
public class Season
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateOnly DateStart { get; set; }
    public DateOnly DateEnd { get; set; }
}
