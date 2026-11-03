namespace QonaqHotels.Models;

// Строка таблицы audit_log (db/schema.sql)
public class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string Action { get; set; } = "";
    public string TableName { get; set; } = "";
    public int? RecordId { get; set; }
    public DateTime ChangedAt { get; set; }
}
