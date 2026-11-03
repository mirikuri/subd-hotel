using Microsoft.Extensions.Configuration;
using QonaqHotels.Data;

namespace QonaqHotels.Tests;

// Тесты вставляют и удаляют строки, поэтому работают с отдельной базой, а не с рабочей gosti.
// Создать или пересоздать её: db/reset.sh gosti_test
// Другая база: переменная окружения QONAQ_TEST_DB="Host=...;Database=...;Username=...;Password=..."
internal static class TestDb
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("QONAQ_TEST_DB")
        ?? "Host=localhost;Database=gosti_test;Username=postgres;Password=";

    public static Db Create() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString
            })
            .Build());
}
