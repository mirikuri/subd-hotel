using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QonaqHotels.Models;

[Table("loyalty_cards")]
[Index("CardNumber", Name = "loyalty_cards_card_number_key", IsUnique = true)]
[Index("GuestId", Name = "loyalty_cards_guest_id_key", IsUnique = true)]
public partial class LoyaltyCard
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("guest_id")]
    public int GuestId { get; set; }

    [Column("card_number")]
    [StringLength(20)]
    public string CardNumber { get; set; } = null!;

    [Column("level")]
    [StringLength(20)]
    public string Level { get; set; } = null!;

    [Column("bonus_points")]
    public int BonusPoints { get; set; }

    [Column("issue_date")]
    public DateOnly IssueDate { get; set; }

    [ForeignKey("GuestId")]
    [InverseProperty("LoyaltyCard")]
    public virtual Guest Guest { get; set; } = null!;
}
