using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("rooms")]
[Index("HotelId", "RoomNumber", Name = "rooms_hotel_id_room_number_key", IsUnique = true)]
public partial class Room
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("hotel_id")]
    public int HotelId { get; set; }

    [Column("room_type_id")]
    public int RoomTypeId { get; set; }

    [Column("room_number")]
    [StringLength(10)]
    public string RoomNumber { get; set; } = null!;

    [Column("floor")]
    public short? Floor { get; set; }

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = null!;

    [InverseProperty("Room")]
    public virtual ICollection<BookingRoom> BookingRooms { get; set; } = new List<BookingRoom>();

    [ForeignKey("HotelId")]
    [InverseProperty("Rooms")]
    public virtual Hotel Hotel { get; set; } = null!;

    [ForeignKey("RoomTypeId")]
    [InverseProperty("Rooms")]
    public virtual RoomType RoomType { get; set; } = null!;
}
