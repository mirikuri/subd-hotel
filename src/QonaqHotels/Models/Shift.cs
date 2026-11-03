namespace QonaqHotels.Models;

// Строка таблицы shifts (db/schema.sql)
public class Shift
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateTime ShiftStart { get; set; }
    public DateTime ShiftEnd { get; set; }
    public decimal? HoursWorked { get; set; }
}
