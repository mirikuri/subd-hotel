using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("hotels")]
public partial class Hotel
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    [StringLength(150)]
    public string Name { get; set; } = null!;

    [Column("city_id")]
    public int CityId { get; set; }

    [Column("address")]
    [StringLength(250)]
    public string Address { get; set; } = null!;

    [Column("stars")]
    public short Stars { get; set; }

    [Column("phone")]
    [StringLength(20)]
    public string? Phone { get; set; }

    [InverseProperty("Hotel")]
    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    [ForeignKey("CityId")]
    [InverseProperty("Hotels")]
    public virtual City City { get; set; } = null!;

    [InverseProperty("Hotel")]
    public virtual ICollection<CommercialUnit> CommercialUnits { get; set; } = new List<CommercialUnit>();

    [InverseProperty("Hotel")]
    public virtual ICollection<Employee> Employees { get; set; } = new List<Employee>();

    [InverseProperty("Hotel")]
    public virtual ICollection<Room> Rooms { get; set; } = new List<Room>();

    [InverseProperty("Hotel")]
    public virtual ICollection<Service> Services { get; set; } = new List<Service>();
}
