using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("countries")]
[Index("Name", Name = "countries_name_key", IsUnique = true)]
public partial class Country
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    [StringLength(80)]
    public string Name { get; set; } = null!;

    [InverseProperty("Country")]
    public virtual ICollection<City> Cities { get; set; } = new List<City>();

    [InverseProperty("Country")]
    public virtual ICollection<Guest> Guests { get; set; } = new List<Guest>();
}
