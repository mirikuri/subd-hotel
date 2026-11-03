namespace QonaqHotels.Models;

// Строка таблицы guests (db/schema.sql)
public class Guest
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public DateOnly? BirthDate { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public int? CountryId { get; set; }
    public string? Login { get; set; }
    public string? PasswordHash { get; set; }
    public bool IsActive { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime RegisteredAt { get; set; }
}
