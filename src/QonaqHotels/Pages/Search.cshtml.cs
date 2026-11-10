using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages;

// Поиск свободных номеров на сайте — открыт всем, заявку отправляет только вошедший гость
public class SearchModel : PageModel
{
    private readonly Db _db;
    public SearchModel(Db db) => _db = db;

    [BindProperty(SupportsGet = true)] public int? City { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public int Guests { get; set; } = 2;

    public List<City> Cities { get; private set; } = [];
    public List<RoomOffer> Offers { get; private set; } = [];
    public string? Error { get; private set; }
    public bool Searched { get; private set; }

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        Cities = await LoadCitiesAsync(conn);

        if (From is null || To is null) return;   // открыли без параметров — только форма

        Error = RoomSearch.Validate(From.Value, To.Value, Guests, Clock.Today);
        if (Error != null) return;

        Searched = true;
        Offers = await RoomSearch.SearchAsync(conn, City, From.Value, To.Value, Guests);
    }

    public string RequestUrl(RoomOffer o) =>
        $"/Booking/Request?hotelId={o.HotelId}&roomTypeId={o.RoomTypeId}" +
        $"&from={From:yyyy-MM-dd}&to={To:yyyy-MM-dd}&guests={Guests}";

    public static string NightsWord(int n) =>
        (n % 10 == 1 && n % 100 != 11) ? "ночь"
        : (n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14) ? "ночи"
        : "ночей";

    internal static async Task<List<City>> LoadCitiesAsync(NpgsqlConnection conn)
    {
        // Только города, где есть отели сети
        await using var cmd = new NpgsqlCommand("""
            SELECT c.id, c.country_id, c.name
            FROM cities c
            WHERE EXISTS (SELECT 1 FROM hotels h WHERE h.city_id = c.id)
            ORDER BY c.name
            """, conn);
        var list = new List<City>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new City { Id = r.GetInt32(0), CountryId = r.GetInt32(1), Name = r.GetString(2) });
        return list;
    }
}
