using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("tenants")]
[Index("Bin", Name = "tenants_bin_key", IsUnique = true)]
public partial class Tenant
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("company_name")]
    [StringLength(150)]
    public string CompanyName { get; set; } = null!;

    [Column("bin")]
    [StringLength(12)]
    public string Bin { get; set; } = null!;

    [Column("legal_address")]
    [StringLength(250)]
    public string? LegalAddress { get; set; }

    [Column("director_name")]
    [StringLength(150)]
    public string? DirectorName { get; set; }

    [Column("phone")]
    [StringLength(20)]
    public string? Phone { get; set; }

    [Column("email")]
    [StringLength(120)]
    public string? Email { get; set; }

    [InverseProperty("Tenant")]
    public virtual ICollection<LeaseContract> LeaseContracts { get; set; } = new List<LeaseContract>();
}
