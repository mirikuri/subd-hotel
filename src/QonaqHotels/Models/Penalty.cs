using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("penalties")]
public partial class Penalty
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("invoice_id")]
    public int InvoiceId { get; set; }

    [Column("calc_date")]
    public DateOnly CalcDate { get; set; }

    [Column("days_overdue")]
    public int DaysOverdue { get; set; }

    [Column("amount")]
    [Precision(12, 2)]
    public decimal Amount { get; set; }

    [ForeignKey("InvoiceId")]
    [InverseProperty("Penalties")]
    public virtual Invoice Invoice { get; set; } = null!;
}
