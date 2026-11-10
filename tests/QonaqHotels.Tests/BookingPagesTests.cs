using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;
using QonaqHotels.Pages;

namespace QonaqHotels.Tests;

public class BookingPagesTests : IClassFixture<AppFactory>
{
    private readonly Db _db = TestDb.Create();
    private readonly AppFactory _app;
    private static readonly DateOnly D = Clock.Today.AddDays(14);

    public BookingPagesTests(AppFactory app) => _app = app;

    private static string RequestUrl(TestHotel t, int guests = 2, int? roomTypeId = null) =>
        $"/Booking/Request?hotelId={t.HotelId}&roomTypeId={roomTypeId ?? t.RoomTypeId}" +
        $"&from={D:yyyy-MM-dd}&to={D.AddDays(2):yyyy-MM-dd}&guests={guests}";

    private static string Token(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

    [Fact]
    public async Task Request_Anonymous_Challenged()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var res = await _app.Client(null).GetAsync(RequestUrl(t));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Request_Staff_Forbidden()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var res = await _app.Client(Roles.Reception).GetAsync(RequestUrl(t));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Request_Get_ShowsTotal()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        await using var conn = await _db.OpenAsync();
        var total = await RoomSearch.PeriodPriceAsync(conn, t.RoomTypeId, D, D.AddDays(2));

        var html = await _app.Client(Roles.Guest, t.GuestId).GetStringAsync(RequestUrl(t));
        Assert.Contains(Money.Usd(total!.Value), html);
    }

    [Theory]
    [InlineData(6, false)]     // «Люкс» вмещает двоих
    [InlineData(2, true)]      // несуществующий тип
    public async Task Request_Get_NoOffer_404(int guests, bool badType)
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var url = RequestUrl(t, guests, badType ? int.MaxValue : null);
        var res = await _app.Client(Roles.Guest, t.GuestId).GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Request_Post_RedirectsToMy()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var client = _app.Client(Roles.Guest, t.GuestId);
        var html = await client.GetStringAsync(RequestUrl(t));

        var res = await client.PostAsync(RequestUrl(t), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(html),
            ["Comment"] = "поздний заезд"
        }));

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/Booking/My", res.Headers.Location!.OriginalString);
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT guest_comment FROM bookings WHERE guest_id = @g AND source = 'сайт'", conn);
        cmd.Parameters.AddWithValue("g", t.GuestId);
        Assert.Equal("поздний заезд", await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task My_ShowsOnlyOwn()
    {
        await using var t = await TestHotel.CreateAsync(_db, rooms: 2);
        var other = await t.AddGuestAsync();
        var mine   = await t.BookAsync(t.RoomIds[0], D, D.AddDays(1));
        var theirs = await t.BookAsync(t.RoomIds[1], D, D.AddDays(1), guestId: other);

        var html = await _app.Client(Roles.Guest, t.GuestId).GetStringAsync("/Booking/My");
        Assert.Contains($"№{mine}", html);
        Assert.DoesNotContain($"№{theirs}", html);
    }

    [Fact]
    public async Task My_CancelForeign_NoChange()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var other  = await t.AddGuestAsync();
        var theirs = await t.BookAsync(t.RoomIds[0], D, D.AddDays(1), guestId: other);

        var client = _app.Client(Roles.Guest, t.GuestId);
        var html = await client.GetStringAsync("/Booking/My");
        await client.PostAsync("/Booking/My?handler=Cancel", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(html),
            ["id"] = theirs.ToString()
        }));

        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT status FROM bookings WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", theirs);
        Assert.Equal("новая", await cmd.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("/Booking/Request?x=1", "/Booking/Request?x=1")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData(null, "/")]
    public void RegisterLogin_SafeReturnUrl(string? url, string expected)
    {
        var helper = new UrlHelper(new ActionContext(
            new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        Assert.Equal(expected, RegisterLoginModel.SafeReturnUrl(url, helper));
    }
}
