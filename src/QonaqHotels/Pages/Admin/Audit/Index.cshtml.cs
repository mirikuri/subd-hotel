using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using NpgsqlTypes;
using QonaqHotels.Data;

namespace QonaqHotels.Pages.Admin.Audit;

public record AuditRow(long Id, DateTime At, string? User, string Action, string Table, int? RecordId,
                       string? OldData, string? NewData);

// Журнал изменений: читает audit_log, который заполняет триггер audit_row (db/schema.sql)
[Authorize(Policy = "RequireAdmin")]
public class IndexModel : PageModel
{
    private const int PageSize = 200;

    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Table { get; set; }
    [BindProperty(SupportsGet = true)] public string? Action { get; set; }
    [BindProperty(SupportsGet = true)] public int? RecordId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Day { get; set; }

    public List<AuditRow> Rows { get; private set; } = [];
    public List<string> Tables { get; private set; } = [];

    public static readonly Dictionary<string, string> ActionTitles = new()
    {
        ["INSERT"] = "создание", ["UPDATE"] = "изменение", ["DELETE"] = "удаление"
    };

    public async Task OnGetAsync()
    {
        await using var conn = await _db.OpenAsync();

        // Таблицы, на которых висит триггер журнала
        await using (var t = new NpgsqlCommand("""
            SELECT DISTINCT event_object_table::text FROM information_schema.triggers
            WHERE trigger_name = 'audit' AND trigger_schema = 'public' ORDER BY 1
            """, conn))
        await using (var r = await t.ExecuteReaderAsync())
            while (await r.ReadAsync()) Tables.Add(r.GetString(0));

        await using var cmd = new NpgsqlCommand($"""
            SELECT a.id, a.changed_at, coalesce(e.full_name || ' (' || u.login || ')', NULL),
                   a.action, a.table_name, a.record_id, a.old_data::text, a.new_data::text
            FROM audit_log a
            LEFT JOIN users u     ON u.id = a.user_id
            LEFT JOIN employees e ON e.id = u.employee_id
            WHERE (@table::text  IS NULL OR a.table_name = @table)
              AND (@action::text IS NULL OR a.action = @action)
              AND (@rec::int     IS NULL OR a.record_id = @rec)
              AND (@day::date    IS NULL OR a.changed_at >= @day::date AND a.changed_at < @day::date + 1)
            ORDER BY a.changed_at DESC, a.id DESC
            LIMIT {PageSize}
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter("table",  NpgsqlDbType.Text)    { Value = Nullable(Table) });
        cmd.Parameters.Add(new NpgsqlParameter("action", NpgsqlDbType.Text)    { Value = Nullable(Action) });
        cmd.Parameters.Add(new NpgsqlParameter("rec",    NpgsqlDbType.Integer) { Value = (object?)RecordId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("day",    NpgsqlDbType.Date)    { Value = (object?)Day ?? DBNull.Value });

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            Rows.Add(new AuditRow(rd.GetInt64(0), rd.GetDateTime(1).ToLocalTime(),
                rd.IsDBNull(2) ? null : rd.GetString(2), rd.GetString(3), rd.GetString(4),
                rd.IsDBNull(5) ? null : rd.GetInt32(5),
                rd.IsDBNull(6) ? null : rd.GetString(6), rd.IsDBNull(7) ? null : rd.GetString(7)));
    }

    private static object Nullable(string? s) => string.IsNullOrEmpty(s) ? DBNull.Value : s;
}
