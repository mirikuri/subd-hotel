using System.Net;

namespace QonaqHotels.Tests;

public class AccessTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public AccessTests(AppFactory app) => _app = app;

    [Theory]
    [InlineData(null,        "/Admin",           HttpStatusCode.Unauthorized)]
    [InlineData("гость",     "/Admin",           HttpStatusCode.Forbidden)]
    [InlineData("гость",     "/Admin/Bookings",  HttpStatusCode.Forbidden)]
    [InlineData("бухгалтер", "/Admin",           HttpStatusCode.OK)]
    [InlineData("бухгалтер", "/Admin/Chess",     HttpStatusCode.Forbidden)]
    [InlineData("бухгалтер", "/Admin/Bookings",  HttpStatusCode.Forbidden)]
    [InlineData("ресепшен",  "/Admin/Chess",     HttpStatusCode.OK)]
    [InlineData("ресепшен",  "/Admin/Employees", HttpStatusCode.Forbidden)]
    [InlineData("админ",     "/Admin/Employees", HttpStatusCode.OK)]
    public async Task Admin_AccessByRole(string? role, string url, HttpStatusCode expected)
    {
        var res = await _app.Client(role).GetAsync(url);
        Assert.Equal(expected, res.StatusCode);
    }

    [Fact]
    public async Task Sidebar_Accountant_HidesReceptionAndAdminLinks()
    {
        var html = await _app.Client("бухгалтер").GetStringAsync("/Admin");
        Assert.DoesNotContain("href=\"/Admin/Chess\"", html);
        Assert.DoesNotContain("href=\"/Admin/Employees\"", html);
        Assert.Contains("href=\"/Admin/Catalogs\"", html);
    }

    [Theory]
    [InlineData("бухгалтер")]
    [InlineData("ресепшен")]
    [InlineData("админ")]
    public async Task Sidebar_NoRoomSearchItem(string role)
    {
        var html = await _app.Client(role).GetStringAsync("/Admin");
        Assert.DoesNotContain("Поиск номера", html);
    }
}
