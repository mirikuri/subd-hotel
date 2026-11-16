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

    // Сообщения DbErrors привязаны к именам ограничений. Если ограничение переименовали или убрали
    // в schema.sql, словарь молча перестаёт работать — этот тест ловит расхождение.
    [Fact]
    public async Task DbErrors_KnowOnlyExistingConstraints()
    {
        var existing = new HashSet<string>();
        await using var conn = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT conname::text FROM pg_constraint WHERE connamespace = 'public'::regnamespace
            UNION
            SELECT indexname::text FROM pg_indexes WHERE schemaname = 'public'
            """, conn);
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) existing.Add(r.GetString(0));

        Assert.Empty(DbErrors.KnownConstraints.Where(n => !existing.Contains(n)));
    }

    // Выполняет SQL в транзакции, которая откатывается, и возвращает имя нарушенного ограничения
    private async Task<string?> ViolatedConstraintAsync(string sql)
    {
        await using var conn = await _db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        return ex.ConstraintName;
    }

    [Fact]
    public async Task BookingRoom_FromAnotherHotel_IsRejected()
    {
        // номер другого отеля под видом «своего» — не сходится с отелем номера
        Assert.Equal("booking_rooms_room_fkey", await ViolatedConstraintAsync("""
            INSERT INTO booking_rooms (booking_id, room_id, hotel_id, date_from, date_to, is_active)
            SELECT b.id, r.id, b.hotel_id, DATE '2090-01-01', DATE '2090-01-02', false
            FROM bookings b JOIN rooms r ON r.hotel_id <> b.hotel_id
            LIMIT 1
            """));
        // честный отель номера — не сходится с отелем брони
        Assert.Equal("booking_rooms_booking_fkey", await ViolatedConstraintAsync("""
            INSERT INTO booking_rooms (booking_id, room_id, hotel_id, date_from, date_to, is_active)
            SELECT b.id, r.id, r.hotel_id, DATE '2090-01-01', DATE '2090-01-02', false
            FROM bookings b JOIN rooms r ON r.hotel_id <> b.hotel_id
            LIMIT 1
            """));
    }

    [Fact]
    public async Task Room_WithBookings_CannotMoveToAnotherHotel()
    {
        Assert.Equal("booking_rooms_room_fkey", await ViolatedConstraintAsync("""
            UPDATE rooms r SET hotel_id = (SELECT min(id) FROM hotels WHERE id <> r.hotel_id),
                               room_number = 'перенос'
            WHERE r.id = (SELECT room_id FROM booking_rooms LIMIT 1)
            """));
    }

    [Fact]
    public async Task CancelReason_MatchesStatus()
    {
        Assert.Equal("bookings_cancel_reason_check", await ViolatedConstraintAsync(
            "UPDATE bookings SET status = 'отменена' WHERE id = (SELECT id FROM bookings WHERE status = 'новая' LIMIT 1)"));
        Assert.Equal("bookings_cancel_reason_check", await ViolatedConstraintAsync(
            "UPDATE bookings SET cancel_reason = 'x' WHERE id = (SELECT id FROM bookings WHERE status = 'новая' LIMIT 1)"));
    }

    [Fact]
    public async Task Stay_SecondOpenForSameBookingRoom_IsRejected()
    {
        Assert.Equal("stays_one_open_key", await ViolatedConstraintAsync("""
            INSERT INTO stays (booking_room_id, employee_id)
            SELECT booking_room_id, employee_id FROM stays WHERE actual_check_out IS NULL LIMIT 1
            """));
    }

    [Fact]
    public async Task Position_UnknownRole_IsRejected()
    {
        Assert.Equal("positions_role_check", await ViolatedConstraintAsync(
            "INSERT INTO positions (name, role) VALUES ('тест-роль', 'директор')"));
    }

    [Fact]
    public async Task Guest_LoginWithoutPassword_IsRejected()
    {
        Assert.Equal("guests_account_check", await ViolatedConstraintAsync(
            "INSERT INTO guests (full_name, document_number, login) VALUES ('Тест', 'T-acc-check', 't_acc_check')"));
    }

    [Fact]
    public async Task ForeignKeyColumns_AreIndexed()
    {
        // Внешние ключи, у которых нет индекса, начинающегося с тех же столбцов
        var missing = await ScalarAsync("""
            SELECT string_agg(c.conrelid::regclass || '.' || c.conname, ', ')
            FROM pg_constraint c
            WHERE c.contype = 'f' AND c.connamespace = 'public'::regnamespace
              AND NOT EXISTS (
                  SELECT 1 FROM pg_index i
                  WHERE i.indrelid = c.conrelid
                    AND (i.indkey::smallint[])[0:cardinality(c.conkey) - 1] @> c.conkey
              )
            """);
        Assert.Equal(DBNull.Value, missing);
    }
}
