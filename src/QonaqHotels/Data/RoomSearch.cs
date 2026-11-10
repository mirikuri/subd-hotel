using Npgsql;
using NpgsqlTypes;
using QonaqHotels.Models;

namespace QonaqHotels.Data;

// Предложение для гостя: тип номера в отеле, свободный на весь период, с ценой за период (USD)
public record RoomOffer(
    int HotelId, string HotelName, short Stars, string Address,
    int RoomTypeId, string RoomTypeName, short Capacity,
    decimal? AreaM2, string? BedType, string? Description,
    IReadOnlyList<(string Name, string? Icon)> Amenities,
    int FreeRooms, int Nights, decimal TotalPrice)
{
    public decimal AvgPerNight => Math.Round(TotalPrice / Nights, 2);
}

// Единственное место, где описано «номер свободен» и «сколько стоит период».
// Им пользуются и сайт (поиск, заявки), и админка (BookingQueries).
public static class RoomSearch
{
    public const int MaxNights = 30;
    public const int MaxGuests = 6;

    // Номер r свободен на [@dateFrom, @dateTo): не на ремонте и нет АКТИВНОЙ строки booking_rooms
    // с пересекающимся периодом. Ровно условие ограничения booking_rooms_room_id_daterange_excl:
    //     EXCLUDE USING gist (room_id WITH =, daterange(date_from, date_to, '[)') WITH &&) WHERE (is_active)
    // поэтому «свободен» здесь = «БД позволит вставить». @excludeBookingId — бронь, которую правят (0 — никакая).
    public const string FreeRoomCondition = """
        r.status <> @repair
        AND NOT EXISTS (
            SELECT 1
            FROM booking_rooms br
            WHERE br.room_id = r.id
              AND br.is_active
              AND br.booking_id <> @excludeBookingId
              AND daterange(br.date_from, br.date_to, '[)') && daterange(@dateFrom, @dateTo, '[)')
        )
        """;

    // Цена каждого типа номера за период: сумма тарифов по ночам d ∈ [@dateFrom, @dateTo).
    // nights_priced < числа ночей — на какую-то ночь нет тарифа, такой тип не продаём.
    private const string PriceCte = """
        price AS (
            SELECT ra.room_type_id, sum(ra.price_per_night) AS total, count(*) AS nights_priced
            FROM generate_series(@dateFrom::date, @dateTo::date - 1, interval '1 day') AS g(d)
            JOIN seasons s ON g.d::date BETWEEN s.date_start AND s.date_end
            JOIN rates ra  ON ra.season_id = s.id
            GROUP BY ra.room_type_id
        )
        """;

    private static readonly string OffersSql = $"""
        WITH free AS (
            SELECT r.hotel_id, r.room_type_id, count(*)::int AS free_rooms
            FROM rooms r
            JOIN hotels h      ON h.id = r.hotel_id
            JOIN room_types rt ON rt.id = r.room_type_id
            WHERE (@cityId::int IS NULL OR h.city_id = @cityId)
              AND (@hotelId::int IS NULL OR r.hotel_id = @hotelId)
              AND (@roomTypeId::int IS NULL OR r.room_type_id = @roomTypeId)
              AND rt.capacity >= @guests
              AND {FreeRoomCondition}
            GROUP BY r.hotel_id, r.room_type_id
        ),
        {PriceCte}
        SELECT h.id, h.name, h.stars, h.address,
               rt.id, rt.name, rt.capacity, rt.area_m2, rt.bed_type, rt.description,
               coalesce(am.names, ARRAY[]::text[]), coalesce(am.icons, ARRAY[]::text[]),
               f.free_rooms, p.total
        FROM free f
        JOIN hotels h      ON h.id = f.hotel_id
        JOIN room_types rt ON rt.id = f.room_type_id
        JOIN price p       ON p.room_type_id = f.room_type_id AND p.nights_priced = @nights
        LEFT JOIN LATERAL (
            SELECT array_agg(a.name ORDER BY a.name) AS names,
                   array_agg(coalesce(a.icon, '') ORDER BY a.name) AS icons
            FROM room_type_amenities rta
            JOIN amenities a ON a.id = rta.amenity_id
            WHERE rta.room_type_id = rt.id
        ) am ON true
        ORDER BY h.name, p.total
        """;

    // null — всё верно, иначе текст ошибки для формы
    public static string? Validate(DateOnly from, DateOnly to, int guests, DateOnly today)
    {
        if (to <= from)                    return "Дата выезда должна быть позже даты заезда.";
        if (from < today)                  return "Нельзя бронировать прошедшие даты.";
        if (to > today.AddYears(1))        return "Бронирование возможно максимум за год вперёд.";
        if (to.DayNumber - from.DayNumber > MaxNights) return "Не больше 30 ночей.";
        if (guests < 1 || guests > MaxGuests) return "Гостей — от 1 до 6.";
        return null;
    }

    public static Task<List<RoomOffer>> SearchAsync(
        NpgsqlConnection conn, int cityId, DateOnly from, DateOnly to, int guests) =>
        LoadOffersAsync(conn, cityId, null, null, from, to, guests);

    public static async Task<RoomOffer?> FindOfferAsync(
        NpgsqlConnection conn, int hotelId, int roomTypeId, DateOnly from, DateOnly to, int guests) =>
        (await LoadOffersAsync(conn, null, hotelId, roomTypeId, from, to, guests)).FirstOrDefault();

    private static async Task<List<RoomOffer>> LoadOffersAsync(
        NpgsqlConnection conn, int? cityId, int? hotelId, int? roomTypeId,
        DateOnly from, DateOnly to, int guests)
    {
        var nights = to.DayNumber - from.DayNumber;
        var list = new List<RoomOffer>();
        if (nights <= 0) return list;

        await using var cmd = new NpgsqlCommand(OffersSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("cityId",     NpgsqlDbType.Integer) { Value = (object?)cityId     ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("hotelId",    NpgsqlDbType.Integer) { Value = (object?)hotelId    ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("roomTypeId", NpgsqlDbType.Integer) { Value = (object?)roomTypeId ?? DBNull.Value });
        AddPeriod(cmd, from, to);
        cmd.Parameters.AddWithValue("guests", guests);
        cmd.Parameters.AddWithValue("nights", (long)nights);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var names = r.GetFieldValue<string[]>(10);
            var icons = r.GetFieldValue<string[]>(11);
            list.Add(new RoomOffer(
                HotelId:      r.GetInt32(0),
                HotelName:    r.GetString(1),
                Stars:        r.GetInt16(2),
                Address:      r.GetString(3),
                RoomTypeId:   r.GetInt32(4),
                RoomTypeName: r.GetString(5),
                Capacity:     r.GetInt16(6),
                AreaM2:       r.IsDBNull(7) ? null : r.GetDecimal(7),
                BedType:      r.IsDBNull(8) ? null : r.GetString(8),
                Description:  r.IsDBNull(9) ? null : r.GetString(9),
                Amenities:    names.Select((n, i) => (n, icons[i] == "" ? null : (string?)icons[i])).ToList(),
                FreeRooms:    r.GetInt32(12),
                Nights:       nights,
                TotalPrice:   r.GetDecimal(13)));
        }
        return list;
    }

    // Сумма тарифов за период; null — хотя бы на одну ночь тарифа нет
    public static async Task<decimal?> PeriodPriceAsync(
        NpgsqlConnection conn, int roomTypeId, DateOnly from, DateOnly to)
    {
        var nights = to.DayNumber - from.DayNumber;
        if (nights <= 0) return null;

        await using var cmd = new NpgsqlCommand($"""
            WITH {PriceCte}
            SELECT total FROM price WHERE room_type_id = @roomTypeId AND nights_priced = @nights
            """, conn);
        AddPeriod(cmd, from, to);
        cmd.Parameters.AddWithValue("roomTypeId", roomTypeId);
        cmd.Parameters.AddWithValue("nights", (long)nights);
        return await cmd.ExecuteScalarAsync() is decimal total ? total : null;
    }

    // Первый свободный номер типа в отеле — тот, который получит заявка с сайта
    public static async Task<int?> FirstFreeRoomAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, int hotelId, int roomTypeId, DateOnly from, DateOnly to)
    {
        await using var cmd = new NpgsqlCommand($"""
            SELECT r.id
            FROM rooms r
            WHERE r.hotel_id = @hotelId AND r.room_type_id = @roomTypeId
              AND {FreeRoomCondition}
            ORDER BY r.floor NULLS LAST, r.room_number
            LIMIT 1
            """, conn, tx);
        cmd.Parameters.AddWithValue("hotelId",    hotelId);
        cmd.Parameters.AddWithValue("roomTypeId", roomTypeId);
        AddPeriod(cmd, from, to);
        return await cmd.ExecuteScalarAsync() is int id ? id : null;
    }

    // «от $N за ночь»: минимальный тариф типа среди сезонов ближайшего года
    public static async Task<Dictionary<int, decimal>> MinNightlyRatesAsync(NpgsqlConnection conn, DateOnly today)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT ra.room_type_id, min(ra.price_per_night)
            FROM rates ra
            JOIN seasons s ON s.id = ra.season_id
            WHERE daterange(s.date_start, s.date_end, '[]') && daterange(@today, @yearLater, '[]')
            GROUP BY ra.room_type_id
            """, conn);
        cmd.Parameters.AddWithValue("today",     today);
        cmd.Parameters.AddWithValue("yearLater", today.AddYears(1));

        var map = new Dictionary<int, decimal>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) map[r.GetInt32(0)] = r.GetDecimal(1);
        return map;
    }

    // Параметры FreeRoomCondition и PriceCte
    public static void AddPeriod(NpgsqlCommand cmd, DateOnly from, DateOnly to, int excludeBookingId = 0)
    {
        cmd.Parameters.AddWithValue("dateFrom",         from);
        cmd.Parameters.AddWithValue("dateTo",           to);
        cmd.Parameters.AddWithValue("excludeBookingId", excludeBookingId);
        cmd.Parameters.AddWithValue("repair",           RoomStatus.Repair);
    }
}
