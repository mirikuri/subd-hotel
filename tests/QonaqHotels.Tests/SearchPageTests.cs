using QonaqHotels.Data;
using QonaqHotels.Pages;

namespace QonaqHotels.Tests;

public class SearchPageTests : IClassFixture<AppFactory>
{
    private readonly Db _db = TestDb.Create();
    private readonly AppFactory _app;
    private static readonly DateOnly D = Clock.Today.AddDays(12);

    public SearchPageTests(AppFactory app) => _app = app;

    [Fact]
    public async Task SearchPage_ReturnsOffersForTestCity()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var model = new SearchModel(_db) { City = t.CityId, From = D, To = D.AddDays(2), Guests = 2 };
        await model.OnGetAsync();
        Assert.Null(model.Error);
        Assert.Single(model.Offers);
        Assert.NotEmpty(model.Cities);
    }

    [Fact]
    public async Task SearchPage_InvalidDates_ShowsError()
    {
        var model = new SearchModel(_db) { From = D, To = D, Guests = 2 };
        await model.OnGetAsync();
        Assert.Equal("Дата выезда должна быть позже даты заезда.", model.Error);
        Assert.Empty(model.Offers);
    }

    [Fact]
    public async Task SearchPage_NoParams_NoError()
    {
        var model = new SearchModel(_db);
        await model.OnGetAsync();
        Assert.Null(model.Error);
        Assert.Empty(model.Offers);
        Assert.False(model.Searched);
    }

    [Fact]
    public async Task SearchPage_Http_ShowsDollars()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var url = $"/Search?city={t.CityId}&from={D:yyyy-MM-dd}&to={D.AddDays(2):yyyy-MM-dd}&guests=2";
        var res = await _app.Client(null).GetAsync(url);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("$", html);
        Assert.Contains("/Booking/Request?hotelId=" + t.HotelId, html);
        Assert.DoesNotContain("₸", html);
    }
}
