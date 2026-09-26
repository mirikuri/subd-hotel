using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("users")]
[Index("EmployeeId", Name = "users_employee_id_key", IsUnique = true)]
[Index("Login", Name = "users_login_key", IsUnique = true)]
public partial class User
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("employee_id")]
    public int EmployeeId { get; set; }

    [Column("login")]
    [StringLength(50)]
    public string Login { get; set; } = null!;

    [Column("password_hash")]
    [StringLength(255)]
    public string PasswordHash { get; set; } = null!;

    [Column("role")]
    [StringLength(30)]
    public string Role { get; set; } = null!;

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("failed_attempts")]
    public int FailedAttempts { get; set; }

    [Column("locked_until")]
    public DateTime? LockedUntil { get; set; }

    [Column("last_login_at")]
    public DateTime? LastLoginAt { get; set; }

    [InverseProperty("User")]
    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    [ForeignKey("EmployeeId")]
    [InverseProperty("User")]
    public virtual Employee Employee { get; set; } = null!;
}
