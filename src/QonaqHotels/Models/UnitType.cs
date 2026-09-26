using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("unit_types")]
public partial class UnitType
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    [StringLength(60)]
    public string Name { get; set; } = null!;

    [InverseProperty("UnitType")]
    public virtual ICollection<CommercialUnit> CommercialUnits { get; set; } = new List<CommercialUnit>();
}
