namespace QonaqHotels.Models;

// Строка таблицы users (db/schema.sql)
public class User
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Login { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsActive { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }
    // заполняются JOIN-запросом, не хранятся в БД
    public string EmployeeName { get; set; } = "";
    public DateOnly? DismissalDate { get; set; }
    public int EmployeeHotelId { get; set; }
}
