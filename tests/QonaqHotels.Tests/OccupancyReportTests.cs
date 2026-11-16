using ClosedXML.Excel;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Pages.Admin.Reports;

namespace QonaqHotels.Tests;

// Отчёт о загрузке на известных данных: тестовый отель с двумя номерами «Люкс».
//   номер 1: 3 ночи (D, D+1, D+2), номер 2: 1 ночь (D+1), ещё одна бронь отменена.
// Период D..D+3 — 4 ночи × 2 номера = 8 доступных, 4 проданных.
public class OccupancyReportTests
{
    private readonly Db _db = TestDb.Create();
    private static readonly DateOnly D = Clock.Today.AddDays(40);

    private async Task<(TestHotel Hotel, List<ReportTable> Tables)> BuildAsync()
    {
        var t = await TestHotel.CreateAsync(_db, rooms: 2);
        await t.BookAsync(t.RoomIds[0], D, D.AddDays(3), source: "сайт");
        await t.BookAsync(t.RoomIds[1], D.AddDays(1), D.AddDays(2), source: "телефон");
        await t.BookAsync(t.RoomIds[1], D.AddDays(2), D.AddDays(4), status: "отменена");

        await using var conn = await _db.OpenAsync();
        var tables = await OccupancyReport.BuildAsync(conn, new ReportParams(D, D.AddDays(3), [t.HotelId]));
        return (t, tables);
    }

    private static ReportTable Sheet(List<ReportTable> tables, string name) => tables.Single(x => x.Name == name);

    private static object? Cell(ReportTable t, object?[] row, string header) =>
        row[t.Columns.FindIndex(c => c.Header == header)];

    [Fact]
    public async Task Summary_CountsNightsRevenueAndCancellations()
    {
        var (hotel, tables) = await BuildAsync();
        await using var _ = hotel;

        // Выручка вручную: тариф «Люкс» по каждой проданной ночи
        decimal expected;
        await using (var conn = await _db.OpenAsync())
        await using (var cmd = new NpgsqlCommand("""
            SELECT sum(ra.price_per_night)
            FROM unnest(@nights::date[]) AS n(d)
            JOIN seasons s ON n.d BETWEEN s.date_start AND s.date_end
            JOIN rates ra  ON ra.season_id = s.id AND ra.room_type_id = @rt
            """, conn))
        {
            cmd.Parameters.AddWithValue("nights", new[] { D, D.AddDays(1), D.AddDays(2), D.AddDays(1) });
            cmd.Parameters.AddWithValue("rt", hotel.RoomTypeId);
            expected = (decimal)(await cmd.ExecuteScalarAsync())!;
        }

        var s = Sheet(tables, "Сводка");
        Assert.Equal(2, s.Rows.Count);                       // отель + итог
        foreach (var row in s.Rows)
        {
            Assert.Equal(2, Cell(s, row, "Номеров"));
            Assert.Equal(8, Cell(s, row, "Доступно ночей"));
            Assert.Equal(4, Cell(s, row, "Продано ночей"));
            Assert.Equal(0.5m, Cell(s, row, "Загрузка"));
            Assert.Equal(expected, Cell(s, row, "Выручка"));
            Assert.Equal(expected / 4, Cell(s, row, "ADR"));
            Assert.Equal(expected / 8, Cell(s, row, "RevPAR"));
            Assert.Equal(2, Cell(s, row, "Броней"));
            Assert.Equal(2m, Cell(s, row, "Ср. ночей на бронь"));
            Assert.Equal(1, Cell(s, row, "Отмен"));
        }
        Assert.Equal("Итого", s.Rows[1][0]);
    }

    [Fact]
    public async Task Rating_PutsBusiestRoomFirst()
    {
        var (hotel, tables) = await BuildAsync();
        await using var _ = hotel;

        var r = Sheet(tables, "Рейтинг номеров");
        Assert.Equal(2, r.Rows.Count);
        Assert.Equal("201", Cell(r, r.Rows[0], "Номер"));
        Assert.Equal(3, Cell(r, r.Rows[0], "Продано ночей"));
        Assert.Equal(1, Cell(r, r.Rows[0], "Простой, ночей"));
        Assert.Equal(2, Cell(r, r.Rows[1], "Место"));
        Assert.Equal(1, Cell(r, r.Rows[1], "Продано ночей"));
    }

    [Fact]
    public async Task ByDays_OneRowPerNight_WithHotelColumn()
    {
        var (hotel, tables) = await BuildAsync();
        await using var _ = hotel;

        var d = Sheet(tables, "По дням");
        Assert.Equal(4, d.Rows.Count);
        Assert.Equal(new[] { 1, 2, 1, 0 }, d.Rows.Select(r => (int)Cell(d, r, "Продано ночей")!).ToArray());
        Assert.Equal(new[] { 0.5m, 1m, 0.5m, 0m }, d.Rows.Select(r => (decimal)Cell(d, r, "Все отели")!).ToArray());
        Assert.Equal(1m, d.Rows[1][^1]);                      // столбец отеля
    }

    [Fact]
    public async Task WeeksAndMonths_SumToTotal()
    {
        var (hotel, tables) = await BuildAsync();
        await using var _ = hotel;

        foreach (var name in new[] { "По неделям", "По месяцам", "Источники", "Будни и выходные" })
        {
            var t = Sheet(tables, name);
            Assert.Equal(4, t.Rows.Sum(r => (int)Cell(t, r, "Продано ночей")!));
        }
        Assert.Equal(2, Sheet(tables, "Брони").Rows.Count);   // отменённая бронь не входит
    }

    [Fact]
    public async Task Xlsx_HasSheetPerTable()
    {
        var (hotel, tables) = await BuildAsync();
        await using var _ = hotel;

        using var wb = new XLWorkbook(new MemoryStream(ReportWorkbook.ToXlsx(tables)));
        Assert.Equal(tables.Select(t => t.Name), wb.Worksheets.Select(w => w.Name));
        var ws = wb.Worksheet("Сводка");
        Assert.Equal("Отель", ws.Cell(4, 1).GetString());
        Assert.Equal(4, ws.Cell(5, 4).GetValue<int>());       // «Продано ночей»
    }

    [Fact]
    public void Params_Validate()
    {
        Assert.Null(new ReportParams(D, D).Validate());
        Assert.NotNull(new ReportParams(D, D.AddDays(-1)).Validate());
        Assert.NotNull(new ReportParams(D, D.AddDays(ReportParams.MaxDays)).Validate());
    }
}
