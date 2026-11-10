using System.Globalization;

namespace QonaqHotels.Models;

// Все цены в БД — в долларах США (rates, services, service_orders).
// Выводить суммы только через Money.Usd, чтобы формат был один на весь сайт.
public static class Money
{
    public static string Usd(decimal amount) =>
        amount == decimal.Truncate(amount)
            ? amount.ToString("$#,0", CultureInfo.InvariantCulture)
            : amount.ToString("$#,0.00", CultureInfo.InvariantCulture);
}
