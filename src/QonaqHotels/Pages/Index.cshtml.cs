using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages;

public class IndexModel : PageModel
{
    private readonly Db _db;
    private readonly IWebHostEnvironment _env;

    public IndexModel(Db db, IWebHostEnvironment env)
    {
        _db  = db;
        _env = env;
    }

    public List<Hotel>    Hotels    { get; private set; } = [];
    public List<RoomType> RoomTypes { get; private set; } = [];
    public List<City>     Cities    { get; private set; } = [];
    public int HotelCount  { get; private set; }
    public int RoomCount   { get; private set; }
    public int CityCount   { get; private set; }

    public async Task OnGetAsync()
    {
        using var conn = _db.Open();

        // Загружаем отели вместе с названием города
        using (var cmd = new NpgsqlCommand(
            "SELECT h.id, h.name, h.city_id, h.address, h.stars, h.phone, c.name " +
            "FROM hotels h JOIN cities c ON c.id = h.city_id " +
            "ORDER BY c.name", conn))
        using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                Hotels.Add(new Hotel
                {
                    Id       = r.GetInt32(0),
                    Name     = r.GetString(1),
                    CityId   = r.GetInt32(2),
                    Address  = r.GetString(3),
                    Stars    = r.GetInt16(4),
                    Phone    = r.IsDBNull(5) ? null : r.GetString(5),
                    CityName = r.GetString(6)
                });
            }
        }

        // Загружаем типы номеров (первые 6)
        using (var cmd = new NpgsqlCommand(
            "SELECT id, name, capacity, description FROM room_types ORDER BY id LIMIT 6", conn))
        using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                RoomTypes.Add(new RoomType
                {
                    Id          = r.GetInt32(0),
                    Name        = r.GetString(1),
                    Capacity    = r.GetInt16(2),
                    Description = r.IsDBNull(3) ? null : r.GetString(3)
                });
            }
        }

        // Загружаем города
        using (var cmd = new NpgsqlCommand(
            "SELECT id, country_id, name FROM cities ORDER BY name", conn))
        using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                Cities.Add(new City
                {
                    Id        = r.GetInt32(0),
                    CountryId = r.GetInt32(1),
                    Name      = r.GetString(2)
                });
            }
        }

        // Счётчики для статистики на главной
        using (var cmd = new NpgsqlCommand(
            "SELECT " +
            "  (SELECT COUNT(*) FROM hotels) AS hotel_count, " +
            "  (SELECT COUNT(*) FROM rooms)  AS room_count, " +
            "  (SELECT COUNT(*) FROM cities) AS city_count", conn))
        using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                HotelCount = (int)r.GetInt64(0);
                RoomCount  = (int)r.GetInt64(1);
                CityCount  = (int)r.GetInt64(2);
            }
        }
    }

    public string RoomImage(int id)
    {
        var path = Path.Combine(_env.WebRootPath ?? "", "img", $"room-{id}.webp");
        return System.IO.File.Exists(path) ? $"/img/room-{id}.webp" : "/img/placeholder.webp";
    }

    public string HotelImage(int id)
    {
        var path = Path.Combine(_env.WebRootPath ?? "", "img", $"hotel-{id}.webp");
        return System.IO.File.Exists(path) ? $"/img/hotel-{id}.webp" : "/img/placeholder.webp";
    }
}
