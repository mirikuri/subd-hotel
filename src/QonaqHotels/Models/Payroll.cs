using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("payrolls")]
[Index("EmployeeId", "PeriodMonth", Name = "payrolls_employee_id_period_month_key", IsUnique = true)]
public partial class Payroll
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("employee_id")]
    public int EmployeeId { get; set; }

    [Column("period_month")]
    public DateOnly PeriodMonth { get; set; }

    [Column("hours_total")]
    [Precision(6, 2)]
    public decimal HoursTotal { get; set; }

    [Column("base_amount")]
    [Precision(10, 2)]
    public decimal BaseAmount { get; set; }

    [Column("bonus")]
    [Precision(10, 2)]
    public decimal Bonus { get; set; }

    [Column("deduction")]
    [Precision(10, 2)]
    public decimal Deduction { get; set; }

    [Column("total_amount")]
    [Precision(10, 2)]
    public decimal TotalAmount { get; set; }

    [Column("payment_date")]
    public DateOnly? PaymentDate { get; set; }

    [ForeignKey("EmployeeId")]
    [InverseProperty("Payrolls")]
    public virtual Employee Employee { get; set; } = null!;
}
