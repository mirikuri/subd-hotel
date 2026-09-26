using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("employees")]
public partial class Employee
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("hotel_id")]
    public int HotelId { get; set; }

    [Column("position_id")]
    public int PositionId { get; set; }

    [Column("full_name")]
    [StringLength(150)]
    public string FullName { get; set; } = null!;

    [Column("phone")]
    [StringLength(20)]
    public string? Phone { get; set; }

    [Column("hire_date")]
    public DateOnly HireDate { get; set; }

    [Column("dismissal_date")]
    public DateOnly? DismissalDate { get; set; }

    [ForeignKey("HotelId")]
    [InverseProperty("Employees")]
    public virtual Hotel Hotel { get; set; } = null!;

    [InverseProperty("Employee")]
    public virtual ICollection<Payroll> Payrolls { get; set; } = new List<Payroll>();

    [ForeignKey("PositionId")]
    [InverseProperty("Employees")]
    public virtual Position Position { get; set; } = null!;

    [InverseProperty("Employee")]
    public virtual ICollection<ServiceOrder> ServiceOrders { get; set; } = new List<ServiceOrder>();

    [InverseProperty("Employee")]
    public virtual ICollection<Shift> Shifts { get; set; } = new List<Shift>();

    [InverseProperty("Employee")]
    public virtual ICollection<Stay> Stays { get; set; } = new List<Stay>();

    [InverseProperty("Employee")]
    public virtual User? User { get; set; }
}
