using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("rates")]
[Index("RoomTypeId", "SeasonId", Name = "rates_room_type_id_season_id_key", IsUnique = true)]
public partial class Rate
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("room_type_id")]
    public int RoomTypeId { get; set; }

    [Column("season_id")]
    public int SeasonId { get; set; }

    [Column("price_per_night")]
    [Precision(10, 2)]
    public decimal PricePerNight { get; set; }

    [ForeignKey("RoomTypeId")]
    [InverseProperty("Rates")]
    public virtual RoomType RoomType { get; set; } = null!;

    [ForeignKey("SeasonId")]
    [InverseProperty("Rates")]
    public virtual Season Season { get; set; } = null!;
}
