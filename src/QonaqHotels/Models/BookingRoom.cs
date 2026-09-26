using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("booking_rooms")]
public partial class BookingRoom
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("booking_id")]
    public int BookingId { get; set; }

    [Column("room_id")]
    public int RoomId { get; set; }

    [Column("date_from")]
    public DateOnly DateFrom { get; set; }

    [Column("date_to")]
    public DateOnly DateTo { get; set; }

    [Column("guests_count")]
    public short GuestsCount { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [ForeignKey("BookingId")]
    [InverseProperty("BookingRooms")]
    public virtual Booking Booking { get; set; } = null!;

    [ForeignKey("RoomId")]
    [InverseProperty("BookingRooms")]
    public virtual Room Room { get; set; } = null!;

    [InverseProperty("BookingRoom")]
    public virtual ICollection<Stay> Stays { get; set; } = new List<Stay>();
}
