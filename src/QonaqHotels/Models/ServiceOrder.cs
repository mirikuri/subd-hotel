using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("service_orders")]
public partial class ServiceOrder
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("booking_id")]
    public int BookingId { get; set; }

    [Column("service_id")]
    public int ServiceId { get; set; }

    [Column("employee_id")]
    public int? EmployeeId { get; set; }

    [Column("order_date", TypeName = "timestamp without time zone")]
    public DateTime OrderDate { get; set; }

    [Column("quantity")]
    [Precision(8, 2)]
    public decimal Quantity { get; set; }

    [Column("price_at_order")]
    [Precision(10, 2)]
    public decimal PriceAtOrder { get; set; }

    [ForeignKey("BookingId")]
    [InverseProperty("ServiceOrders")]
    public virtual Booking Booking { get; set; } = null!;

    [ForeignKey("EmployeeId")]
    [InverseProperty("ServiceOrders")]
    public virtual Employee? Employee { get; set; }

    [ForeignKey("ServiceId")]
    [InverseProperty("ServiceOrders")]
    public virtual Service Service { get; set; } = null!;
}
