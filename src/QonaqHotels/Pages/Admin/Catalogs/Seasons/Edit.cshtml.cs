using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages.Admin.Catalogs.Seasons;

public class RatePrice
{
    public int RoomTypeId { get; set; }
    public string RoomTypeName { get; set; } = "";
    public decimal? Price { get; set; }
}

// Сезон и его тарифы. Цены меняют бухгалтер и админ; название, даты и новый сезон — только админ.
// Пересечение сезонов запрещает EXCLUDE seasons_no_overlap.
[Authorize(Policy = "RequireAccountant")]
public class EditModel : PageModel
{
    private readonly Db _db;
    public EditModel(Db db) => _db = db;

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public DateOnly DateStart { get; set; } = Clock.Today;
    [BindProperty] public DateOnly DateEnd { get; set; } = Clock.Today.AddMonths(3);
    [BindProperty] public List<RatePrice> Prices { get; set; } = [];

    public bool IsAdmin => User.IsInRole(Roles.Admin);

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is null && !IsAdmin) return Forbid();
        await using var conn = await _db.OpenAsync();
        if (Id is int id)
        {
            await using var cmd = CatalogDb.Cmd(conn, "SELECT name, date_start, date_end FROM seasons WHERE id = @id", null, ("id", id));
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            Name = r.GetString(0); DateStart = r.GetFieldValue<DateOnly>(1); DateEnd = r.GetFieldValue<DateOnly>(2);
        }
        await LoadPricesAsync(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Id is null && !IsAdmin) return Forbid();
        if (IsAdmin && string.IsNullOrWhiteSpace(Name)) ModelState.AddModelError(nameof(Name), "Укажите название.");
        if (Prices.Any(p => p.Price <= 0)) ModelState.AddModelError("", "Цена должна быть больше нуля.");

        await using var conn = await _db.OpenAsync();
        if (ModelState.IsValid)
        {
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                int seasonId;
                if (IsAdmin)
                {
                    await using var save = CatalogDb.Cmd(conn, Id is null
                        ? "INSERT INTO seasons (name, date_start, date_end) VALUES (@n, @s, @e) RETURNING id"
                        : "UPDATE seasons SET name = @n, date_start = @s, date_end = @e WHERE id = @id RETURNING id",
                        tx, ("id", Id), ("n", Name.Trim()), ("s", DateStart), ("e", DateEnd));
                    if (await save.ExecuteScalarAsync() is not int sid) return NotFound();
                    seasonId = sid;
                }
                else seasonId = Id!.Value;   // бухгалтер: название и даты не трогаем

                // Пустая цена — тарифа нет; иначе вставка или обновление
                foreach (var p in Prices)
                    await CatalogDb.ExecAsync(conn, p.Price is null
                        ? "DELETE FROM rates WHERE season_id = @s AND room_type_id = @t"
                        : """
                          INSERT INTO rates (room_type_id, season_id, price_per_night) VALUES (@t, @s, @p)
                          ON CONFLICT (room_type_id, season_id) DO UPDATE SET price_per_night = EXCLUDED.price_per_night
                          """, tx, ("s", seasonId), ("t", p.RoomTypeId), ("p", p.Price));

                await tx.CommitAsync();
                return RedirectToPage("Index");
            }
            catch (PostgresException ex) when (DbErrors.IsConstraintViolation(ex))
            {
                await tx.RollbackAsync();
                ModelState.AddModelError("", DbErrors.Describe(ex));
            }
        }
        // Повторный показ: подписи типов из БД, введённые цены — из формы
        var typed = Prices.ToDictionary(p => p.RoomTypeId, p => p.Price);
        await LoadPricesAsync(conn);
        foreach (var p in Prices) p.Price = typed.GetValueOrDefault(p.RoomTypeId);
        if (!IsAdmin && Id is int sid2) await ReloadHeaderAsync(conn, sid2);
        return Page();
    }

    private async Task LoadPricesAsync(NpgsqlConnection conn)
    {
        await using var cmd = CatalogDb.Cmd(conn, """
            SELECT rt.id, rt.name, r.price_per_night
            FROM room_types rt LEFT JOIN rates r ON r.room_type_id = rt.id AND r.season_id = @s
            ORDER BY rt.name
            """, null, ("s", Id ?? 0));
        await using var r = await cmd.ExecuteReaderAsync();
        Prices = [];
        while (await r.ReadAsync())
            Prices.Add(new RatePrice { RoomTypeId = r.GetInt32(0), RoomTypeName = r.GetString(1), Price = r.IsDBNull(2) ? null : r.GetDecimal(2) });
    }

    private async Task ReloadHeaderAsync(NpgsqlConnection conn, int id)
    {
        await using var cmd = CatalogDb.Cmd(conn, "SELECT name, date_start, date_end FROM seasons WHERE id = @id", null, ("id", id));
        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync()) { Name = r.GetString(0); DateStart = r.GetFieldValue<DateOnly>(1); DateEnd = r.GetFieldValue<DateOnly>(2); }
    }
}
