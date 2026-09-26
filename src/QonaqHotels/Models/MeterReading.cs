using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("meter_readings")]
[Index("MeterId", "ReadingDate", Name = "meter_readings_meter_id_reading_date_key", IsUnique = true)]
public partial class MeterReading
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("meter_id")]
    public int MeterId { get; set; }

    [Column("reading_date")]
    public DateOnly ReadingDate { get; set; }

    [Column("value")]
    [Precision(12, 3)]
    public decimal Value { get; set; }

    [ForeignKey("MeterId")]
    [InverseProperty("MeterReadings")]
    public virtual UtilityMeter Meter { get; set; } = null!;
}
