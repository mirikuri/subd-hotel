using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("shifts")]
public partial class Shift
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("employee_id")]
    public int EmployeeId { get; set; }

    [Column("shift_start", TypeName = "timestamp without time zone")]
    public DateTime ShiftStart { get; set; }

    [Column("shift_end", TypeName = "timestamp without time zone")]
    public DateTime ShiftEnd { get; set; }

    [Column("hours_worked")]
    [Precision(5, 2)]
    public decimal? HoursWorked { get; set; }

    [ForeignKey("EmployeeId")]
    [InverseProperty("Shifts")]
    public virtual Employee Employee { get; set; } = null!;
}
