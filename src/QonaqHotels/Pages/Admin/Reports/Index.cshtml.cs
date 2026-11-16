using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Reports;

[Authorize(Policy = "RequireStaff")]
public class IndexModel : PageModel
{
    private const string RoomTypesSql = "SELECT id, name FROM room_types ORDER BY name";

    // Что показываем на странице; остальные листы — только в Excel
    private static readonly string[] PreviewSheets = ["Сводка", "По месяцам", "Типы по дням недели"];
    private const int TopRooms = 10;

    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public int[] Hotels { get; set; } = [];
    [BindProperty(SupportsGet = true)] public int[] Types { get; set; } = [];

    public List<(string Value, string Label)> HotelOptions { get; private set; } = [];
    public List<(string Value, string Label)> TypeOptions { get; private set; } = [];
    public List<(string Label, DateOnly From, DateOnly To)> Presets { get; private set; } = [];
    public List<ReportTable> Preview { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();
        var p = await PrepareAsync(conn);
        Error = p.Validate();
        if (Error != null) return;

        var tables = await OccupancyReport.BuildAsync(conn, p);
        Preview = tables.Where(t => PreviewSheets.Contains(t.Name)).ToList();

        var rating = tables.Single(t => t.Name == "Рейтинг номеров");
        Preview.Add(new ReportTable
        {
            Name = rating.Name, Title = $"Самые популярные номера (первые {TopRooms})",
            Columns = rating.Columns, Rows = rating.Rows.Take(TopRooms).ToList()
        });
    }

    public async Task<IActionResult> OnGetExcelAsync()
    {
        await using var conn = await _db.OpenAsync();
        var p = await PrepareAsync(conn);
        Error = p.Validate();
        if (Error != null) return Page();

        var tables = await OccupancyReport.BuildAsync(conn, p);
        return File(ReportWorkbook.ToXlsx(tables),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"zagruzka_{p.From:yyyy-MM-dd}_{p.To:yyyy-MM-dd}.xlsx");
    }

    private async Task<ReportParams> PrepareAsync(Npgsql.NpgsqlConnection conn)
    {
        HotelOptions = await AdminLists.OptionsAsync(conn, AdminLists.Hotels);
        TypeOptions  = await AdminLists.OptionsAsync(conn, RoomTypesSql);

        var today = Clock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var weekStart  = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));   // понедельник
        var quarterStart = new DateOnly(today.Year, (today.Month - 1) / 3 * 3 + 1, 1);
        Presets =
        [
            ("Эта неделя",       weekStart,               weekStart.AddDays(6)),
            ("Прошлая неделя",   weekStart.AddDays(-7),   weekStart.AddDays(-1)),
            ("Этот месяц",       monthStart,              monthStart.AddMonths(1).AddDays(-1)),
            ("Прошлый месяц",    monthStart.AddMonths(-1), monthStart.AddDays(-1)),
            ("Квартал",          quarterStart,            quarterStart.AddMonths(3).AddDays(-1)),
            ("Этот год",         new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)),
            ("Последние 12 мес.", monthStart.AddMonths(-11), monthStart.AddMonths(1).AddDays(-1))
        ];

        // По умолчанию — текущий месяц
        From ??= monthStart;
        To   ??= monthStart.AddMonths(1).AddDays(-1);
        return new ReportParams(From.Value, To.Value, Hotels, Types);
    }

    // Адрес страницы с другим периодом, фильтры отелей и типов сохраняются
    public string PresetUrl(DateOnly from, DateOnly to)
    {
        var parts = new List<string> { $"from={from:yyyy-MM-dd}", $"to={to:yyyy-MM-dd}" };
        parts.AddRange(Hotels.Select(h => $"hotels={h}"));
        parts.AddRange(Types.Select(t => $"types={t}"));
        return "/Admin/Reports?" + string.Join("&", parts);
    }

    public static string Format(object? value, Fmt format)
    {
        if (value == null) return "—";
        var ru = CultureInfo.GetCultureInfo("ru-RU");
        return format switch
        {
            Fmt.Money   => Money.Usd(Math.Round(Convert.ToDecimal(value))),
            Fmt.Percent => (Convert.ToDecimal(value) * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%",
            Fmt.Delta   => (Convert.ToDecimal(value) * 100).ToString("+0.0;−0.0;0.0", CultureInfo.InvariantCulture) + " п.п.",
            Fmt.Num1    => Convert.ToDecimal(value).ToString("0.0", CultureInfo.InvariantCulture),
            Fmt.Date    => Convert.ToDateTime(value).ToString("dd.MM.yyyy"),
            Fmt.Month   => Convert.ToDateTime(value).ToString("MMMM yyyy", ru),
            _           => value.ToString() ?? ""
        };
    }
}
