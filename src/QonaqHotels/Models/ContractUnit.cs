using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[PrimaryKey("ContractId", "UnitId")]
[Table("contract_units")]
public partial class ContractUnit
{
    [Key]
    [Column("contract_id")]
    public int ContractId { get; set; }

    [Key]
    [Column("unit_id")]
    public int UnitId { get; set; }

    [Column("date_from")]
    public DateOnly DateFrom { get; set; }

    [Column("date_to")]
    public DateOnly DateTo { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [ForeignKey("ContractId")]
    [InverseProperty("ContractUnits")]
    public virtual LeaseContract Contract { get; set; } = null!;

    [ForeignKey("UnitId")]
    [InverseProperty("ContractUnits")]
    public virtual CommercialUnit Unit { get; set; } = null!;
}
