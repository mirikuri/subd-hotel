using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("utility_tariffs")]
public partial class UtilityTariff
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("meter_type")]
    [StringLength(20)]
    public string MeterType { get; set; } = null!;

    [Column("price_per_unit")]
    [Precision(10, 4)]
    public decimal PricePerUnit { get; set; }

    [Column("valid_from")]
    public DateOnly ValidFrom { get; set; }
}
