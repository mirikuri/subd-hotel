using Npgsql;
using QonaqHotels.Data;

namespace QonaqHotels.Tests;

// Изолированные данные для теста: свой город (чтобы поиск по городу видел только их),
// свой отель с номерами одного типа и свой гость. DisposeAsync удаляет всё созданное.
public sealed class TestHotel : IAsyncDisposable
{
    private readonly Db _db;
    private readonly List<int> _extraGuests = [];

    public int CityId { get; private set; }
    public int HotelId { get; private set; }
    public int RoomTypeId { get; private set; }
    public IReadOnlyList<int> RoomIds { get; private set; } = [];
    public int GuestId { get; private set; }
    public string Tag { get; }

    private TestHotel(Db db)
    {
        _db = db;
        Tag = "t" + Guid.NewGuid().ToString("N")[..10];
    }

    public static async Task<TestHotel> CreateAsync(Db db, int rooms = 1, string roomType = "Люкс")
    {
        var t = new TestHotel(db);
        await using var conn = await db.OpenAsync();

        t.CityId = await ScalarIntAsync(conn,
            "INSERT INTO cities (country_id, name) SELECT min(id), @n FROM countries RETURNING id",
            ("n", t.Tag));
        t.HotelId = await ScalarIntAsync(conn,
            "INSERT INTO hotels (name, city_id, address, stars) VALUES (@n, @c, 'тестовый адрес', 4) RETURNING id",
            ("n", "Отель " + t.Tag), ("c", t.CityId));
        t.RoomTypeId = await ScalarIntAsync(conn,
            "SELECT id FROM room_types WHERE name = @n", ("n", roomType));

        var ids = new List<int>();
        for (var i = 1; i <= rooms; i++)
            ids.Add(await ScalarIntAsync(conn,
                "INSERT INTO rooms (hotel_id, room_type_id, room_number, floor) VALUES (@h, @rt, @num, 2) RETURNING id",
                ("h", t.HotelId), ("rt", t.RoomTypeId), ("num", (200 + i).ToString())));
        t.RoomIds = ids;

        t.GuestId = await t.AddGuestAsync();
        return t;
    }

    // Ещё один гость (для тестов «чужая бронь», параллельные заявки)
    public async Task<int> AddGuestAsync()
    {
        await using var conn = await _db.OpenAsync();
        var n = _extraGuests.Count;
        var id = await ScalarIntAsync(conn, """
            INSERT INTO guests (full_name, document_number, login, birth_date)
            VALUES (@n, @d, @l, '1990-01-01') RETURNING id
            """, ("n", "Гость " + Tag), ("d", Tag + "_" + n), ("l", Tag + "_" + n));
        _extraGuests.Add(id);
        return id;
    }

    public async Task<int> BookAsync(int roomId, DateOnly from, DateOnly to,
                                     string status = "новая", string source = "стойка", int? guestId = null)
    {
        await using var conn = await _db.OpenAsync();
        var bookingId = await ScalarIntAsync(conn, """
            INSERT INTO bookings (guest_id, hotel_id, check_in_plan, check_out_plan, status, source, cancel_reason)
            VALUES (@g, @h, @f, @t, @s, @src, CASE WHEN @s = 'отменена' THEN 'тест' END) RETURNING id
            """, ("g", guestId ?? GuestId), ("h", HotelId), ("f", from), ("t", to), ("s", status), ("src", source));
        await ExecAsync(conn, """
            INSERT INTO booking_rooms (booking_id, room_id, date_from, date_to, is_active)
            VALUES (@b, @r, @f, @t, @s <> 'отменена')
            """, ("b", bookingId), ("r", roomId), ("f", from), ("t", to), ("s", status));
        return bookingId;
    }

    public async Task SetGuestActiveAsync(bool active)
    {
        await using var conn = await _db.OpenAsync();
        await ExecAsync(conn, "UPDATE guests SET is_active = @a WHERE id = @id", ("a", active), ("id", GuestId));
    }

    public async Task SetRoomStatusAsync(int roomId, string status)
    {
        await using var conn = await _db.OpenAsync();
        await ExecAsync(conn, "UPDATE rooms SET status = @s WHERE id = @id", ("s", status), ("id", roomId));
    }

    public async ValueTask DisposeAsync()
    {
        await using var conn = await _db.OpenAsync();
        await ExecAsync(conn, """
            DELETE FROM booking_rooms WHERE booking_id IN (SELECT id FROM bookings WHERE hotel_id = @h);
            DELETE FROM bookings WHERE hotel_id = @h;
            DELETE FROM rooms WHERE hotel_id = @h;
            DELETE FROM hotels WHERE id = @h;
            DELETE FROM guests WHERE id = ANY(@g);
            DELETE FROM cities WHERE id = @c;
            """, ("h", HotelId), ("g", _extraGuests.ToArray()), ("c", CityId));
    }

    private static async Task<int> ScalarIntAsync(NpgsqlConnection conn, string sql, params (string, object)[] ps)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql, params (string, object)[] ps)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }
}
