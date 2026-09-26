using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages;

public class IndexModel : PageModel
{
    private readonly HotelDbContext _db;
    private readonly IWebHostEnvironment _env;

    public IndexModel(HotelDbContext db, IWebHostEnvironment env)
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
        Hotels = await _db.Hotels
            .AsNoTracking()
            .Include(h => h.City)
            .OrderBy(h => h.City.Name)
            .ToListAsync();

        RoomTypes = await _db.RoomTypes
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .Take(6)
            .ToListAsync();

        Cities = await _db.Cities
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync();

        HotelCount = await _db.Hotels.AsNoTracking().CountAsync();
        RoomCount  = await _db.Rooms.AsNoTracking().CountAsync();
        CityCount  = await _db.Cities.AsNoTracking().CountAsync();
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
