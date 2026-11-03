namespace QonaqHotels.Models;

// Строка таблицы employees (db/schema.sql)
public class Employee
{
    public int Id { get; set; }
    public int HotelId { get; set; }
    public int PositionId { get; set; }
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public DateOnly HireDate { get; set; }
    public DateOnly? DismissalDate { get; set; }
}
