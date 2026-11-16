using System.Security.Claims;
using Npgsql;

namespace QonaqHotels.Data;

// Фабрика соединений с PostgreSQL.
// Npgsql сам держит пул соединений, поэтому Open/OpenAsync — это «взять соединение из пула»,
// а Dispose (using) — «вернуть в пул». Правило для обработчика страницы:
// одно соединение на запрос, все запросы обработчика идут через него,
// несколько связанных изменений — в одной транзакции (conn.BeginTransactionAsync()).
//
// Если запрос выполняет вошедший сотрудник, соединение получает настройку сеанса app.user_id —
// по ней триггер audit_row (db/schema.sql) записывает в audit_log, кто изменил строку.
// При возврате в пул Npgsql сбрасывает состояние сеанса, поэтому id не «протекает» в чужой запрос.
public class Db
{
    private readonly string _cs;
    private readonly IHttpContextAccessor? _http;

    public Db(IConfiguration cfg, IHttpContextAccessor? http = null)
    {
        _cs = cfg.GetConnectionString("DefaultConnection")
              ?? throw new InvalidOperationException("Не задана строка подключения DefaultConnection.");
        _http = http;
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        if (StaffUserId() is { } userId)
        {
            await using var cmd = new NpgsqlCommand("SELECT set_config('app.user_id', @id, false)", conn);
            cmd.Parameters.AddWithValue("id", userId.ToString());
            await cmd.ExecuteNonQueryAsync();
        }
        return conn;
    }

    // Синхронный вариант — для тестов и мест, где async недоступен
    public NpgsqlConnection Open()
    {
        var conn = new NpgsqlConnection(_cs);
        conn.Open();
        if (StaffUserId() is { } userId)
        {
            using var cmd = new NpgsqlCommand("SELECT set_config('app.user_id', @id, false)", conn);
            cmd.Parameters.AddWithValue("id", userId.ToString());
            cmd.ExecuteNonQuery();
        }
        return conn;
    }

    // users.id вошедшего сотрудника. У гостя сайта claim имеет вид "guest:<id>" — это не users.id.
    private int? StaffUserId() =>
        int.TryParse(_http?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
