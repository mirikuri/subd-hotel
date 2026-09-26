using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("positions")]
public partial class Position
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    [StringLength(80)]
    public string Name { get; set; } = null!;

    [Column("base_salary")]
    [Precision(10, 2)]
    public decimal? BaseSalary { get; set; }

    [Column("hourly_rate")]
    [Precision(8, 2)]
    public decimal? HourlyRate { get; set; }

    [InverseProperty("Position")]
    public virtual ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
