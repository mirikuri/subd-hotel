using Npgsql;
using QonaqHotels.Data;

namespace QonaqHotels.Tests;

// Проверяют db/schema.sql + db/seed.sql на базе gosti_test (db/reset.sh -y gosti_test)
public class SchemaTests
{
    private readonly Db _db = TestDb.Create();

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync();
    }

    [Fact]
    public async Task Rates_AreUsd()
    {
        var standardAutumn = await ScalarAsync("""
            SELECT r.price_per_night FROM rates r
            JOIN room_types rt ON rt.id = r.room_type_id
            JOIN seasons s ON s.id = r.season_id
            WHERE rt.name = 'Стандарт' AND s.name = 'Осень ' || extract(year FROM CURRENT_DATE)
            """);
        Assert.Equal(50m, standardAutumn);

        var businessSummer = await ScalarAsync("""
            SELECT r.price_per_night FROM rates r
            JOIN room_types rt ON rt.id = r.room_type_id
            JOIN seasons s ON s.id = r.season_id
            WHERE rt.name = 'Бизнес-люкс' AND s.name = 'Лето ' || extract(year FROM CURRENT_DATE)
            """);
        Assert.Equal(200m, businessSummer);
    }

    [Fact]
    public async Task Services_AreUsd()
    {
        var breakfast = await ScalarAsync("""
            SELECT s.price FROM services s JOIN hotels h ON h.id = s.hotel_id
            WHERE h.stars = 4 AND s.name = 'Завтрак «шведский стол»' LIMIT 1
            """);
        Assert.Equal(12m, breakfast);
    }

    [Fact]
    public async Task NewColumns_Seeded()
    {
        Assert.Equal(0L, await ScalarAsync(
            "SELECT count(*) FROM room_types WHERE area_m2 IS NULL OR bed_type IS NULL"));
        Assert.Equal(0L, await ScalarAsync("""
            SELECT count(*) FROM room_types rt
            WHERE (SELECT count(*) FROM room_type_amenities a WHERE a.room_type_id = rt.id) < 3
            """));
        Assert.Equal(0L, await ScalarAsync(
            "SELECT count(*) FROM hotels WHERE check_in_time <> '14:00'"));
    }

    [Fact]
    public async Task SeasonOverlap_HasNamedConstraint()
    {
        await using var conn = await _db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO seasons (name, date_start, date_end) VALUES ('t', CURRENT_DATE, CURRENT_DATE + 3)",
            conn, tx);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("seasons_no_overlap", ex.ConstraintName);
        Assert.Equal("Сезон пересекается с другим сезоном.", DbErrors.Describe(ex));
    }
}
