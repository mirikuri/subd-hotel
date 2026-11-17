CREATE EXTENSION IF NOT EXISTS btree_gist;



-- номера
-- Все денежные столбцы (rates, services, service_orders) — в долларах США.

-- справочник стран
CREATE TABLE countries (
    id   int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name varchar(80) NOT NULL UNIQUE
);

-- справочник городов
CREATE TABLE cities (
    id         int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    country_id int NOT NULL REFERENCES countries(id) ON DELETE RESTRICT,
    name       varchar(100) NOT NULL,
    UNIQUE (country_id, name)
);

-- отели сети название, город, адрес, звёздность, телефон
CREATE TABLE hotels (
    id      int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name    varchar(150) NOT NULL,
    city_id int NOT NULL REFERENCES cities(id) ON DELETE RESTRICT,
    address varchar(250) NOT NULL,
    stars   smallint NOT NULL CHECK (stars BETWEEN 1 AND 5),
    phone   varchar(20),
    email          varchar(120),
    check_in_time  time NOT NULL DEFAULT '14:00',
    check_out_time time NOT NULL DEFAULT '12:00',
    description    text,
    CONSTRAINT hotels_name_key UNIQUE (name)
);

-- справочник типов номеров
CREATE TABLE room_types (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name        varchar(80) NOT NULL,
    capacity    smallint NOT NULL CHECK (capacity > 0),
    description text,
    area_m2     numeric(5,1) CONSTRAINT room_types_area_check CHECK (area_m2 > 0),
    bed_type    varchar(30)  CONSTRAINT room_types_bed_type_check
                CHECK (bed_type IN ('односпальная', 'двуспальная', 'две односпальные', 'king-size')),
    CONSTRAINT room_types_name_key UNIQUE (name)
);

-- справочник удобств номера (Wi-Fi, кондиционер, ...)
CREATE TABLE amenities (
    id   int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name varchar(60) NOT NULL CONSTRAINT amenities_name_key UNIQUE,
    icon varchar(40)            -- имя значка Bootstrap Icons, например 'wifi'
);

-- какие удобства есть у типа номера (многие ко многим)
CREATE TABLE room_type_amenities (
    room_type_id int NOT NULL REFERENCES room_types(id) ON DELETE CASCADE,
    amenity_id   int NOT NULL REFERENCES amenities(id)  ON DELETE RESTRICT,
    PRIMARY KEY (room_type_id, amenity_id)
);

-- Конкретные номера конкретного отеля
CREATE TABLE rooms (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    hotel_id      int NOT NULL REFERENCES hotels(id) ON DELETE RESTRICT,
    room_type_id  int NOT NULL REFERENCES room_types(id) ON DELETE RESTRICT,
    room_number   varchar(10) NOT NULL,
    floor         smallint,
    status        varchar(20) NOT NULL DEFAULT 'свободен'
                  CHECK (status IN ('свободен', 'занят', 'уборка', 'ремонт')),
    view          varchar(20) CONSTRAINT rooms_view_check
                  CHECK (view IN ('город', 'парк', 'двор', 'горы')),
    UNIQUE (hotel_id, room_number),
    -- цель составного внешнего ключа booking_rooms (номер + его отель)
    CONSTRAINT rooms_id_hotel_key UNIQUE (id, hotel_id)
);

-- Сезоны периоды
CREATE TABLE seasons (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name       varchar(60) NOT NULL,
    date_start date NOT NULL,
    date_end   date NOT NULL,
    CONSTRAINT seasons_dates_check CHECK (date_end >= date_start),
    CONSTRAINT seasons_no_overlap
        EXCLUDE USING gist (daterange(date_start, date_end, '[]') WITH &&)
);

-- цена за тип номера в сезон
CREATE TABLE rates (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    room_type_id     int NOT NULL REFERENCES room_types(id) ON DELETE RESTRICT,
    season_id        int NOT NULL REFERENCES seasons(id) ON DELETE RESTRICT,
    price_per_night  numeric(10,2) NOT NULL CHECK (price_per_night > 0),
    UNIQUE (room_type_id, season_id)
);
COMMENT ON COLUMN rates.price_per_night IS 'USD';



-- Сотрудники

-- позиции оклад и ставка (moved above employees to fix FK ordering bug)
CREATE TABLE positions (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name         varchar(80) NOT NULL UNIQUE,
    role         varchar(30) NOT NULL DEFAULT 'ресепшен'
                 CONSTRAINT positions_role_check CHECK (role IN ('админ', 'ресепшен', 'бухгалтер')),
    base_salary  numeric(10,2),
    hourly_rate  numeric(8,2)
);

CREATE TABLE employees (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    hotel_id         int NOT NULL REFERENCES hotels(id) ON DELETE RESTRICT,
    position_id      int NOT NULL REFERENCES positions(id) ON DELETE RESTRICT,
    full_name        varchar(150) NOT NULL,
    phone            varchar(20),
    hire_date        date NOT NULL,
    dismissal_date   date,
    CONSTRAINT employees_dates_check CHECK (dismissal_date IS NULL OR dismissal_date >= hire_date)
);




--гости


-- Гости:
CREATE TABLE guests (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    full_name        varchar(150) NOT NULL,
    document_number  varchar(40) NOT NULL UNIQUE,
    birth_date       date,
    phone            varchar(20),
    email            varchar(120),
    country_id       int REFERENCES countries(id) ON DELETE RESTRICT,
    login            varchar(50),             -- уникальность — индекс guests_login_lower_key ниже
    password_hash    varchar(500),
    is_active        boolean NOT NULL DEFAULT true,
    failed_attempts  int NOT NULL DEFAULT 0,
    locked_until     timestamptz,
    last_login_at    timestamptz,
    registered_at    timestamptz NOT NULL DEFAULT now(),
    -- учётная запись сайта либо есть целиком (логин и пароль), либо её нет
    CONSTRAINT guests_account_check CHECK ((login IS NULL) = (password_hash IS NULL))
);

-- Вход ищет логин без учёта регистра (lower(login) = lower(@login)),
-- поэтому и уникальность — без учёта регистра: «Ivanov» и «ivanov» не могут сосуществовать
CREATE UNIQUE INDEX guests_login_lower_key ON guests (lower(login));

-- Карты программы лояльности
CREATE TABLE loyalty_cards (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    guest_id      int NOT NULL UNIQUE REFERENCES guests(id) ON DELETE CASCADE,
    card_number   varchar(20) NOT NULL UNIQUE,
    level         varchar(20) NOT NULL DEFAULT 'silver'
                  CHECK (level IN ('silver', 'gold', 'platinum')),
    bonus_points  int NOT NULL DEFAULT 0 CHECK (bonus_points >= 0),
    issue_date    date NOT NULL DEFAULT CURRENT_DATE
);




-- Бронирования
CREATE TABLE bookings (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    guest_id         int NOT NULL REFERENCES guests(id) ON DELETE RESTRICT,
    hotel_id         int NOT NULL REFERENCES hotels(id) ON DELETE RESTRICT,
    booking_date     timestamptz NOT NULL DEFAULT now(),
    check_in_plan    date NOT NULL,
    check_out_plan   date NOT NULL,
    status           varchar(20) NOT NULL DEFAULT 'новая'
                     CHECK (status IN ('новая', 'подтверждена', 'заселён', 'завершена', 'отменена')),
    source           varchar(30)
                     CHECK (source IN ('сайт', 'телефон', 'агрегатор', 'стойка')),
    cancel_reason    text,
    guest_comment    varchar(500),   -- пожелания гостя из заявки с сайта
    CONSTRAINT bookings_dates_check CHECK (check_out_plan > check_in_plan),
    -- причина отмены есть у отменённой брони и только у неё
    CONSTRAINT bookings_cancel_reason_check
        CHECK ((status = 'отменена') = (cancel_reason IS NOT NULL)),
    -- цель составного внешнего ключа booking_rooms (бронь + её отель)
    CONSTRAINT bookings_id_hotel_key UNIQUE (id, hotel_id)
);

-- Какие номера входят в бронь и на какие даты.
-- hotel_id повторяет отель брони и отель номера, а два составных внешних ключа заставляют
-- их совпадать: привязать к брони номер другого отеля нельзя даже прямым INSERT в psql.
CREATE TABLE booking_rooms (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    booking_id    int NOT NULL,
    room_id       int NOT NULL,
    hotel_id      int NOT NULL,
    date_from     date NOT NULL,
    date_to       date NOT NULL,
    guests_count  smallint NOT NULL DEFAULT 1 CHECK (guests_count > 0),
    is_active     boolean NOT NULL DEFAULT true,
    CONSTRAINT booking_rooms_booking_fkey
        FOREIGN KEY (booking_id, hotel_id) REFERENCES bookings (id, hotel_id) ON DELETE CASCADE,
    CONSTRAINT booking_rooms_room_fkey
        FOREIGN KEY (room_id, hotel_id) REFERENCES rooms (id, hotel_id) ON DELETE RESTRICT,
    CONSTRAINT booking_rooms_dates_check CHECK (date_to > date_from),
    -- один номер — одна активная бронь на ночь
    CONSTRAINT booking_rooms_no_overlap EXCLUDE USING gist (
        room_id WITH =,
        daterange(date_from, date_to, '[)') WITH &&
    ) WHERE (is_active)
);

-- Гостей в номере не больше, чем вмещает его тип. CHECK не может заглянуть в другую таблицу
-- (вместимость лежит в room_types), поэтому это триггер. Ошибка оформлена как нарушение
-- ограничения с именем booking_rooms_capacity_check — приложение ловит её так же, как CHECK.
CREATE FUNCTION booking_rooms_check_capacity() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    cap smallint;
BEGIN
    SELECT rt.capacity INTO cap
    FROM rooms r JOIN room_types rt ON rt.id = r.room_type_id
    WHERE r.id = NEW.room_id;

    IF NEW.guests_count > cap THEN
        RAISE EXCEPTION 'В номере помещается не больше % гостей, указано %', cap, NEW.guests_count
            USING ERRCODE = 'check_violation', CONSTRAINT = 'booking_rooms_capacity_check';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER booking_rooms_capacity_check
    BEFORE INSERT OR UPDATE OF room_id, guests_count ON booking_rooms
    FOR EACH ROW EXECUTE FUNCTION booking_rooms_check_capacity();

-- Фактические заселения
CREATE TABLE stays (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    booking_room_id    int NOT NULL REFERENCES booking_rooms(id) ON DELETE RESTRICT,
    actual_check_in    timestamp NOT NULL DEFAULT now(),
    actual_check_out   timestamp,
    employee_id        int NOT NULL REFERENCES employees(id) ON DELETE RESTRICT,
    CONSTRAINT stays_dates_check CHECK (actual_check_out IS NULL OR actual_check_out > actual_check_in)
);

-- У строки брони не больше одного открытого заселения (гость не может заехать дважды, не выехав)
CREATE UNIQUE INDEX stays_one_open_key ON stays (booking_room_id) WHERE actual_check_out IS NULL;




-- услуги

CREATE TABLE service_categories (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name  varchar(60) NOT NULL CONSTRAINT service_categories_name_key UNIQUE
);

-- Доп услуги конкретного отеля
CREATE TABLE services (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    category_id  int NOT NULL REFERENCES service_categories(id) ON DELETE RESTRICT,
    hotel_id     int NOT NULL REFERENCES hotels(id) ON DELETE RESTRICT,
    name         varchar(120) NOT NULL,
    price        numeric(10,2) NOT NULL CHECK (price >= 0),
    unit         varchar(20),
    description  text,
    is_active    boolean NOT NULL DEFAULT true   -- false: услуга снята с продажи
);
COMMENT ON COLUMN services.price IS 'USD';

-- Услуги в рамках брони
CREATE TABLE service_orders (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    booking_id       int NOT NULL REFERENCES bookings(id) ON DELETE CASCADE,
    service_id       int NOT NULL REFERENCES services(id) ON DELETE RESTRICT,
    employee_id      int REFERENCES employees(id) ON DELETE SET NULL,
    order_date       timestamp NOT NULL DEFAULT now(),
    quantity         numeric(8,2) NOT NULL CHECK (quantity > 0),
    price_at_order   numeric(10,2) NOT NULL CHECK (price_at_order >= 0)
);
COMMENT ON COLUMN service_orders.price_at_order IS 'USD';




-- смены зарплата

-- график смен сотрудников.
CREATE TABLE shifts (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    employee_id   int NOT NULL REFERENCES employees(id) ON DELETE CASCADE,
    shift_start   timestamp NOT NULL,
    shift_end     timestamp NOT NULL,
    hours_worked  numeric(5,2) GENERATED ALWAYS AS (
                      round((extract(epoch FROM shift_end - shift_start) / 3600)::numeric, 2)
                  ) STORED,
    CONSTRAINT shifts_dates_check  CHECK (shift_end > shift_start),
    CONSTRAINT shifts_length_check CHECK (shift_end - shift_start <= interval '24 hours'),
    CONSTRAINT shifts_no_overlap EXCLUDE USING gist (
        employee_id WITH =,
        tsrange(shift_start, shift_end, '[)') WITH &&
    )
);


--доступы

-- Учётки для входа в систему
CREATE TABLE users (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    employee_id     int NOT NULL UNIQUE REFERENCES employees(id) ON DELETE CASCADE,
    login           varchar(50) NOT NULL,    -- уникальность — индекс users_login_lower_key ниже
    password_hash   varchar(255) NOT NULL,
    role            varchar(30) NOT NULL
                    CHECK (role IN ('админ', 'ресепшен', 'бухгалтер')),
    is_active       boolean NOT NULL DEFAULT true,
    failed_attempts int NOT NULL DEFAULT 0,
    locked_until    timestamptz,
    last_login_at   timestamptz
);

-- Логин уникален без учёта регистра — так его ищет LoginService
CREATE UNIQUE INDEX users_login_lower_key ON users (lower(login));

-- ── Журнал изменений ─────────────────────────────────────────────────────────
-- Пишется только триггером audit_row (ниже), приложение в него не вставляет.
CREATE TABLE audit_log (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id      int REFERENCES users(id) ON DELETE SET NULL,   -- NULL: гость сайта, psql, seed
    action       varchar(20) NOT NULL CHECK (action IN ('INSERT', 'UPDATE', 'DELETE')),
    table_name   varchar(60) NOT NULL,
    record_id    int,
    changed_at   timestamptz NOT NULL DEFAULT now(),
    old_data     jsonb,   -- UPDATE: прежние значения изменённых столбцов; DELETE: вся строка
    new_data     jsonb    -- UPDATE: новые значения изменённых столбцов;  INSERT: вся строка
);

-- Кто меняет: приложение после открытия соединения выполняет
--     SELECT set_config('app.user_id', '<users.id>', false)        (Data/Db.cs)
-- Для массовой загрузки журнал отключается на сеанс: SET app.audit = 'off' (db/seed.sql).
CREATE FUNCTION audit_row() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    -- пароли в журнал не попадают; счётчики входа меняются при каждом входе и журнал только засоряют
    skip  constant text[] := ARRAY['password_hash', 'failed_attempts', 'locked_until', 'last_login_at'];
    o     jsonb;
    n     jsonb;
    actor int;
    rec   int;
BEGIN
    IF current_setting('app.audit', true) = 'off' THEN
        RETURN NULL;
    END IF;

    IF TG_OP <> 'INSERT' THEN o := to_jsonb(OLD) - skip; END IF;
    IF TG_OP <> 'DELETE' THEN n := to_jsonb(NEW) - skip; END IF;
    rec := coalesce(n ->> 'id', o ->> 'id')::int;   -- до того, как от UPDATE останутся только изменённые столбцы

    IF TG_OP = 'UPDATE' THEN
        IF o = n THEN
            RETURN NULL;                        -- изменились только пропускаемые столбцы
        END IF;
        -- оставляем только то, что изменилось
        SELECT jsonb_object_agg(e.key, o -> e.key), jsonb_object_agg(e.key, e.value)
        INTO o, n
        FROM jsonb_each(n) AS e
        WHERE o -> e.key IS DISTINCT FROM e.value;
    END IF;

    -- id из настройки сеанса; через подзапрос, чтобы несуществующий id дал NULL, а не ошибку FK
    SELECT u.id INTO actor
    FROM users u
    WHERE u.id = nullif(current_setting('app.user_id', true), '')::int;

    INSERT INTO audit_log (user_id, action, table_name, record_id, old_data, new_data)
    VALUES (actor, TG_OP, TG_TABLE_NAME, rec, o, n);
    RETURN NULL;
END;
$$;

CREATE TRIGGER audit AFTER INSERT OR UPDATE OR DELETE ON bookings       FOR EACH ROW EXECUTE FUNCTION audit_row();
CREATE TRIGGER audit AFTER INSERT OR UPDATE OR DELETE ON booking_rooms  FOR EACH ROW EXECUTE FUNCTION audit_row();
CREATE TRIGGER audit AFTER INSERT OR UPDATE OR DELETE ON stays          FOR EACH ROW EXECUTE FUNCTION audit_row();
CREATE TRIGGER audit AFTER INSERT OR UPDATE OR DELETE ON service_orders FOR EACH ROW EXECUTE FUNCTION audit_row();
CREATE TRIGGER audit AFTER INSERT OR UPDATE OR DELETE ON guests         FOR EACH ROW EXECUTE FUNCTION audit_row();
CREATE TRIGGER audit AFTER INSERT OR UPDATE OR DELETE ON employees      FOR EACH ROW EXECUTE FUNCTION audit_row();
CREATE TRIGGER audit AFTER INSERT OR UPDATE OR DELETE ON positions      FOR EACH ROW EXECUTE FUNCTION audit_row();
CREATE TRIGGER audit AFTER INSERT OR UPDATE OR DELETE ON users          FOR EACH ROW EXECUTE FUNCTION audit_row();
CREATE TRIGGER audit AFTER INSERT OR UPDATE OR DELETE ON hotels         FOR EACH ROW EXECUTE FUNCTION audit_row();
CREATE TRIGGER audit AFTER INSERT OR UPDATE OR DELETE ON room_types     FOR EACH ROW EXECUTE FUNCTION audit_row();
CREATE TRIGGER audit AFTER INSERT OR UPDATE OR DELETE ON rooms          FOR EACH ROW EXECUTE FUNCTION audit_row();
CREATE TRIGGER audit AFTER INSERT OR UPDATE OR DELETE ON seasons        FOR EACH ROW EXECUTE FUNCTION audit_row();
CREATE TRIGGER audit AFTER INSERT OR UPDATE OR DELETE ON rates          FOR EACH ROW EXECUTE FUNCTION audit_row();
CREATE TRIGGER audit AFTER INSERT OR UPDATE OR DELETE ON services       FOR EACH ROW EXECUTE FUNCTION audit_row();


-- ── Индексы ──────────────────────────────────────────────────────────────────
-- PostgreSQL сам создаёт индексы для PRIMARY KEY, UNIQUE и EXCLUDE, но не для столбцов
-- внешних ключей. Без них каждая проверка ON DELETE RESTRICT / CASCADE при удалении
-- родительской строки и каждый JOIN «родитель → дети» — последовательный просмотр.
-- Здесь нет столбцов, которые уже стоят первыми в UNIQUE (rooms.hotel_id, rates.room_type_id,
-- cities.country_id, loyalty_cards.guest_id, users.employee_id).
CREATE INDEX hotels_city_id_idx               ON hotels (city_id);
CREATE INDEX rooms_room_type_id_idx           ON rooms (room_type_id);
CREATE INDEX room_type_amenities_amenity_idx  ON room_type_amenities (amenity_id);
CREATE INDEX rates_season_id_idx              ON rates (season_id);
CREATE INDEX employees_hotel_id_idx           ON employees (hotel_id);
CREATE INDEX employees_position_id_idx        ON employees (position_id);
CREATE INDEX guests_country_id_idx            ON guests (country_id);
CREATE INDEX bookings_guest_id_idx            ON bookings (guest_id);
CREATE INDEX bookings_hotel_id_idx            ON bookings (hotel_id);
-- под составные внешние ключи; EXCLUDE-индекс по room_id частичный (только is_active) и их не заменяет
CREATE INDEX booking_rooms_booking_idx        ON booking_rooms (booking_id, hotel_id);
CREATE INDEX booking_rooms_room_idx           ON booking_rooms (room_id, hotel_id);
CREATE INDEX stays_booking_room_id_idx        ON stays (booking_room_id);
CREATE INDEX stays_employee_id_idx            ON stays (employee_id);
CREATE INDEX services_hotel_id_idx            ON services (hotel_id);
CREATE INDEX services_category_id_idx         ON services (category_id);
CREATE INDEX service_orders_booking_id_idx    ON service_orders (booking_id);
CREATE INDEX service_orders_service_id_idx    ON service_orders (service_id);
CREATE INDEX service_orders_employee_id_idx   ON service_orders (employee_id);
CREATE INDEX audit_log_user_id_idx            ON audit_log (user_id);

-- Список броней в админке сортируется по дате оформления, отчёты и «Мои брони» ищут по заезду
CREATE INDEX bookings_booking_date_idx        ON bookings (booking_date DESC);
CREATE INDEX bookings_check_in_plan_idx       ON bookings (check_in_plan);
CREATE INDEX audit_log_changed_at_idx         ON audit_log (changed_at DESC);
