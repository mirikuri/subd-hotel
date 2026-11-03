namespace QonaqHotels.Models;

// Строка таблицы positions (db/schema.sql)
public class Position
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "ресепшен";
    public decimal? BaseSalary { get; set; }
    public decimal? HourlyRate { get; set; }
}
