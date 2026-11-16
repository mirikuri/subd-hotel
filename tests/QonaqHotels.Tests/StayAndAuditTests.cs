using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Services;

namespace QonaqHotels.Tests;

// Заселение/выезд (stays) и журнал изменений (триггер audit_row) на базе gosti_test
public class StayAndAuditTests
{
    private readonly Db _db = TestDb.Create();
    private static readonly DateOnly Today = Clock.Today;

    private async Task<T?> ScalarAsync<T>(string sql, params (string, object)[] ps)
    {
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    private Task<int> AnyUserIdAsync() => ScalarAsync<int>("SELECT min(id) FROM users")!;

    private Task<int> BookingRoomIdAsync(int bookingId) =>
        ScalarAsync<int>("SELECT id FROM booking_rooms WHERE booking_id = @b", ("b", bookingId))!;

    // stays ссылается на booking_rooms с RESTRICT — убираем заселения до удаления тестового отеля
    private async Task CleanupStaysAsync(TestHotel t) =>
        await ScalarAsync<object>("""
            DELETE FROM stays WHERE booking_room_id IN (SELECT id FROM booking_rooms WHERE hotel_id = @h)
            """, ("h", t.HotelId));

    [Fact]
    public async Task CheckIn_ThenCheckOut_MovesBookingAndRoomThroughStatuses()
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var svc = new StayService(_db);
        var bookingId = await t.BookAsync(t.RoomIds[0], Today, Today.AddDays(2), status: "подтверждена");
        var brId = await BookingRoomIdAsync(bookingId);
        try
        {
            Assert.Single(await svc.ArrivalsAsync(t.HotelId, Today));

            Assert.Null(await svc.CheckInAsync(brId, await AnyUserIdAsync(), Today));
            Assert.Equal("заселён", await ScalarAsync<string>("SELECT status FROM bookings WHERE id = @b", ("b", bookingId)));
            Assert.Equal("занят", await ScalarAsync<string>("SELECT status FROM rooms WHERE id = @r", ("r", t.RoomIds[0])));
            Assert.Empty(await svc.ArrivalsAsync(t.HotelId, Today));
            var inHouse = Assert.Single(await svc.InHouseAsync(t.HotelId));

            // второй раз заселить нельзя
            Assert.NotNull(await svc.CheckInAsync(brId, await AnyUserIdAsync(), Today));

            Assert.Null(await svc.CheckOutAsync(inHouse.StayId));
            Assert.Equal("завершена", await ScalarAsync<string>("SELECT status FROM bookings WHERE id = @b", ("b", bookingId)));
            Assert.Single(await svc.CleaningAsync(t.HotelId));
            Assert.NotNull(await svc.CheckOutAsync(inHouse.StayId));     // уже выселен

            Assert.True(await svc.MarkCleanAsync(t.RoomIds[0]));
            Assert.Equal("свободен", await ScalarAsync<string>("SELECT status FROM rooms WHERE id = @r", ("r", t.RoomIds[0])));
            Assert.False(await svc.MarkCleanAsync(t.RoomIds[0]));
        }
        finally { await CleanupStaysAsync(t); }
    }

    [Theory]
    [InlineData(1, "новая")]        // заезд только завтра
    [InlineData(0, "отменена")]     // бронь отменена
    public async Task CheckIn_Refused(int startInDays, string status)
    {
        await using var t = await TestHotel.CreateAsync(_db);
        var svc = new StayService(_db);
        var bookingId = await t.BookAsync(t.RoomIds[0], Today.AddDays(startInDays), Today.AddDays(startInDays + 2), status: status);

        Assert.NotNull(await svc.CheckInAsync(await BookingRoomIdAsync(bookingId), await AnyUserIdAsync(), Today));
        Assert.Empty(await svc.InHouseAsync(t.HotelId));
    }

    [Fact]
    public async Task Audit_RecordsInsertUpdateDelete_WithUserAndChangedColumnsOnly()
    {
        var userId = await AnyUserIdAsync();
        await using var conn = await _db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        async Task<object?> Run(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("u", userId.ToString());
            return await cmd.ExecuteScalarAsync();
        }

        await Run("SELECT set_config('app.user_id', @u, true)");
        var id = (int)(await Run("INSERT INTO positions (name, role) VALUES ('аудит-тест', 'ресепшен') RETURNING id"))!;
        await Run($"UPDATE positions SET base_salary = 500 WHERE id = {id}");
        await Run($"UPDATE positions SET base_salary = 500 WHERE id = {id}");   // ничего не изменилось — не пишется
        await Run($"DELETE FROM positions WHERE id = {id}");

        var rows = new List<string>();
        await using (var cmd = new NpgsqlCommand($"""
            SELECT action || ':' || user_id, coalesce(old_data::text, '-'), coalesce(new_data::text, '-')
            FROM audit_log WHERE table_name = 'positions' AND record_id = {id} ORDER BY id
            """, conn, tx))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) rows.Add($"{r.GetString(0)} {r.GetString(1)} {r.GetString(2)}");
        await tx.RollbackAsync();

        Assert.Equal(3, rows.Count);
        Assert.StartsWith($"INSERT:{userId} - {{", rows[0]);
        Assert.Equal($"UPDATE:{userId} {{\"base_salary\": null}} {{\"base_salary\": 500.00}}", rows[1]);
        Assert.StartsWith($"DELETE:{userId} {{", rows[2]);
        Assert.EndsWith(" -", rows[2]);
    }

    [Fact]
    public async Task Audit_SkipsPasswordsAndLoginCounters()
    {
        await using var conn = await _db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        async Task<object?> Run(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            return await cmd.ExecuteScalarAsync();
        }

        // считаем только свои строки: другие тесты пишут в журнал параллельно
        const string mine = "SELECT count(*) FROM audit_log WHERE table_name = 'users' AND record_id = (SELECT min(id) FROM users)";
        var before = (long)(await Run(mine))!;
        await Run("UPDATE users SET failed_attempts = failed_attempts + 1, last_login_at = now(), password_hash = 'x' WHERE id = (SELECT min(id) FROM users)");
        Assert.Equal(before, (long)(await Run(mine))!);

        await Run("UPDATE users SET is_active = NOT is_active, password_hash = 'y' WHERE id = (SELECT min(id) FROM users)");
        Assert.Equal(before + 1, (long)(await Run(mine))!);
        Assert.Equal(0L, (long)(await Run("SELECT count(*) FROM audit_log WHERE old_data ? 'password_hash' OR new_data ? 'password_hash'"))!);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Seed_BookingStatusesAgreeWithStays()
    {
        // «заселён» ⇔ у брони есть открытое заселение
        Assert.Equal(0L, await ScalarAsync<long>("""
            SELECT count(*) FROM bookings b
            WHERE (b.status = 'заселён') <> EXISTS (
                SELECT 1 FROM stays s JOIN booking_rooms br ON br.id = s.booking_room_id
                WHERE br.booking_id = b.id AND s.actual_check_out IS NULL)
            """));
    }
}
