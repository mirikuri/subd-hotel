using Npgsql;

namespace QonaqHotels.Data;

// Перевод ошибок PostgreSQL в сообщения для пользователя.
//
// Целостность данных проверяет сама БД: UNIQUE, FOREIGN KEY, CHECK, EXCLUDE из db/schema.sql.
// Код не дублирует эти проверки запросами «SELECT COUNT(*) ... перед INSERT»
// (между проверкой и записью другой запрос может успеть вставить то же самое),
// а выполняет запись и ловит нарушение ограничения, показывая понятный текст вместо страницы 500.
public static class DbErrors
{
    // SQLSTATE класса 23 — «нарушение ограничения целостности»
    // https://www.postgresql.org/docs/current/errcodes-appendix.html
    public const string NotNullViolation    = "23502";
    public const string ForeignKeyViolation = "23503";
    public const string UniqueViolation     = "23505";
    public const string CheckViolation      = "23514";
    public const string ExclusionViolation  = "23P01";

    // Нарушение ограничения — это ошибка данных, а не сбой: показываем её в форме.
    // Остальные ошибки (нет соединения, синтаксис SQL) пусть падают как обычно.
    public static bool IsConstraintViolation(PostgresException ex) =>
        ex.SqlState.StartsWith("23", StringComparison.Ordinal);

    // Имена ограничений PostgreSQL генерирует по шаблону <таблица>_<столбец>_key / _fkey / _check / _excl.
    // Сверять с db/schema.sql: \d имя_таблицы в psql показывает имена.
    private static readonly Dictionary<string, string> ByConstraint = new()
    {
        // UNIQUE
        ["guests_document_number_key"] = "Гость с таким номером документа уже есть.",
        ["guests_login_key"]           = "Этот логин уже занят.",
        ["users_login_key"]            = "Такой логин уже занят.",
        ["users_login_lower_key"]      = "Такой логин уже занят.",
        ["guests_login_lower_key"]     = "Этот логин уже занят.",
        ["users_employee_id_key"]      = "У этого сотрудника уже есть учётная запись.",
        ["positions_name_key"]         = "Должность с таким названием уже есть.",

        // EXCLUDE: пересечение периодов
        ["booking_rooms_room_id_daterange_excl"] = "Номер уже занят на выбранные даты.",

        // CHECK
        ["bookings_check"]        = "Дата выезда должна быть позже даты заезда.",
        ["booking_rooms_check"]   = "Дата выезда должна быть позже даты заезда.",
        ["bookings_status_check"] = "Недопустимый статус брони.",
        ["bookings_source_check"] = "Недопустимый источник брони.",
        ["employees_check"]       = "Дата увольнения не может быть раньше даты приёма.",
        ["users_role_check"]      = "Недопустимая роль: разрешены админ, ресепшен, бухгалтер.",

        // FOREIGN KEY при INSERT/UPDATE: указана несуществующая запись
        ["bookings_guest_id_fkey"]      = "Гость не найден.",
        ["bookings_hotel_id_fkey"]      = "Отель не найден.",
        ["booking_rooms_room_id_fkey"]  = "Номер не найден.",
        ["employees_hotel_id_fkey"]     = "Отель не найден.",
        ["employees_position_id_fkey"]  = "Должность не найдена.",
        ["users_employee_id_fkey"]      = "Сотрудник не найден.",
        ["guests_country_id_fkey"]      = "Страна не найдена.",
    };

    // Как назвать таблицу в сообщении «на запись ссылаются …»
    private static readonly Dictionary<string, string> TableTitles = new()
    {
        ["bookings"]       = "брони",
        ["booking_rooms"]  = "брони номеров",
        ["stays"]          = "заселения",
        ["employees"]      = "сотрудники",
        ["users"]          = "учётные записи",
        ["service_orders"] = "заказы услуг",
        ["services"]       = "услуги",
        ["loyalty_cards"]  = "карты лояльности",
        ["shifts"]         = "смены",
        ["rooms"]          = "номера",
        ["hotels"]         = "отели",
        ["cities"]         = "города",
        ["guests"]         = "гости",
        ["rates"]          = "тарифы",
    };

    // Сообщение для ошибки при INSERT/UPDATE
    public static string Describe(PostgresException ex)
    {
        if (ex.ConstraintName is { } name && ByConstraint.TryGetValue(name, out var text))
            return text;

        return ex.SqlState switch
        {
            UniqueViolation     => "Такая запись уже существует.",
            ForeignKeyViolation => "Связанная запись не найдена — обновите страницу и попробуйте снова.",
            CheckViolation      => "Значение не прошло проверку в базе данных.",
            ExclusionViolation  => "Период пересекается с уже существующей записью.",
            NotNullViolation    => "Не заполнено обязательное поле.",
            _                   => "Не удалось сохранить данные."
        };
    }

    // Сообщение для ошибки при DELETE: внешний ключ здесь значит «на запись ещё ссылаются»
    public static string DescribeDelete(PostgresException ex)
    {
        if (ex.SqlState != ForeignKeyViolation)
            return Describe(ex);

        var table = ex.TableName is { } t && TableTitles.TryGetValue(t, out var title) ? title : ex.TableName;
        return $"Нельзя удалить: на запись ссылаются {table}.";
    }
}
