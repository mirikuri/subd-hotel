using System.Net;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Tests;

// Справочники: типы номеров, номера, сезоны и тарифы, услуги — права ролей и запреты
public class CatalogsTests : IClassFixture<AppFactory>
{
    private readonly Db _db = TestDb.Create();
    private readonly AppFactory _app;
    public CatalogsTests(AppFactory app) => _app = app;

    [Theory]
    [InlineData("ресепшен",  "/Admin/Catalogs/RoomTypes",      HttpStatusCode.OK)]
    [InlineData("ресепшен",  "/Admin/Catalogs/RoomTypes/Edit", HttpStatusCode.Forbidden)]
    [InlineData("ресепшен",  "/Admin/Catalogs/Rooms/Edit",     HttpStatusCode.Forbidden)]
    [InlineData("бухгалтер", "/Admin/Catalogs/Rooms",          HttpStatusCode.OK)]
    [InlineData("бухгалтер", "/Admin/Catalogs/Seasons/Edit",   HttpStatusCode.Forbidden)]
    [InlineData("ресепшен",  "/Admin/Catalogs/Seasons/Edit/1", HttpStatusCode.Forbidden)]
    [InlineData("бухгалтер", "/Admin/Catalogs/Services/Edit",  HttpStatusCode.Forbidden)]
    [InlineData("админ",     "/Admin/Catalogs/Services/Edit",  HttpStatusCode.OK)]
    [InlineData("бухгалтер", "/Admin/Catalogs/Services/Delete/1", HttpStatusCode.Forbidden)]
    public async Task Access(string role, string url, HttpStatusCode expected) =>
        Assert.Equal(expected, (await _app.Client(role).GetAsync(url)).StatusCode);

    [Fact]
    public async Task Accountant_PostCreateSeason_Forbidden()
    {
        // Токен берём со страницы, куда бухгалтер пускается, — так запрос доходит до проверки роли
        var res = await _app.Client(Roles.Accountant).PostFormAsync("/Admin/Catalogs/Seasons/Edit/1",
            new() { ["Name"] = "x", ["DateStart"] = "2040-01-01", ["DateEnd"] = "2040-02-01" },
            postUrl: "/Admin/Catalogs/Seasons/Edit");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal(0L, await Sql.ScalarAsync("SELECT count(*) FROM seasons WHERE date_start = '2040-01-01'"));
    }

    [Fact]
    public async Task Accountant_PostDeleteService_Forbidden()
    {
        var res = await _app.Client(Roles.Accountant).PostRawAsync("/Admin/Catalogs/Services/Delete/1");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Reception_RoomEdit_ChangesOnlyStatus()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var room = t.RoomIds[0];
        var res = await _app.Client(Roles.Reception).PostFormAsync($"/Admin/Catalogs/Rooms/Edit/{room}", new()
        {
            ["Status"] = RoomStatus.Repair, ["RoomNumber"] = "999", ["Floor"] = "9", ["RoomTypeId"] = "1"
        });
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("ремонт|201|2", await Sql.ScalarAsync(
            "SELECT status || '|' || room_number || '|' || floor FROM rooms WHERE id = @id", ("id", room)));
    }

    [Fact]
    public async Task Delete_RoomWithBooking_Refused()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        await t.BookAsync(t.RoomIds[0], Clock.Today.AddDays(5), Clock.Today.AddDays(6));
        var res = await _app.Client(Roles.Admin).PostFormAsync($"/Admin/Catalogs/Rooms/Delete/{t.RoomIds[0]}", []);
        Assert.Contains("Нельзя удалить: на запись ссылаются брони номеров.", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Accountant_ChangesRate_DatesUntouched()
    {
        var seasonId = (int)(await Sql.ScalarAsync(
            "SELECT id FROM seasons WHERE @d BETWEEN date_start AND date_end", ("d", Clock.Today.AddDays(200))))!;
        var standard = (int)(await Sql.ScalarAsync("SELECT id FROM room_types WHERE name = 'Стандарт'"))!;
        var old = (decimal)(await Sql.ScalarAsync(
            "SELECT price_per_night FROM rates WHERE season_id = @s AND room_type_id = @t", ("s", seasonId), ("t", standard)))!;
        var oldStart = await Sql.ScalarAsync("SELECT date_start FROM seasons WHERE id = @s", ("s", seasonId));
        var types = await Sql.ScalarAsync("SELECT array_agg(id ORDER BY name) FROM room_types") as int[];
        try
        {
            var form = new Dictionary<string, string> { ["DateStart"] = "2001-01-01", ["Name"] = "взлом" };
            for (var i = 0; i < types!.Length; i++)
            {
                var price = (decimal?)await Sql.ScalarAsync(
                    "SELECT price_per_night FROM rates WHERE season_id = @s AND room_type_id = @t", ("s", seasonId), ("t", types[i]));
                form[$"Prices[{i}].RoomTypeId"] = types[i].ToString();
                form[$"Prices[{i}].Price"] = types[i] == standard ? "77" : price?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
            }
            var res = await _app.Client(Roles.Accountant).PostFormAsync($"/Admin/Catalogs/Seasons/Edit/{seasonId}", form);
            Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
            Assert.Equal(77m, await Sql.ScalarAsync(
                "SELECT price_per_night FROM rates WHERE season_id = @s AND room_type_id = @t", ("s", seasonId), ("t", standard)));
            Assert.Equal(oldStart, await Sql.ScalarAsync("SELECT date_start FROM seasons WHERE id = @s", ("s", seasonId)));
        }
        finally
        {
            await Sql.ExecAsync("UPDATE rates SET price_per_night = @p WHERE season_id = @s AND room_type_id = @t",
                ("p", old), ("s", seasonId), ("t", standard));
        }
    }

    [Fact]
    public async Task Admin_OverlappingSeason_Message()
    {
        var res = await _app.Client(Roles.Admin).PostFormAsync("/Admin/Catalogs/Seasons/Edit", new()
        {
            ["Name"] = "пересечение", ["DateStart"] = Clock.Today.ToString("yyyy-MM-dd"), ["DateEnd"] = Clock.Today.AddDays(3).ToString("yyyy-MM-dd")
        });
        Assert.Contains("Сезон пересекается с другим сезоном.", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Accountant_ServiceEdit_ChangesOnlyPriceAndActive()
    {
        var id = (int)(await Sql.ScalarAsync("SELECT min(id) FROM services"))!;
        var before = (string)(await Sql.ScalarAsync("SELECT name || '|' || price || '|' || is_active FROM services WHERE id = @id", ("id", id)))!;
        try
        {
            var res = await _app.Client(Roles.Accountant).PostFormAsync($"/Admin/Catalogs/Services/Edit/{id}", new()
            {
                ["Price"] = "99.5", ["IsActive"] = "false", ["Name"] = "взлом"
            });
            var body = await res.Content.ReadAsStringAsync();
            Assert.True(res.StatusCode == HttpStatusCode.Redirect, System.Text.RegularExpressions.Regex.Match(body, "validation-summary[^>]*>[\\s\\S]{0,400}").Value);
            var after = (string)(await Sql.ScalarAsync("SELECT name || '|' || price || '|' || is_active FROM services WHERE id = @id", ("id", id)))!;
            Assert.Equal(before.Split('|')[0] + "|99.50|false", after);
        }
        finally
        {
            var p = before.Split('|');
            await Sql.ExecAsync("UPDATE services SET price = @p::numeric, is_active = @a::boolean WHERE id = @id",
                ("p", p[1]), ("a", p[2]), ("id", id));
        }
    }

    [Fact]
    public async Task Delete_ServiceWithOrders_Refused()
    {
        var id = (int)(await Sql.ScalarAsync("SELECT min(service_id) FROM service_orders"))!;
        var res = await _app.Client(Roles.Admin).PostFormAsync($"/Admin/Catalogs/Services/Delete/{id}", []);
        Assert.Contains("Нельзя удалить: на запись ссылаются заказы услуг.", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_RoomType_AmenitiesReplaced()
    {
        var name = "Тип " + Guid.NewGuid().ToString("N")[..8];
        var ids = (int[])(await Sql.ScalarAsync("SELECT array_agg(id ORDER BY id) FROM amenities"))!;
        var admin = _app.Client(Roles.Admin);
        try
        {
            var create = new FormUrlEncodedContent([
                new("Name", name), new("Capacity", "2"), new("BedType", "двуспальная"),
                new("AmenityIds", ids[0].ToString()), new("AmenityIds", ids[1].ToString()),
                new("__RequestVerificationToken", FormPosting.Token(await admin.GetStringAsync("/Admin/Catalogs/RoomTypes/Edit")))]);
            Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Admin/Catalogs/RoomTypes/Edit", create)).StatusCode);
            var typeId = (int)(await Sql.ScalarAsync("SELECT id FROM room_types WHERE name = @n", ("n", name)))!;

            var url = $"/Admin/Catalogs/RoomTypes/Edit/{typeId}";
            var edit = new FormUrlEncodedContent([
                new("Name", name), new("Capacity", "2"), new("AmenityIds", ids[2].ToString()),
                new("__RequestVerificationToken", FormPosting.Token(await admin.GetStringAsync(url)))]);
            Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync(url, edit)).StatusCode);
            Assert.Equal(new[] { ids[2] }, await Sql.ScalarAsync(
                "SELECT array_agg(amenity_id) FROM room_type_amenities WHERE room_type_id = @t", ("t", typeId)));
        }
        finally
        {
            await Sql.ExecAsync("DELETE FROM room_types WHERE name = @n", ("n", name));
        }
    }

    [Theory]
    [InlineData(null,        "/")]
    [InlineData("админ",     "/Admin/Catalogs/Seasons")]
    [InlineData("админ",     "/Admin/Catalogs/Services")]
    [InlineData("бухгалтер", "/Admin/Catalogs/Seasons/Edit/1")]
    public async Task NoTenge_OnPages(string? role, string url)
    {
        var html = await _app.Client(role).GetStringAsync(url);
        Assert.DoesNotContain("₸", html);
        Assert.DoesNotContain(" тг", html);
        Assert.Contains("$", html);
    }
}
