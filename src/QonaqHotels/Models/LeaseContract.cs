using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("lease_contracts")]
[Index("ContractNumber", Name = "lease_contracts_contract_number_key", IsUnique = true)]
public partial class LeaseContract
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("tenant_id")]
    public int TenantId { get; set; }

    [Column("contract_number")]
    [StringLength(30)]
    public string ContractNumber { get; set; } = null!;

    [Column("date_start")]
    public DateOnly DateStart { get; set; }

    [Column("date_end")]
    public DateOnly DateEnd { get; set; }

    [Column("rate_per_sqm")]
    [Precision(10, 2)]
    public decimal RatePerSqm { get; set; }

    [Column("deposit")]
    [Precision(12, 2)]
    public decimal? Deposit { get; set; }

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = null!;

    [InverseProperty("Contract")]
    public virtual ICollection<ContractUnit> ContractUnits { get; set; } = new List<ContractUnit>();

    [InverseProperty("Contract")]
    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    [ForeignKey("TenantId")]
    [InverseProperty("LeaseContracts")]
    public virtual Tenant Tenant { get; set; } = null!;
}
