using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Moq;
using QonaqHotels.Data;
using QonaqHotels.Models;
using QonaqHotels.Pages;

namespace QonaqHotels.Tests;

public class IndexPageTests
{
    private static HotelDbContext CreateContext(string dbName)
    {
        var opts = new DbContextOptionsBuilder<HotelDbContext>()
            .UseInMemoryDatabase(dbName).Options;
        return new HotelDbContext(opts);
    }

    private static Mock<IWebHostEnvironment> MockEnv(string webRoot = "/fake")
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.WebRootPath).Returns(webRoot);
        return env;
    }

    [Fact]
    public async Task OnGetAsync_EmptyTables_DoesNotThrow()
    {
        using var db  = CreateContext("empty-index");
        var model = new IndexModel(db, MockEnv().Object);
        await model.OnGetAsync();
        Assert.Empty(model.Hotels);
        Assert.Empty(model.RoomTypes);
        Assert.Empty(model.Cities);
    }

    [Fact]
    public async Task OnGetAsync_WithData_PopulatesCollections()
    {
        using var db = CreateContext("with-data-index");
        var country = new Country { Name = "Казахстан" };
        db.Countries.Add(country);
        var city = new City { Name = "Астана", Country = country };
        db.Cities.Add(city);
        db.Hotels.Add(new Hotel { Name = "H1", City = city, Address = "a", Stars = 4 });
        db.RoomTypes.Add(new RoomType { Name = "Стандарт", Capacity = 2 });
        await db.SaveChangesAsync();

        var model = new IndexModel(db, MockEnv().Object);
        await model.OnGetAsync();
        Assert.Single(model.Hotels);
        Assert.Single(model.RoomTypes);
        Assert.Single(model.Cities);
    }

    [Fact]
    public void RoomImage_FileMissing_ReturnsPlaceholder()
    {
        using var db    = CreateContext("img-test");
        var model = new IndexModel(db, MockEnv("/nonexistent-dir").Object);
        var img = model.RoomImage(99);
        Assert.Equal("/img/placeholder.webp", img);
    }

    [Fact]
    public async Task OnGetAsync_MoreThan6RoomTypes_Returns6()
    {
        using var db = CreateContext("six-types");
        for (int i = 1; i <= 8; i++)
            db.RoomTypes.Add(new RoomType { Name = $"Type{i}", Capacity = 2 });
        await db.SaveChangesAsync();

        var country = new Country { Name = "Казахстан" };
        db.Countries.Add(country);
        await db.SaveChangesAsync();

        var model = new IndexModel(db, MockEnv().Object);
        await model.OnGetAsync();
        Assert.Equal(6, model.RoomTypes.Count);
    }
}
