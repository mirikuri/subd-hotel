using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("stays")]
public partial class Stay
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("booking_room_id")]
    public int BookingRoomId { get; set; }

    [Column("actual_check_in", TypeName = "timestamp without time zone")]
    public DateTime ActualCheckIn { get; set; }

    [Column("actual_check_out", TypeName = "timestamp without time zone")]
    public DateTime? ActualCheckOut { get; set; }

    [Column("employee_id")]
    public int EmployeeId { get; set; }

    [ForeignKey("BookingRoomId")]
    [InverseProperty("Stays")]
    public virtual BookingRoom BookingRoom { get; set; } = null!;

    [ForeignKey("EmployeeId")]
    [InverseProperty("Stays")]
    public virtual Employee Employee { get; set; } = null!;
}
