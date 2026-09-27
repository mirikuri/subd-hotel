using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("guests")]
[Index("DocumentNumber", Name = "guests_document_number_key", IsUnique = true)]
[Index("Login", Name = "guests_login_key", IsUnique = true)]
public partial class Guest
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("full_name")]
    [StringLength(150)]
    public string FullName { get; set; } = null!;

    [Column("document_number")]
    [StringLength(40)]
    public string DocumentNumber { get; set; } = null!;

    [Column("birth_date")]
    public DateOnly? BirthDate { get; set; }

    [Column("phone")]
    [StringLength(20)]
    public string? Phone { get; set; }

    [Column("email")]
    [StringLength(120)]
    public string? Email { get; set; }

    [Column("country_id")]
    public int? CountryId { get; set; }

    [Column("login")]
    [StringLength(50)]
    public string? Login { get; set; }

    [Column("password_hash")]
    [StringLength(500)]
    public string? PasswordHash { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("failed_attempts")]
    public int FailedAttempts { get; set; }

    [Column("locked_until")]
    public DateTime? LockedUntil { get; set; }

    [Column("last_login_at")]
    public DateTime? LastLoginAt { get; set; }

    [Column("registered_at")]
    public DateTime RegisteredAt { get; set; }

    [InverseProperty("Guest")]
    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    [ForeignKey("CountryId")]
    [InverseProperty("Guests")]
    public virtual Country? Country { get; set; }

    [InverseProperty("Guest")]
    public virtual LoyaltyCard? LoyaltyCard { get; set; }
}
