using Npgsql;

namespace QonaqHotels.Data;

// Фабрика соединений с PostgreSQL.
// Npgsql сам держит пул соединений, поэтому Open/OpenAsync — это «взять соединение из пула»,
// а Dispose (using) — «вернуть в пул». Правило для обработчика страницы:
// одно соединение на запрос, все запросы обработчика идут через него,
// несколько связанных изменений — в одной транзакции (conn.BeginTransactionAsync()).
public class Db
{
    private readonly string _cs;

    public Db(IConfiguration cfg)
    {
        _cs = cfg.GetConnectionString("DefaultConnection")
              ?? throw new InvalidOperationException("Не задана строка подключения DefaultConnection.");
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        return conn;
    }

    // Синхронный вариант — для тестов и мест, где async недоступен
    public NpgsqlConnection Open()
    {
        var conn = new NpgsqlConnection(_cs);
        conn.Open();
        return conn;
    }
}
