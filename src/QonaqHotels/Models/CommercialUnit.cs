using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("commercial_units")]
[Index("HotelId", "UnitNumber", Name = "commercial_units_hotel_id_unit_number_key", IsUnique = true)]
public partial class CommercialUnit
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("hotel_id")]
    public int HotelId { get; set; }

    [Column("unit_type_id")]
    public int UnitTypeId { get; set; }

    [Column("unit_number")]
    [StringLength(10)]
    public string UnitNumber { get; set; } = null!;

    [Column("floor")]
    public short? Floor { get; set; }

    [Column("area_sqm")]
    [Precision(8, 2)]
    public decimal AreaSqm { get; set; }

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = null!;

    [InverseProperty("Unit")]
    public virtual ICollection<ContractUnit> ContractUnits { get; set; } = new List<ContractUnit>();

    [ForeignKey("HotelId")]
    [InverseProperty("CommercialUnits")]
    public virtual Hotel Hotel { get; set; } = null!;

    [ForeignKey("UnitTypeId")]
    [InverseProperty("CommercialUnits")]
    public virtual UnitType UnitType { get; set; } = null!;

    [InverseProperty("Unit")]
    public virtual ICollection<UtilityMeter> UtilityMeters { get; set; } = new List<UtilityMeter>();
}
