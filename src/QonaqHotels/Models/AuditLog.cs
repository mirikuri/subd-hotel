using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("audit_log")]
public partial class AuditLog
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }

    [Column("action")]
    [StringLength(20)]
    public string Action { get; set; } = null!;

    [Column("table_name")]
    [StringLength(60)]
    public string TableName { get; set; } = null!;

    [Column("record_id")]
    public int? RecordId { get; set; }

    [Column("changed_at", TypeName = "timestamp without time zone")]
    public DateTime ChangedAt { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("AuditLogs")]
    public virtual User? User { get; set; }
}
