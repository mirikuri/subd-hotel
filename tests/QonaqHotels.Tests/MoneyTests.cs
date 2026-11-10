using QonaqHotels.Models;

namespace QonaqHotels.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData(1250, "$1,250")]
    [InlineData(12.5, "$12.50")]
    [InlineData(0, "$0")]
    [InlineData(1000000, "$1,000,000")]
    public void Usd_Formats(double amount, string expected) =>
        Assert.Equal(expected, Money.Usd((decimal)amount));
}
