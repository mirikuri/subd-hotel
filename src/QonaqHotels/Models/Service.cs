using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("services")]
public partial class Service
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("category_id")]
    public int CategoryId { get; set; }

    [Column("hotel_id")]
    public int HotelId { get; set; }

    [Column("name")]
    [StringLength(120)]
    public string Name { get; set; } = null!;

    [Column("price")]
    [Precision(10, 2)]
    public decimal Price { get; set; }

    [Column("unit")]
    [StringLength(20)]
    public string? Unit { get; set; }

    [ForeignKey("CategoryId")]
    [InverseProperty("Services")]
    public virtual ServiceCategory Category { get; set; } = null!;

    [ForeignKey("HotelId")]
    [InverseProperty("Services")]
    public virtual Hotel Hotel { get; set; } = null!;

    [InverseProperty("Service")]
    public virtual ICollection<ServiceOrder> ServiceOrders { get; set; } = new List<ServiceOrder>();
}
