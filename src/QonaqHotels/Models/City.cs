using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("cities")]
[Index("CountryId", "Name", Name = "cities_country_id_name_key", IsUnique = true)]
public partial class City
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("country_id")]
    public int CountryId { get; set; }

    [Column("name")]
    [StringLength(100)]
    public string Name { get; set; } = null!;

    [ForeignKey("CountryId")]
    [InverseProperty("Cities")]
    public virtual Country Country { get; set; } = null!;

    [InverseProperty("City")]
    public virtual ICollection<Hotel> Hotels { get; set; } = new List<Hotel>();
}
