using System.Net;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Tests;

public class CatalogHotelsTests : IClassFixture<AppFactory>
{
    private readonly Db _db = TestDb.Create();
    private readonly AppFactory _app;
    public CatalogHotelsTests(AppFactory app) => _app = app;

    private static Dictionary<string, string> Form(string name, int cityId) => new()
    {
        ["Name"] = name, ["CityId"] = cityId.ToString(), ["Address"] = "ул. Тестовая, 1", ["Stars"] = "4",
        ["Phone"] = "+77001112233", ["Email"] = "t@test.kz",
        ["CheckInTime"] = "15:00", ["CheckOutTime"] = "11:00", ["Description"] = "тест"
    };

    [Theory]
    [InlineData("ресепшен",  "/Admin/Catalogs",              HttpStatusCode.OK)]
    [InlineData("бухгалтер", "/Admin/Catalogs/Hotels",       HttpStatusCode.OK)]
    [InlineData("ресепшен",  "/Admin/Catalogs/Hotels/Edit",  HttpStatusCode.Forbidden)]
    [InlineData("бухгалтер", "/Admin/Catalogs/Hotels/Edit",  HttpStatusCode.Forbidden)]
    [InlineData("админ",     "/Admin/Catalogs/Hotels/Edit",  HttpStatusCode.OK)]
    public async Task Access(string role, string url, HttpStatusCode expected) =>
        Assert.Equal(expected, (await _app.Client(role).GetAsync(url)).StatusCode);

    [Fact]
    public async Task Admin_CreatesHotel_ThenDuplicateNameRejected()
    {
        await using var t = await TestHotel.CreateAsync(_db);   // даёт свой город
        var name = "Новый " + t.Tag;
        var admin = _app.Client(Roles.Admin);
        try
        {
            var res = await admin.PostFormAsync("/Admin/Catalogs/Hotels/Edit", Form(name, t.CityId));
            Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
            Assert.Equal("15:00:00", (await Sql.ScalarAsync(
                "SELECT check_in_time::text FROM hotels WHERE name = @n", ("n", name)))?.ToString());

            var dup = await admin.PostFormAsync("/Admin/Catalogs/Hotels/Edit", Form(name, t.CityId));
            Assert.Equal(HttpStatusCode.OK, dup.StatusCode);
            Assert.Contains("Отель с таким названием уже есть.", await dup.Content.ReadAsStringAsync());
        }
        finally
        {
            await Sql.ExecAsync("DELETE FROM hotels WHERE name = @n", ("n", name));
        }
    }

    [Fact]
    public async Task Admin_EditsHotel()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var form = Form("Отель " + t.Tag, t.CityId);
        form["Stars"] = "5";
        var res = await _app.Client(Roles.Admin).PostFormAsync($"/Admin/Catalogs/Hotels/Edit/{t.HotelId}", form);
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal((short)5, await Sql.ScalarAsync("SELECT stars FROM hotels WHERE id = @id", ("id", t.HotelId)));
    }

    [Fact]
    public async Task Delete_HotelWithRooms_Refused()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var res = await _app.Client(Roles.Admin).PostFormAsync($"/Admin/Catalogs/Hotels/Delete/{t.HotelId}", []);
        Assert.Contains("Нельзя удалить: на запись ссылаются номера.", await res.Content.ReadAsStringAsync());
        Assert.Equal(1L, await Sql.ScalarAsync("SELECT count(*) FROM hotels WHERE id = @id", ("id", t.HotelId)));
    }

    [Fact]
    public async Task Reception_PostEdit_Forbidden()
    {
        var res = await _app.Client(Roles.Reception).PostRawAsync("/Admin/Catalogs/Hotels/Edit");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
