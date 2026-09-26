using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("invoices")]
[Index("InvoiceNumber", Name = "invoices_invoice_number_key", IsUnique = true)]
public partial class Invoice
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("invoice_number")]
    [StringLength(30)]
    public string InvoiceNumber { get; set; } = null!;

    [Column("invoice_type")]
    [StringLength(20)]
    public string InvoiceType { get; set; } = null!;

    [Column("booking_id")]
    public int? BookingId { get; set; }

    [Column("contract_id")]
    public int? ContractId { get; set; }

    [Column("issue_date")]
    public DateOnly IssueDate { get; set; }

    [Column("due_date")]
    public DateOnly DueDate { get; set; }

    [Column("total_amount")]
    [Precision(12, 2)]
    public decimal TotalAmount { get; set; }

    [Column("status")]
    [StringLength(20)]
    public string Status { get; set; } = null!;

    [ForeignKey("BookingId")]
    [InverseProperty("Invoices")]
    public virtual Booking? Booking { get; set; }

    [ForeignKey("ContractId")]
    [InverseProperty("Invoices")]
    public virtual LeaseContract? Contract { get; set; }

    [InverseProperty("Invoice")]
    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();

    [InverseProperty("Invoice")]
    public virtual ICollection<Penalty> Penalties { get; set; } = new List<Penalty>();
}
