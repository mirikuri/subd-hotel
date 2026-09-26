using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("utility_meters")]
[Index("SerialNumber", Name = "utility_meters_serial_number_key", IsUnique = true)]
public partial class UtilityMeter
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("unit_id")]
    public int UnitId { get; set; }

    [Column("meter_type")]
    [StringLength(20)]
    public string MeterType { get; set; } = null!;

    [Column("serial_number")]
    [StringLength(40)]
    public string SerialNumber { get; set; } = null!;

    [InverseProperty("Meter")]
    public virtual ICollection<MeterReading> MeterReadings { get; set; } = new List<MeterReading>();

    [ForeignKey("UnitId")]
    [InverseProperty("UtilityMeters")]
    public virtual CommercialUnit Unit { get; set; } = null!;
}
