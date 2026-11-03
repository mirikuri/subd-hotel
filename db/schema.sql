CREATE EXTENSION IF NOT EXISTS btree_gist;



-- номера

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
    phone   varchar(20)
);

-- справочник типов номеров
CREATE TABLE room_types (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name        varchar(80) NOT NULL,
    capacity    smallint NOT NULL CHECK (capacity > 0),
    description text
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
    UNIQUE (hotel_id, room_number)
);

-- Сезоны периоды
CREATE TABLE seasons (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name       varchar(60) NOT NULL,
    date_start date NOT NULL,
    date_end   date NOT NULL,
    CHECK (date_end >= date_start),
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



-- Сотрудники

-- позиции оклад и ставка (moved above employees to fix FK ordering bug)
CREATE TABLE positions (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name         varchar(80) NOT NULL UNIQUE,
    role         varchar(30) NOT NULL DEFAULT 'ресепшен',
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
    CHECK (dismissal_date IS NULL OR dismissal_date >= hire_date)
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
    login            varchar(50) UNIQUE,
    password_hash    varchar(500),
    is_active        boolean NOT NULL DEFAULT true,
    failed_attempts  int NOT NULL DEFAULT 0,
    locked_until     timestamptz,
    last_login_at    timestamptz,
    registered_at    timestamptz NOT NULL DEFAULT now()
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
    CHECK (check_out_plan > check_in_plan)
);

-- Какие номера входят в бронь и на какие даты
CREATE TABLE booking_rooms (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    booking_id    int NOT NULL REFERENCES bookings(id) ON DELETE CASCADE,
    room_id       int NOT NULL REFERENCES rooms(id) ON DELETE RESTRICT,
    date_from     date NOT NULL,
    date_to       date NOT NULL,
    guests_count  smallint NOT NULL DEFAULT 1 CHECK (guests_count > 0),
    is_active     boolean NOT NULL DEFAULT true,
    CHECK (date_to > date_from),
    EXCLUDE USING gist (
        room_id WITH =,
        daterange(date_from, date_to, '[)') WITH &&
    ) WHERE (is_active)
);

-- Фактические заселения
CREATE TABLE stays (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    booking_room_id    int NOT NULL REFERENCES booking_rooms(id) ON DELETE RESTRICT,
    actual_check_in    timestamp NOT NULL DEFAULT now(),
    actual_check_out   timestamp,
    employee_id        int NOT NULL REFERENCES employees(id) ON DELETE RESTRICT,
    CHECK (actual_check_out IS NULL OR actual_check_out > actual_check_in)
);




-- услуги

CREATE TABLE service_categories (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name  varchar(60) NOT NULL
);

-- Доп услуги конкретного отеля
CREATE TABLE services (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    category_id  int NOT NULL REFERENCES service_categories(id) ON DELETE RESTRICT,
    hotel_id     int NOT NULL REFERENCES hotels(id) ON DELETE RESTRICT,
    name         varchar(120) NOT NULL,
    price        numeric(10,2) NOT NULL CHECK (price >= 0),
    unit         varchar(20)
);

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
    CHECK (shift_end > shift_start),
    CHECK (shift_end - shift_start <= interval '24 hours'),
    EXCLUDE USING gist (
        employee_id WITH =,
        tsrange(shift_start, shift_end, '[)') WITH &&
    )
);


--доступы

-- Учётки для входа в систему
CREATE TABLE users (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    employee_id     int NOT NULL UNIQUE REFERENCES employees(id) ON DELETE CASCADE,
    login           varchar(50) NOT NULL UNIQUE,
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

--логи
CREATE TABLE audit_log (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id      int REFERENCES users(id) ON DELETE SET NULL,
    action       varchar(20) NOT NULL CHECK (action IN ('INSERT', 'UPDATE', 'DELETE')),
    table_name   varchar(60) NOT NULL,
    record_id    int,
    changed_at   timestamp NOT NULL DEFAULT now()
);
