using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("seasons")]
public partial class Season
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    [StringLength(60)]
    public string Name { get; set; } = null!;

    [Column("date_start")]
    public DateOnly DateStart { get; set; }

    [Column("date_end")]
    public DateOnly DateEnd { get; set; }

    [InverseProperty("Season")]
    public virtual ICollection<Rate> Rates { get; set; } = new List<Rate>();
}
