using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;

namespace QonaqHotels.Pages.Admin.Catalogs;

// Общие мелочи справочников: команда с параметрами (null → NULL), шаблон для LIKE, списки для <select>
public static class CatalogDb
{
    public static NpgsqlCommand Cmd(NpgsqlConnection conn, string sql, NpgsqlTransaction? tx = null,
                                    params (string Name, object? Value)[] ps)
    {
        var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }

    public static async Task<int> ExecAsync(NpgsqlConnection conn, string sql, NpgsqlTransaction? tx = null,
                                            params (string, object?)[] ps)
    {
        await using var cmd = Cmd(conn, sql, tx, ps);
        return await cmd.ExecuteNonQueryAsync();
    }

    // Поиск подстроки: экранируем % и _, чтобы они искались буквально
    public static string? Like(string? q) =>
        string.IsNullOrWhiteSpace(q) ? null
        : "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    // id + подпись для <select>; запрос возвращает (int, text)
    public static async Task<List<SelectListItem>> OptionsAsync(NpgsqlConnection conn, string sql)
    {
        var list = new List<SelectListItem>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new SelectListItem(r.GetString(1), r.GetInt32(0).ToString()));
        return list;
    }

    public const string HotelsSql     = "SELECT id, name FROM hotels ORDER BY name";
    public const string RoomTypesSql  = "SELECT id, name FROM room_types ORDER BY name";
    public const string CategoriesSql = "SELECT id, name FROM service_categories ORDER BY name";
    public const string CitiesSql     = """
        SELECT c.id, co.name || ' — ' || c.name FROM cities c
        JOIN countries co ON co.id = c.country_id ORDER BY co.name, c.name
        """;
}
