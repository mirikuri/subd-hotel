using ClosedXML.Excel;

namespace QonaqHotels.Pages.Admin.Reports;

// Оформляет готовые таблицы отчёта в книгу .xlsx: лист на таблицу.
// Ничего не считает — только форматы, шапка, автофильтр и цветовые шкалы.
public static class ReportWorkbook
{
    private const int HeaderRow = 4;   // 1 — заголовок, 2 — примечание, 3 — пусто

    public static byte[] ToXlsx(IEnumerable<ReportTable> tables)
    {
        using var wb = new XLWorkbook();
        foreach (var t in tables) AddSheet(wb, t);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void AddSheet(XLWorkbook wb, ReportTable t)
    {
        var ws = wb.Worksheets.Add(t.Name);
        var cols = t.Columns.Count;

        ws.Cell(1, 1).Value = t.Title;
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        if (t.Note != null)
        {
            ws.Cell(2, 1).Value = t.Note;
            ws.Cell(2, 1).Style.Font.SetFontColor(XLColor.Gray);
        }

        for (var c = 0; c < cols; c++)
            ws.Cell(HeaderRow, c + 1).Value = t.Columns[c].Header;
        var header = ws.Range(HeaderRow, 1, HeaderRow, cols);
        header.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#1F3A5F"))
              .Font.SetFontColor(XLColor.White)
              .Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Center);

        for (var r = 0; r < t.Rows.Count; r++)
            for (var c = 0; c < cols; c++)
                SetCell(ws.Cell(HeaderRow + 1 + r, c + 1), t.Rows[r][c], t.Columns[c].Format);

        var lastRow = HeaderRow + t.Rows.Count;
        for (var c = 0; c < cols; c++)
        {
            var format = NumberFormat(t.Columns[c].Format);
            if (format != null && t.Rows.Count > 0)
                ws.Range(HeaderRow + 1, c + 1, lastRow, c + 1).Style.NumberFormat.Format = format;
        }

        if (t.Rows.Count > 0)
        {
            var dataLast = t.LastRowIsTotal ? lastRow - 1 : lastRow;
            if (t.LastRowIsTotal)
                ws.Range(lastRow, 1, lastRow, cols).Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin);

            // Цветовая шкала: низкая загрузка — красная, высокая — зелёная
            if (t.HeatFrom is { } heat && dataLast > HeaderRow)
                for (var c = heat; c < cols; c++)
                    if (t.Columns[c].Format == Fmt.Percent)
                        ws.Range(HeaderRow + 1, c + 1, dataLast, c + 1).AddConditionalFormat()
                          .ColorScale()
                          .LowestValue(XLColor.FromHtml("#F8696B"))
                          .Midpoint(XLCFContentType.Percentile, 50, XLColor.FromHtml("#FFEB84"))
                          .HighestValue(XLColor.FromHtml("#63BE7B"));

            ws.Range(HeaderRow, 1, dataLast, cols).SetAutoFilter();
        }

        ws.SheetView.FreezeRows(HeaderRow);

        // Ширину считаем по шапке и данным, а не по длинным заголовку и примечанию в строках 1–2
        ws.Columns(1, cols).AdjustToContents(HeaderRow, Math.Max(lastRow, HeaderRow), 8, 60);
    }

    private static void SetCell(IXLCell cell, object? value, Fmt format)
    {
        switch (value)
        {
            case null:                                   break;
            case string s:   cell.Value = s;             break;
            case DateTime d: cell.Value = d;             break;
            case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); break;
            default:         cell.Value = Convert.ToDouble(value);         break;
        }
        if (format == Fmt.Text)
            cell.Style.Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Top);
    }

    private static string? NumberFormat(Fmt f) => f switch
    {
        Fmt.Int     => "#,##0",
        Fmt.Money   => "$#,##0",
        Fmt.Percent => "0.0%",
        Fmt.Delta   => "+0.0%;-0.0%;0.0%",
        Fmt.Num1    => "0.0",
        Fmt.Date    => "dd.mm.yyyy",
        Fmt.Month   => "mmmm yyyy",
        _           => null
    };
}
