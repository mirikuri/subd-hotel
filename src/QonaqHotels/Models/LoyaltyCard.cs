namespace QonaqHotels.Models;

// Строка таблицы loyalty_cards (db/schema.sql)
public class LoyaltyCard
{
    public int Id { get; set; }
    public int GuestId { get; set; }
    public string CardNumber { get; set; } = "";
    public string Level { get; set; } = "";
    public int BonusPoints { get; set; }
    public DateOnly IssueDate { get; set; }
}
