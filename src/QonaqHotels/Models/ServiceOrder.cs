namespace QonaqHotels.Models;

// Строка таблицы service_orders (db/schema.sql)
public class ServiceOrder
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public int ServiceId { get; set; }
    public int? EmployeeId { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal PriceAtOrder { get; set; }
}
