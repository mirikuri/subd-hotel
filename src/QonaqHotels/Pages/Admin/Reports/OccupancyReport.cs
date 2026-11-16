using Npgsql;
using NpgsqlTypes;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Reports;

// Как показывать значение столбца (в Excel и на странице)
public enum Fmt { Text, Int, Money, Percent, Delta, Num1, Date, Month }

public record ReportColumn(string Header, Fmt Format);

// Один лист отчёта: готовая таблица, посчитанная SQL-запросом
public class ReportTable
{
    public required string Name { get; init; }          // имя листа (до 31 символа)
    public required string Title { get; init; }
    public string? Note { get; init; }
    public required List<ReportColumn> Columns { get; init; }
    public List<object?[]> Rows { get; init; } = [];
    public int? HeatFrom { get; init; }                  // с какого столбца (0-based) красить цветовой шкалой
    public bool LastRowIsTotal { get; init; }
}

// Параметры отчёта. Период — ночи с From по To включительно (ночь d = с d на d+1).
public record ReportParams(DateOnly From, DateOnly To, int[]? HotelIds = null, int[]? RoomTypeIds = null)
{
    public const int MaxDays = 731;

    public int Days => To.DayNumber - From.DayNumber + 1;

    public string? Validate()
    {
        if (To < From)      return "Конец периода раньше начала.";
        if (Days > MaxDays) return "Период — не больше двух лет.";
        return null;
    }
}

// Отчёт о загрузке. Все показатели считает PostgreSQL; C# только раскладывает строки по таблицам.
//
// Основа — временная таблица rn («номеро-ночи»): строка на каждую пару (номер, ночь периода).
//   sold     — на эту ночь есть АКТИВНАЯ строка booking_rooms (date_from <= d < date_to).
//              Отменённые брони неактивны и не считаются; будущие брони считаются.
//              EXCLUDE-ограничение booking_rooms гарантирует не больше одной такой строки на ночь.
//   revenue  — тариф типа номера в сезоне этой ночи (rates × seasons), 0 если ночь не продана.
// Загрузка = проданные / доступные, ADR = выручка / проданные, RevPAR = выручка / доступные.
// Номера на ремонте считаются доступными: истории ремонтов в БД нет.
public static class OccupancyReport
{
    private const string FillSql = """
        CREATE TEMP TABLE rn (
            room_id int, hotel_id int, room_type_id int, d date,
            booking_id int, sold boolean, revenue numeric, no_rate boolean, season_id int
        ) ON COMMIT DROP;

        INSERT INTO rn
        SELECT r.id, r.hotel_id, r.room_type_id, g.d::date,
               br.booking_id,
               br.booking_id IS NOT NULL,
               CASE WHEN br.booking_id IS NOT NULL THEN coalesce(ra.price_per_night, 0) ELSE 0 END,
               br.booking_id IS NOT NULL AND ra.id IS NULL,
               s.id
        FROM rooms r
        CROSS JOIN generate_series(@from::date, @to::date, interval '1 day') AS g(d)
        LEFT JOIN booking_rooms br
               ON br.room_id = r.id AND br.is_active
              AND br.date_from <= g.d::date AND g.d::date < br.date_to
        LEFT JOIN seasons s ON g.d::date BETWEEN s.date_start AND s.date_end
        LEFT JOIN rates ra  ON ra.season_id = s.id AND ra.room_type_id = r.room_type_id
        WHERE (@hotels::int[] IS NULL OR r.hotel_id = ANY(@hotels))
          AND (@types::int[]  IS NULL OR r.room_type_id = ANY(@types));
        """;

    // Общие показатели группы строк rn
    private const string Avail   = "count(*)::int";
    private const string Sold    = "(count(*) FILTER (WHERE sold))::int";
    private const string Occ     = "(count(*) FILTER (WHERE sold))::numeric / count(*)";
    private const string Revenue = "sum(revenue)";
    private const string Adr     = "sum(revenue) / nullif(count(*) FILTER (WHERE sold), 0)";
    private const string RevPar  = "sum(revenue) / count(*)";
    private const string Metrics = $"{Avail}, {Sold}, {Occ}, {Revenue}, {Adr}, {RevPar}";

    // Выходная ночь — с пятницы на субботу и с субботы на воскресенье
    private const string IsWeekend = "extract(isodow FROM d) IN (5, 6)";
    private const string DowNames  = "ARRAY['пн','вт','ср','чт','пт','сб','вс']";

    private static readonly ReportColumn[] MetricCols =
    [
        new("Доступно ночей", Fmt.Int), new("Продано ночей", Fmt.Int), new("Загрузка", Fmt.Percent),
        new("Выручка", Fmt.Money), new("ADR", Fmt.Money), new("RevPAR", Fmt.Money)
    ];

    public static async Task<List<ReportTable>> BuildAsync(NpgsqlConnection conn, ReportParams p)
    {
        // Одна транзакция: все листы видят один снимок данных, rn исчезает при COMMIT
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);

        await using (var fill = new NpgsqlCommand(FillSql, conn, tx))
        {
            fill.Parameters.AddWithValue("from", p.From);
            fill.Parameters.AddWithValue("to",   p.To);
            fill.Parameters.Add(IntArray("hotels", p.HotelIds));
            fill.Parameters.Add(IntArray("types",  p.RoomTypeIds));
            await fill.ExecuteNonQueryAsync();
        }

        var periodText = $"Период: {p.From:dd.MM.yyyy} – {p.To:dd.MM.yyyy} ({p.Days} ноч.)";
        var tables = new List<ReportTable>();

        // ── 1. Сводка по отелям + итог (GROUPING SETS) ──────────────────────────
        var noRate = (long)(await ScalarAsync(conn, tx, "SELECT count(*) FROM rn WHERE no_rate"))!;
        tables.Add(new ReportTable
        {
            Name = "Сводка", Title = "Загрузка отелей — сводка",
            Note = periodText + await FilterTextAsync(conn, tx, p)
                 + (noRate > 0 ? $" · Внимание: {noRate} проданных ночей без тарифа, их выручка = 0" : ""),
            Columns =
            [
                new("Отель", Fmt.Text), new("Номеров", Fmt.Int), .. MetricCols,
                new("Броней", Fmt.Int), new("Ср. ночей на бронь", Fmt.Num1),
                new("Отмен", Fmt.Int), new("Доля отмен", Fmt.Percent)
            ],
            LastRowIsTotal = true,
            Rows = await RowsAsync(conn, tx, $"""
                WITH agg AS (
                    SELECT hotel_id, grouping(hotel_id) AS is_total,
                           count(DISTINCT room_id)::int AS rooms,
                           {Avail} AS avail, {Sold} AS sold, {Occ} AS occ,
                           {Revenue} AS revenue, {Adr} AS adr, {RevPar} AS revpar,
                           count(DISTINCT booking_id)::int AS bookings
                    FROM rn
                    GROUP BY GROUPING SETS ((hotel_id), ())
                ),
                canc AS (   -- отменённые брони с плановым заездом в периоде
                    SELECT hotel_id, count(*)::int AS n
                    FROM bookings
                    WHERE status = @cancelled
                      AND check_in_plan BETWEEN @from AND @to
                      AND (@hotels::int[] IS NULL OR hotel_id = ANY(@hotels))
                    GROUP BY hotel_id
                )
                SELECT coalesce(h.name, 'Итого'), a.rooms, a.avail, a.sold, a.occ, a.revenue, a.adr, a.revpar,
                       a.bookings, a.sold::numeric / nullif(a.bookings, 0), x.n,
                       x.n::numeric / nullif(x.n + a.bookings, 0)
                FROM agg a
                LEFT JOIN hotels h ON h.id = a.hotel_id
                CROSS JOIN LATERAL (
                    SELECT coalesce(sum(c.n), 0)::int AS n
                    FROM canc c
                    WHERE a.is_total = 1 OR c.hotel_id = a.hotel_id
                ) x
                ORDER BY a.is_total, h.name
                """, p)
        });

        // ── 2. По типам номеров ─────────────────────────────────────────────────
        tables.Add(new ReportTable
        {
            Name = "По типам номеров", Title = "Загрузка по типам номеров", Note = periodText,
            Columns = [new("Отель", Fmt.Text), new("Тип номера", Fmt.Text), new("Номеров", Fmt.Int), .. MetricCols],
            Rows = await RowsAsync(conn, tx, $"""
                SELECT h.name, rt.name, count(DISTINCT rn.room_id)::int, {Metrics}
                FROM rn
                JOIN hotels h      ON h.id = rn.hotel_id
                JOIN room_types rt ON rt.id = rn.room_type_id
                GROUP BY h.name, rt.name
                ORDER BY h.name, rt.name
                """)
        });

        // ── 3–4. По неделям и по месяцам, с изменением к предыдущему периоду (lag) ──
        tables.Add(new ReportTable
        {
            Name = "По неделям", Title = "Загрузка по неделям (пн–вс)",
            Note = periodText + " · Крайние недели могут быть неполными",
            Columns =
            [
                new("Неделя с", Fmt.Date), new("по", Fmt.Date), .. MetricCols,
                new("Δ загрузки к пред. неделе", Fmt.Delta)
            ],
            Rows = await RowsAsync(conn, tx, $"""
                SELECT min(d), max(d), {Metrics},
                       {Occ} - lag({Occ}) OVER (ORDER BY date_trunc('week', d))
                FROM rn
                GROUP BY date_trunc('week', d)
                ORDER BY date_trunc('week', d)
                """)
        });

        tables.Add(new ReportTable
        {
            Name = "По месяцам", Title = "Загрузка по месяцам",
            Note = periodText + " · Крайние месяцы могут быть неполными",
            Columns =
            [
                new("Месяц", Fmt.Month), .. MetricCols,
                new("Δ загрузки к пред. месяцу", Fmt.Delta), new("Δ выручки к пред. месяцу", Fmt.Money)
            ],
            Rows = await RowsAsync(conn, tx, $"""
                SELECT date_trunc('month', d)::date, {Metrics},
                       {Occ} - lag({Occ}) OVER (ORDER BY date_trunc('month', d)),
                       {Revenue} - lag({Revenue}) OVER (ORDER BY date_trunc('month', d))
                FROM rn
                GROUP BY date_trunc('month', d)
                ORDER BY date_trunc('month', d)
                """)
        });

        // ── 5. По дням: строки — даты, столбцы — отели ──────────────────────────
        tables.Add(Pivot(
            name: "По дням", title: "Загрузка по дням и отелям", note: periodText,
            keyColumns: [new("Дата", Fmt.Date), new("День", Fmt.Text)], totalHeader: "Все отели",
            rows: await RowsAsync(conn, tx, $"""
                SELECT rn.d, ({DowNames})[extract(isodow FROM rn.d)::int], h.name,
                       {Sold}, {Revenue}, {Occ}
                FROM rn
                JOIN hotels h ON h.id = rn.hotel_id
                GROUP BY GROUPING SETS ((rn.d), (rn.d, h.name))
                ORDER BY rn.d, h.name NULLS FIRST
                """)));

        // ── 6. Будни и выходные ─────────────────────────────────────────────────
        tables.Add(new ReportTable
        {
            Name = "Будни и выходные", Title = "Будни и выходные",
            Note = periodText + " · Выходные — ночи с пятницы на субботу и с субботы на воскресенье",
            Columns = [new("Отель", Fmt.Text), new("Тип номера", Fmt.Text), new("Ночи", Fmt.Text), .. MetricCols],
            Rows = await RowsAsync(conn, tx, $"""
                SELECT h.name, rt.name, CASE WHEN {IsWeekend} THEN 'выходные' ELSE 'будни' END, {Metrics}
                FROM rn
                JOIN hotels h      ON h.id = rn.hotel_id
                JOIN room_types rt ON rt.id = rn.room_type_id
                GROUP BY h.name, rt.name, {IsWeekend}
                ORDER BY h.name, rt.name, {IsWeekend}
                """)
        });

        // ── 7. Когда популярны: тип номера × день недели ────────────────────────
        string DowOcc(int n) =>
            $"(count(*) FILTER (WHERE sold AND extract(isodow FROM d) = {n}))::numeric" +
            $" / nullif(count(*) FILTER (WHERE extract(isodow FROM d) = {n}), 0)";
        tables.Add(new ReportTable
        {
            Name = "Типы по дням недели", Title = "Когда популярны: загрузка типов номеров по дням недели",
            Note = periodText + " · День недели — день начала ночи",
            Columns =
            [
                new("Тип номера", Fmt.Text),
                .. new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" }.Select(x => new ReportColumn(x, Fmt.Percent)),
                new("В среднем", Fmt.Percent), new("Самый загруженный день", Fmt.Text)
            ],
            HeatFrom = 1, LastRowIsTotal = true,
            Rows = await RowsAsync(conn, tx, $"""
                SELECT coalesce(rt.name, 'Все типы'),
                       {string.Join(", ", Enumerable.Range(1, 7).Select(DowOcc))},
                       {Occ},
                       ({DowNames})[mode() WITHIN GROUP (ORDER BY extract(isodow FROM d)::int) FILTER (WHERE sold)]
                FROM rn
                JOIN room_types rt ON rt.id = rn.room_type_id
                GROUP BY GROUPING SETS ((rt.name), ())
                ORDER BY grouping(rt.name), rt.name
                """)
        });

        // ── 8. Когда популярны: месяц × тип номера ──────────────────────────────
        tables.Add(Pivot(
            name: "Типы по месяцам", title: "Когда популярны: загрузка типов номеров по месяцам", note: periodText,
            keyColumns: [new("Месяц", Fmt.Month)], totalHeader: "Все типы",
            rows: await RowsAsync(conn, tx, $"""
                SELECT date_trunc('month', rn.d)::date, rt.name, {Sold}, {Revenue}, {Occ}
                FROM rn
                JOIN room_types rt ON rt.id = rn.room_type_id
                GROUP BY GROUPING SETS ((date_trunc('month', rn.d)), (date_trunc('month', rn.d), rt.name))
                ORDER BY 1, rt.name NULLS FIRST
                """)));

        // ── 9. Рейтинг номеров ──────────────────────────────────────────────────
        tables.Add(new ReportTable
        {
            Name = "Рейтинг номеров", Title = "Какие номера популярны: рейтинг по проданным ночам",
            Note = periodText + " · Внизу списка — самые простаивающие номера",
            Columns =
            [
                new("Место", Fmt.Int), new("Отель", Fmt.Text), new("Номер", Fmt.Text), new("Этаж", Fmt.Int),
                new("Тип номера", Fmt.Text), new("Продано ночей", Fmt.Int), new("Простой, ночей", Fmt.Int),
                new("Загрузка", Fmt.Percent), new("Выручка", Fmt.Money), new("Броней", Fmt.Int),
                new("Чаще всего занят в", Fmt.Text), new("Пиковый месяц", Fmt.Month)
            ],
            HeatFrom = 7,
            Rows = await RowsAsync(conn, tx, $"""
                SELECT rank() OVER (ORDER BY {Sold} DESC, {Revenue} DESC)::int,
                       h.name, r.room_number, r.floor::int, rt.name,
                       {Sold}, {Avail} - {Sold}, {Occ}, {Revenue},
                       count(DISTINCT rn.booking_id)::int,
                       ({DowNames})[mode() WITHIN GROUP (ORDER BY extract(isodow FROM rn.d)::int) FILTER (WHERE sold)],
                       mode() WITHIN GROUP (ORDER BY date_trunc('month', rn.d)::date) FILTER (WHERE sold)
                FROM rn
                JOIN rooms r       ON r.id = rn.room_id
                JOIN hotels h      ON h.id = rn.hotel_id
                JOIN room_types rt ON rt.id = rn.room_type_id
                GROUP BY h.name, r.id, r.room_number, r.floor, rt.name
                ORDER BY 1, h.name, r.room_number
                """)
        });

        // ── 10. По сезонам ──────────────────────────────────────────────────────
        tables.Add(new ReportTable
        {
            Name = "По сезонам", Title = "Загрузка по сезонам", Note = periodText,
            Columns =
            [
                new("Отель", Fmt.Text), new("Сезон", Fmt.Text),
                new("Ночи с", Fmt.Date), new("по", Fmt.Date), .. MetricCols
            ],
            Rows = await RowsAsync(conn, tx, $"""
                SELECT h.name, coalesce(s.name, 'вне сезонов'), min(rn.d), max(rn.d), {Metrics}
                FROM rn
                JOIN hotels h       ON h.id = rn.hotel_id
                LEFT JOIN seasons s ON s.id = rn.season_id
                GROUP BY h.name, s.id, s.name
                ORDER BY h.name, min(rn.d)
                """)
        });

        // ── 11. Источники броней ────────────────────────────────────────────────
        tables.Add(new ReportTable
        {
            Name = "Источники", Title = "Источники броней", Note = periodText,
            Columns =
            [
                new("Источник", Fmt.Text), new("Броней", Fmt.Int), new("Продано ночей", Fmt.Int),
                new("Выручка", Fmt.Money), new("Доля ночей", Fmt.Percent), new("Доля выручки", Fmt.Percent)
            ],
            Rows = await RowsAsync(conn, tx, """
                SELECT coalesce(b.source, 'не указан'), count(DISTINCT b.id)::int, count(*)::int, sum(rn.revenue),
                       count(*)::numeric / sum(count(*)) OVER (),
                       sum(rn.revenue) / nullif(sum(sum(rn.revenue)) OVER (), 0)
                FROM rn
                JOIN bookings b ON b.id = rn.booking_id
                GROUP BY b.source
                ORDER BY count(*) DESC
                """)
        });

        // ── 12. Брони, из которых сложились цифры ───────────────────────────────
        tables.Add(new ReportTable
        {
            Name = "Брони", Title = "Брони, занимающие номера в периоде", Note = periodText,
            Columns =
            [
                new("№ брони", Fmt.Int), new("Отель", Fmt.Text), new("Номер", Fmt.Text), new("Тип номера", Fmt.Text),
                new("Гость", Fmt.Text), new("Заезд", Fmt.Date), new("Выезд", Fmt.Date),
                new("Ночей в периоде", Fmt.Int), new("Статус", Fmt.Text), new("Источник", Fmt.Text),
                new("Выручка в периоде", Fmt.Money)
            ],
            Rows = await RowsAsync(conn, tx, """
                SELECT b.id, h.name, r.room_number, rt.name, g.full_name,
                       b.check_in_plan, b.check_out_plan, count(*)::int, b.status, b.source, sum(rn.revenue)
                FROM rn
                JOIN bookings b    ON b.id = rn.booking_id
                JOIN guests g      ON g.id = b.guest_id
                JOIN rooms r       ON r.id = rn.room_id
                JOIN hotels h      ON h.id = rn.hotel_id
                JOIN room_types rt ON rt.id = rn.room_type_id
                GROUP BY b.id, h.name, r.id, r.room_number, rt.name, g.full_name
                ORDER BY b.check_in_plan, b.id
                """)
        });

        tables.Add(new ReportTable
        {
            Name = "Пояснения", Title = "Как считаются показатели",
            Columns = [new("Показатель", Fmt.Text), new("Определение", Fmt.Text)],
            Rows =
            [
                ["Доступная номеро-ночь", "Пара (номер, ночь периода). Номера на ремонте считаются доступными."],
                ["Проданная номеро-ночь", "На эту ночь у номера есть активная бронь (заезд ≤ ночь < выезд). Отменённые брони не считаются, будущие — считаются."],
                ["Загрузка", "Проданные ночи / доступные ночи."],
                ["Выручка", "Сумма тарифов (тип номера × сезон) по проданным ночам, $. Скидки и услуги не учитываются."],
                ["ADR", "Средняя цена проданной ночи: выручка / проданные ночи."],
                ["RevPAR", "Выручка на доступный номер: выручка / доступные ночи."],
                ["Ср. ночей на бронь", "Проданные ночи периода / число броней, занимавших номера в периоде."],
                ["Отмены", "Брони со статусом «отменена» и плановым заездом в периоде. Доля = отмены / (отмены + брони)."],
                ["Выходные", "Ночи с пятницы на субботу и с субботы на воскресенье."],
                ["Сформирован", DateTime.Now.ToString("dd.MM.yyyy HH:mm")]
            ]
        });

        await tx.CommitAsync();
        return tables;
    }

    // Длинный результат (ключ…, категория, продано, выручка, загрузка) → широкая таблица:
    // ключ…, Продано, Выручка, <итог>, <категория 1>, <категория 2>… (в ячейках — загрузка).
    // Строка с категорией NULL — итог по ключу (её даёт GROUPING SETS).
    private static ReportTable Pivot(string name, string title, string note,
        ReportColumn[] keyColumns, string totalHeader, List<object?[]> rows)
    {
        var k = keyColumns.Length;
        var cats = rows.Select(r => r[k] as string).Where(c => c != null).Distinct().Order().ToList();
        var fixedCols = k + 3;
        var result = new List<object?[]>();
        object?[]? current = null;

        foreach (var r in rows)
        {
            if (current == null || !Equals(current[0], r[0]))
            {
                current = new object?[fixedCols + cats.Count];
                Array.Copy(r, current, k);
                result.Add(current);
            }
            if (r[k] is string cat)
                current[fixedCols + cats.IndexOf(cat)] = r[k + 3];
            else
            {
                current[k]     = r[k + 1];
                current[k + 1] = r[k + 2];
                current[k + 2] = r[k + 3];
            }
        }

        return new ReportTable
        {
            Name = name, Title = title, Note = note,
            Columns =
            [
                .. keyColumns, new("Продано ночей", Fmt.Int), new("Выручка", Fmt.Money),
                new(totalHeader, Fmt.Percent), .. cats.Select(c => new ReportColumn(c!, Fmt.Percent))
            ],
            Rows = result,
            HeatFrom = k + 2
        };
    }

    private static async Task<string> FilterTextAsync(NpgsqlConnection conn, NpgsqlTransaction tx, ReportParams p)
    {
        var text = "";
        if (p.HotelIds is { Length: > 0 })
            text += " · Отели: " + await NamesAsync(conn, tx, "hotels", p.HotelIds);
        if (p.RoomTypeIds is { Length: > 0 })
            text += " · Типы: " + await NamesAsync(conn, tx, "room_types", p.RoomTypeIds);
        return text;
    }

    // table — только литералы из кода выше, не пользовательский ввод
    private static async Task<string> NamesAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string table, int[] ids)
    {
        await using var cmd = new NpgsqlCommand(
            $"SELECT coalesce(string_agg(name, ', ' ORDER BY name), '') FROM {table} WHERE id = ANY(@ids)", conn, tx);
        cmd.Parameters.AddWithValue("ids", ids);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task<List<object?[]>> RowsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string sql, ReportParams? p = null)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        if (p != null)
        {
            cmd.Parameters.AddWithValue("from", p.From);
            cmd.Parameters.AddWithValue("to",   p.To);
            cmd.Parameters.AddWithValue("cancelled", BookingStatus.Cancelled);
            cmd.Parameters.Add(IntArray("hotels", p.HotelIds));
        }

        var rows = new List<object?[]>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var row = new object?[r.FieldCount];
            for (var i = 0; i < row.Length; i++)
                row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private static NpgsqlParameter IntArray(string name, int[]? ids) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Integer)
            { Value = ids is { Length: > 0 } ? ids : DBNull.Value };
}
