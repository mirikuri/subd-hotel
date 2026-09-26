using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("bookings")]
public partial class Booking
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("guest_id")]
    public int GuestId { get; set; }

    [Column("hotel_id")]
    public int HotelId { get; set; }

    [Column("booking_date", TypeName = "timestamp without time zone")]
    public DateTime BookingDate { get; set; }

    [Column("check_in_plan")]
    public DateOnly CheckInPlan { get; set; }

    [Column("check_out_plan")]
    public DateOnly CheckOutPlan { get; set; }

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = null!;

    [Column("source")]
    [StringLength(30)]
    public string? Source { get; set; }

    [Column("cancel_reason")]
    public string? CancelReason { get; set; }

    [InverseProperty("Booking")]
    public virtual ICollection<BookingRoom> BookingRooms { get; set; } = new List<BookingRoom>();

    [ForeignKey("GuestId")]
    [InverseProperty("Bookings")]
    public virtual Guest Guest { get; set; } = null!;

    [ForeignKey("HotelId")]
    [InverseProperty("Bookings")]
    public virtual Hotel Hotel { get; set; } = null!;

    [InverseProperty("Booking")]
    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    [InverseProperty("Booking")]
    public virtual ICollection<ServiceOrder> ServiceOrders { get; set; } = new List<ServiceOrder>();
}
