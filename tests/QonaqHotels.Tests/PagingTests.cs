using System.Text.RegularExpressions;
using QonaqHotels.Data;

namespace QonaqHotels.Tests;

// Списки админки отдаются порциями по 50 строк с кнопкой «Показать ещё»
public class PagingTests : IClassFixture<AppFactory>
{
    private readonly HttpClient _client;

    public PagingTests(AppFactory app)
    {
        _client = app.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Test-Role", Uri.EscapeDataString("ресепшен"));
    }

    private static int Rows(string html) => Regex.Matches(html, "<tr>").Count;

    [Fact]
    public async Task Bookings_FirstPage_Has50RowsAndMoreButton()
    {
        var html = await _client.GetStringAsync("/Admin/Bookings");
        Assert.Contains("data-next-page=\"2\"", html);
        Assert.Equal(50 + 1, Rows(html));               // 50 броней + строка шапки
    }

    [Fact]
    public async Task Bookings_Pages_DoNotRepeat_AndLastPageHasNoButton()
    {
        // в seed сейчас заселено меньше 50 броней — одна порция, без кнопки
        var checkedIn = await _client.GetStringAsync("/Admin/Bookings?handler=Search&status=" + Uri.EscapeDataString("заселён"));
        Assert.DoesNotContain("data-next-page", checkedIn);
        Assert.InRange(Rows(checkedIn), 1, 49);

        // только давние брони из seed: другие тесты параллельно создают брони на будущие даты,
        // и без фильтра новые строки сдвигали бы порции между двумя запросами
        var old = "&checkinTo=" + Clock.Today.AddDays(-100).ToString("yyyy-MM-dd");
        var first  = await _client.GetStringAsync("/Admin/Bookings?handler=Search&pageNo=1" + old);
        var second = await _client.GetStringAsync("/Admin/Bookings?handler=Search&pageNo=2" + old);
        Assert.Contains("data-next-page=\"2\"", first);
        Assert.Contains("data-next-page=\"3\"", second);

        static HashSet<string> Ids(string html) =>
            Regex.Matches(html, @"/Admin/Bookings/Edit/(\d+)").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Equal(50, Ids(first).Count);
        Assert.Equal(50, Ids(second).Count);
        Assert.Empty(Ids(first).Intersect(Ids(second)));
    }

    [Fact]
    public async Task Guests_SmallList_NoMoreButton()
    {
        var html = await _client.GetStringAsync("/Admin/Guests?handler=Search&q=" + Uri.EscapeDataString("Ахметов"));
        Assert.DoesNotContain("data-next-page", html);
        Assert.Contains("Ахметов", html);
    }
}
