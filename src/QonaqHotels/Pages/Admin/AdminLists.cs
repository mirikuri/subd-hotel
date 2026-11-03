using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;

namespace QonaqHotels.Pages.Admin;

// Справочники для выпадающих списков и фильтров админки.
// Запрос должен возвращать два столбца: id (int) и подпись (text).
public static class AdminLists
{
    public const string Hotels    = "SELECT id, name FROM hotels ORDER BY name";
    public const string Positions = "SELECT id, name FROM positions ORDER BY name";
    public const string Countries = "SELECT id, name FROM countries ORDER BY name";
    public const string Guests    = "SELECT id, full_name FROM guests ORDER BY full_name";

    // Для <select asp-items="...">
    public static async Task<SelectList> SelectListAsync(
        NpgsqlConnection conn, string sql, object? selectedValue = null)
    {
        var items = new List<SelectListItem>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            items.Add(new SelectListItem(r.GetString(1), r.GetInt32(0).ToString()));

        return new SelectList(items, nameof(SelectListItem.Value), nameof(SelectListItem.Text),
            selectedValue?.ToString());
    }

    // Для FilterBarModel (пары «значение — подпись»)
    public static async Task<List<(string Value, string Label)>> OptionsAsync(
        NpgsqlConnection conn, string sql)
    {
        var list = new List<(string, string)>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add((r.GetInt32(0).ToString(), r.GetString(1)));
        return list;
    }
}
