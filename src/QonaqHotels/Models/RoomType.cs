using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("room_types")]
public partial class RoomType
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    [StringLength(80)]
    public string Name { get; set; } = null!;

    [Column("capacity")]
    public short Capacity { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [InverseProperty("RoomType")]
    public virtual ICollection<Rate> Rates { get; set; } = new List<Rate>();

    [InverseProperty("RoomType")]
    public virtual ICollection<Room> Rooms { get; set; } = new List<Room>();
}
