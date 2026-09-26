CREATE EXTENSION IF NOT EXISTS btree_gist;



-- номера

-- отели сети название, город, адрес, звёздность, телефон
CREATE TABLE hotels (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name     varchar(150) NOT NULL,
    city     varchar(100) NOT NULL,
    address  varchar(250) NOT NULL,
    stars    smallint NOT NULL CHECK (stars BETWEEN 1 AND 5),
    phone    varchar(20)
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


-- позиции оклад и ставка
CREATE TABLE positions (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name         varchar(80) NOT NULL,
    base_salary  numeric(10,2),
    hourly_rate  numeric(8,2)
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
    country          varchar(60)
);

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
    booking_date     timestamp NOT NULL DEFAULT now(),
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















-- аренда

-- типы помещ
CREATE TABLE unit_types (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name  varchar(60) NOT NULL
);

-- коммерч помещения
CREATE TABLE commercial_units (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    hotel_id      int NOT NULL REFERENCES hotels(id) ON DELETE RESTRICT,
    unit_type_id  int NOT NULL REFERENCES unit_types(id) ON DELETE RESTRICT,
    unit_number   varchar(10) NOT NULL,
    floor         smallint,
    area_sqm      numeric(8,2) NOT NULL CHECK (area_sqm > 0),
    status        varchar(20) NOT NULL DEFAULT 'свободно'
                  CHECK (status IN ('свободно', 'сдано', 'ремонт')),
    UNIQUE (hotel_id, unit_number)
);

-- Арендаторы 
CREATE TABLE tenants (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_name   varchar(150) NOT NULL,
    bin            varchar(12) NOT NULL UNIQUE,
    legal_address  varchar(250),
    director_name  varchar(150),
    phone          varchar(20),
    email          varchar(120)
);

-- Договоры аренды
CREATE TABLE lease_contracts (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id        int NOT NULL REFERENCES tenants(id) ON DELETE RESTRICT,
    contract_number  varchar(30) NOT NULL UNIQUE,
    date_start       date NOT NULL,
    date_end         date NOT NULL,
    rate_per_sqm     numeric(10,2) NOT NULL CHECK (rate_per_sqm > 0),
    deposit          numeric(12,2) DEFAULT 0,
    status           varchar(20) NOT NULL DEFAULT 'действует'
                     CHECK (status IN ('действует', 'продлён', 'расторгнут', 'истёк')),
    CHECK (date_end > date_start)
);

-- Какие помещения входят в договор и на какой срок 
CREATE TABLE contract_units (
    contract_id  int NOT NULL REFERENCES lease_contracts(id) ON DELETE CASCADE,
    unit_id      int NOT NULL REFERENCES commercial_units(id) ON DELETE RESTRICT,
    date_from    date NOT NULL,
    date_to      date NOT NULL,
    is_active    boolean NOT NULL DEFAULT true,
    PRIMARY KEY (contract_id, unit_id),
    CHECK (date_to >= date_from),
    EXCLUDE USING gist (
        unit_id WITH =,
        daterange(date_from, date_to, '[]') WITH &&
    ) WHERE (is_active)
);

-- Счётчики в помещениях
CREATE TABLE utility_meters (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    unit_id        int NOT NULL REFERENCES commercial_units(id) ON DELETE CASCADE,
    meter_type     varchar(20) NOT NULL CHECK (meter_type IN ('электричество', 'вода', 'тепло')),
    serial_number  varchar(40) NOT NULL UNIQUE
);

-- Показания счётчиков по датам
CREATE TABLE meter_readings (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    meter_id      int NOT NULL REFERENCES utility_meters(id) ON DELETE CASCADE,
    reading_date  date NOT NULL,
    value         numeric(12,3) NOT NULL CHECK (value >= 0),
    UNIQUE (meter_id, reading_date)
);

-- Тарифы на коммунальные услуги 
CREATE TABLE utility_tariffs (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    meter_type      varchar(20) NOT NULL CHECK (meter_type IN ('электричество', 'вода', 'тепло')),
    price_per_unit  numeric(10,4) NOT NULL CHECK (price_per_unit > 0),
    valid_from      date NOT NULL
);






















-- финансы

-- Счетаза проживание или за аренду 
CREATE TABLE invoices (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    invoice_number   varchar(30) NOT NULL UNIQUE,
    invoice_type     varchar(20) NOT NULL CHECK (invoice_type IN ('проживание', 'аренда')),
    booking_id       int REFERENCES bookings(id) ON DELETE RESTRICT,
    contract_id      int REFERENCES lease_contracts(id) ON DELETE RESTRICT,
    issue_date       date NOT NULL DEFAULT CURRENT_DATE,
    due_date         date NOT NULL,
    total_amount     numeric(12,2) NOT NULL DEFAULT 0,
    status           varchar(20) NOT NULL DEFAULT 'выставлен'
                     CHECK (status IN ('выставлен', 'частично оплачен', 'оплачен', 'просрочен')),
    CHECK (
        (booking_id IS NOT NULL AND contract_id IS NULL)
        OR (booking_id IS NULL AND contract_id IS NOT NULL)
    )
);

CREATE TABLE invoice_items (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    invoice_id   int NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    item_type    varchar(20) NOT NULL
                 CHECK (item_type IN ('проживание', 'услуга', 'аренда', 'коммуналка', 'пеня')),
    description  varchar(250),
    quantity     numeric(10,3) NOT NULL CHECK (quantity > 0),
    unit_price   numeric(12,4) NOT NULL CHECK (unit_price >= 0),
    amount       numeric(12,2) NOT NULL CHECK (amount >= 0)
);


-- по просроченным счетам
CREATE TABLE penalties (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    invoice_id    int NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    calc_date     date NOT NULL DEFAULT CURRENT_DATE,
    days_overdue  int NOT NULL CHECK (days_overdue >= 0),
    amount        numeric(12,2) NOT NULL CHECK (amount >= 0)
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

-- начисления зарплаты сотруднику за месяц
CREATE TABLE payrolls (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    employee_id    int NOT NULL REFERENCES employees(id) ON DELETE RESTRICT,
    period_month   date NOT NULL,
    hours_total    numeric(6,2) NOT NULL DEFAULT 0,
    base_amount    numeric(10,2) NOT NULL DEFAULT 0,
    bonus          numeric(10,2) NOT NULL DEFAULT 0,
    deduction      numeric(10,2) NOT NULL DEFAULT 0,
    total_amount   numeric(10,2) NOT NULL DEFAULT 0,
    payment_date   date,
    UNIQUE (employee_id, period_month)
);














--доступы

-- Учётки для входа в систему
CREATE TABLE users (
    id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    employee_id     int NOT NULL UNIQUE REFERENCES employees(id) ON DELETE CASCADE,
    login           varchar(50) NOT NULL UNIQUE,
    password_hash   varchar(255) NOT NULL,
    role            varchar(30) NOT NULL
                    CHECK (role IN ('админ', 'ресепшен', 'бухгалтер', 'управляющий арендой')),
    is_active       boolean NOT NULL DEFAULT true
);
--логи
CREATE TABLE audit_log (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id      int REFERENCES users(id) ON DELETE SET NULL,
    action       varchar(20) NOT NULL CHECK (action IN ('INSERT', 'UPDATE', 'DELETE')),
    table_name   varchar(60) NOT NULL,
    record_id    int,
    changed_at   timestamp NOT NULL DEFAULT now()
);

