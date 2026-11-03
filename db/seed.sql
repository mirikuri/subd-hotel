-- Демо-данные для Qonaq Hotels. Накатывается после schema.sql скриптом db/reset.sh.
--
-- Записи связываются по естественным ключам (название страны, отеля, номер документа),
-- а не по id: так скрипт не зависит от того, какие id выдаст GENERATED ALWAYS AS IDENTITY.
-- Даты броней, смен и заселений считаются от CURRENT_DATE, поэтому после каждого
-- пересоздания базы шахматка показывает живую картину вокруг сегодняшнего дня.
--
-- Учётных записей (users) здесь нет: пароль нужно хэшировать PasswordHasher из ASP.NET,
-- поэтому admin создаёт приложение при первом запуске (Data/Seeder.cs).


-- ── География ────────────────────────────────────────────────────────────────

INSERT INTO countries (name) VALUES
    ('Казахстан'), ('Россия'), ('Узбекистан'), ('Кыргызстан'), ('Таджикистан'),
    ('Азербайджан'), ('Грузия'), ('Армения'), ('Беларусь'), ('Китай'),
    ('Южная Корея'), ('Турция'), ('ОАЭ'), ('Германия'), ('Франция'),
    ('Италия'), ('Великобритания'), ('США'), ('Индия'), ('Япония');

-- Города, где есть отели сети: Казахстан и столицы соседних стран
INSERT INTO cities (country_id, name)
SELECT c.id, v.city
FROM (VALUES
    ('Казахстан',  'Астана'),
    ('Казахстан',  'Алматы'),
    ('Казахстан',  'Шымкент'),
    ('Россия',     'Москва'),
    ('Узбекистан', 'Ташкент'),
    ('Кыргызстан', 'Бишкек')
) AS v(country, city)
JOIN countries c ON c.name = v.country;


-- ── Отели и номера ───────────────────────────────────────────────────────────

INSERT INTO room_types (name, capacity, description) VALUES
    ('Стандарт',    2, 'Двуспальная или две односпальные кровати, рабочий стол, душ. Для коротких поездок.'),
    ('Комфорт',     2, 'Больше места, кресло для отдыха, кофемашина и вид на город.'),
    ('Полулюкс',    3, 'Спальня и гостиная зона в одном пространстве, диван-кровать для третьего гостя.'),
    ('Люкс',        2, 'Отдельная гостиная, ванна, халаты и тапочки, панорамные окна.'),
    ('Семейный',    4, 'Две комнаты, детская кроватка по запросу, место для игр.'),
    ('Бизнес-люкс', 3, 'Кабинет с рабочим столом, гостиная для встреч, кофемашина и минибар.');

INSERT INTO hotels (name, city_id, address, stars, phone)
SELECT v.name, c.id, v.address, v.stars::smallint, v.phone
FROM (VALUES
    ('Qonaq Astana Center', 'Астана',  'пр. Мангилик Ел, 55', '5', '+7 700 000 00 01'),
    ('Qonaq Almaty Park',   'Алматы',  'пр. Абая, 120',        '4', '+7 700 000 00 02'),
    ('Qonaq Shymkent',      'Шымкент', 'ул. Тауке хана, 18',   '3', '+7 700 000 00 03'),
    ('Qonaq Moscow Arbat',  'Москва',  'ул. Новый Арбат, 21',  '5', '+7 495 000 00 04'),
    ('Qonaq Tashkent City', 'Ташкент', 'пр. Амира Темура, 107','4', '+998 71 000 00 05'),
    ('Qonaq Bishkek',       'Бишкек',  'пр. Чуй, 168',         '4', '+996 312 00 00 06')
) AS v(name, city_name, address, stars, phone)
JOIN cities c ON c.name = v.city_name;

-- 40 номеров на каждый отель: этажи 2–5, номера 1–10, типы по кругу
INSERT INTO rooms (hotel_id, room_type_id, room_number, floor)
SELECT h.id, rt.id, (f * 100 + n)::text, f
FROM hotels h
CROSS JOIN generate_series(2, 5)  AS f
CROSS JOIN generate_series(1, 10) AS n
JOIN room_types rt ON rt.name = (ARRAY['Стандарт', 'Комфорт', 'Полулюкс', 'Люкс', 'Семейный', 'Бизнес-люкс'])[((n - 1) % 6) + 1];

-- Номер 410 во всех отелях на ремонте (в подборе свободных номеров не участвует),
-- в Астане 305 на уборке
UPDATE rooms SET status = 'ремонт' WHERE room_number = '410';
UPDATE rooms r SET status = 'уборка'
FROM hotels h
WHERE h.id = r.hotel_id AND h.name = 'Qonaq Astana Center' AND r.room_number = '305';


-- ── Сезоны и тарифы ──────────────────────────────────────────────────────────

-- Сезоны идут подряд без пересечений (это проверяет EXCLUDE в seasons):
-- зима — с 1 декабря прошлого года до конца февраля, весна, лето, осень — по три месяца.
-- Покрывают прошлый год, текущий и два следующих: брони разрешены на год вперёд.
INSERT INTO seasons (name, date_start, date_end)
SELECT s.title || ' ' || CASE WHEN s.code = 'winter' THEN (y - 1) || '/' || right(y::text, 2) ELSE y::text END,
       make_date(CASE WHEN s.code = 'winter' THEN y - 1 ELSE y END, s.month_start, 1),
       (make_date(y, s.month_end, 1) + interval '1 month' - interval '1 day')::date
FROM generate_series(extract(year FROM CURRENT_DATE)::int - 1,
                     extract(year FROM CURRENT_DATE)::int + 2) AS y
CROSS JOIN (VALUES
    ('winter', 'Зима',  12,  2),
    ('spring', 'Весна',  3,  5),
    ('summer', 'Лето',   6,  8),
    ('autumn', 'Осень',  9, 11)
) AS s(code, title, month_start, month_end);

-- Цена за ночь: базовая цена типа номера × сезонный коэффициент
INSERT INTO rates (room_type_id, season_id, price_per_night)
SELECT rt.id, s.id,
       round(base.price * CASE split_part(s.name, ' ', 1)
                              WHEN 'Лето'  THEN 1.25
                              WHEN 'Зима'  THEN 1.10
                              WHEN 'Весна' THEN 0.95
                              ELSE 1.00
                          END, -2)
FROM seasons s
CROSS JOIN room_types rt
JOIN (VALUES
    ('Стандарт',    25000),
    ('Комфорт',     32000),
    ('Полулюкс',    42000),
    ('Люкс',        65000),
    ('Семейный',    52000),
    ('Бизнес-люкс', 80000)
) AS base(type_name, price) ON base.type_name = rt.name;


-- ── Персонал ─────────────────────────────────────────────────────────────────

-- role — роль, которую получает учётка сотрудника на этой должности (users_role_check)
INSERT INTO positions (name, role, base_salary, hourly_rate) VALUES
    -- руководство
    ('администратор',                    'админ',     450000, NULL),
    ('управляющий отелем',               'админ',     600000, NULL),
    ('заместитель управляющего',         'админ',     480000, NULL),
    -- ресепшен и работа с гостями
    ('старший администратор ресепшена',  'ресепшен',  340000, NULL),
    ('администратор ресепшена',          'ресепшен',  280000, NULL),
    ('менеджер по бронированию',         'ресепшен',  300000, NULL),
    ('менеджер по продажам',             'ресепшен',  320000, NULL),
    ('консьерж',                         'ресепшен',  260000, NULL),
    ('портье',                           'ресепшен',  220000, NULL),
    -- финансы
    ('главный бухгалтер',                'бухгалтер', 480000, NULL),
    ('бухгалтер',                        'бухгалтер', 350000, NULL),
    ('кассир',                           'бухгалтер', 240000, NULL),
    -- персонал без доступа к системе: роль — значение по умолчанию на случай выдачи учётки
    ('шеф-повар',                        'ресепшен',  420000, NULL),
    ('повар',                            'ресепшен',  NULL,   2200),
    ('официант',                         'ресепшен',  NULL,   1600),
    ('бармен',                           'ресепшен',  NULL,   1700),
    ('старшая горничная',                'ресепшен',  260000, NULL),
    ('горничная',                        'ресепшен',  NULL,   1500),
    ('техник',                           'ресепшен',  NULL,   1800),
    ('охранник',                         'ресепшен',  NULL,   1600),
    ('водитель',                         'ресепшен',  NULL,   1700),
    ('массажист',                        'ресепшен',  NULL,   2500);

INSERT INTO employees (hotel_id, position_id, full_name, phone, hire_date, dismissal_date)
SELECT h.id, p.id, v.full_name, v.phone, v.hire_date::date, v.dismissal_date::date
FROM (VALUES
    ('Qonaq Astana Center', 'управляющий отелем',      'Жаксылыков Бауыржан Маратович', '+77011110001', '2019-02-01', NULL),
    ('Qonaq Astana Center', 'администратор ресепшена', 'Оспанова Сауле Канатовна',      '+77011110002', '2021-05-10', NULL),
    ('Qonaq Astana Center', 'администратор ресепшена', 'Григорьев Антон Сергеевич',     '+77011110003', '2023-09-01', NULL),
    ('Qonaq Astana Center', 'бухгалтер',               'Мухамедова Асель Бериковна',    '+77011110004', '2020-03-15', NULL),
    ('Qonaq Astana Center', 'горничная',               'Ибраева Гульмира Сапаровна',    '+77011110005', '2022-06-01', NULL),
    ('Qonaq Astana Center', 'техник',                  'Курманов Ерболат Жанатович',    '+77011110006', '2018-11-20', NULL),
    ('Qonaq Astana Center', 'администратор ресепшена', 'Лебедева Ирина Павловна',       '+77011110007', '2020-01-10', '2025-08-31'),
    ('Qonaq Almaty Park',   'управляющий отелем',      'Сарсенов Даурен Ерланович',     '+77021110001', '2020-07-01', NULL),
    ('Qonaq Almaty Park',   'администратор ресепшена', 'Ким Виктория Александровна',    '+77021110002', '2022-02-14', NULL),
    ('Qonaq Almaty Park',   'администратор ресепшена', 'Байтурсынов Нурлан Асхатович',  '+77021110003', '2024-04-01', NULL),
    ('Qonaq Almaty Park',   'бухгалтер',               'Попова Светлана Игоревна',      '+77021110004', '2019-10-01', NULL),
    ('Qonaq Almaty Park',   'горничная',               'Алиева Зарина Руслановна',      '+77021110005', '2023-03-01', NULL),
    ('Qonaq Almaty Park',   'консьерж',                'Дюсенов Айдар Мейрамович',      '+77021110006', '2021-08-15', NULL),
    ('Qonaq Shymkent',      'управляющий отелем',      'Ташенов Ержан Абаевич',         '+77071110001', '2021-01-11', NULL),
    ('Qonaq Shymkent',      'администратор ресепшена', 'Юсупова Лола Хамидовна',        '+77071110002', '2022-09-05', NULL),
    ('Qonaq Shymkent',      'администратор ресепшена', 'Нуриев Ринат Ильдарович',       '+77071110003', '2025-02-01', NULL),
    ('Qonaq Shymkent',      'бухгалтер',               'Жунусова Айжан Талгатовна',     '+77071110004', '2021-01-11', NULL),
    ('Qonaq Shymkent',      'горничная',               'Садыкова Гульнар Есеновна',     '+77071110005', '2024-05-20', NULL),
    ('Qonaq Shymkent',      'техник',                  'Ахмедов Шерзод Улугбекович',    '+77071110006', '2023-07-01', NULL),
    ('Qonaq Moscow Arbat',  'управляющий отелем',      'Орлов Сергей Викторович',       '+79161110001', '2022-03-01', NULL),
    ('Qonaq Moscow Arbat',  'администратор ресепшена', 'Захарова Анна Дмитриевна',      '+79161110002', '2023-05-15', NULL),
    ('Qonaq Moscow Arbat',  'администратор ресепшена', 'Белов Кирилл Андреевич',        '+79161110003', '2024-09-01', NULL),
    ('Qonaq Tashkent City', 'управляющий отелем',      'Рашидов Фарход Бахромович',     '+998901110001', '2022-06-01', NULL),
    ('Qonaq Tashkent City', 'администратор ресепшена', 'Хасанова Нигора Акмаловна',     '+998901110002', '2023-02-01', NULL),
    ('Qonaq Bishkek',       'управляющий отелем',      'Жээнбеков Улан Маратович',      '+996701110001', '2023-01-10', NULL),
    ('Qonaq Bishkek',       'администратор ресепшена', 'Токтогулова Айпери Бакытовна',  '+996701110002', '2023-04-03', NULL)
) AS v(hotel, position, full_name, phone, hire_date, dismissal_date)
JOIN hotels h    ON h.name = v.hotel
JOIN positions p ON p.name = v.position;

-- Смены ресепшена на неделю назад и вперёд: первый администратор каждого отеля — днём
-- (08:00–20:00), второй — ночью (20:00–08:00). EXCLUDE в shifts не даёт сменам одного
-- сотрудника пересечься, hours_worked считает сама БД (GENERATED ALWAYS AS ... STORED).
INSERT INTO shifts (employee_id, shift_start, shift_end)
SELECT r.id,
       (CURRENT_DATE + k) + CASE WHEN r.rn = 1 THEN time '08:00' ELSE time '20:00' END,
       (CURRENT_DATE + k) + CASE WHEN r.rn = 1 THEN time '08:00' ELSE time '20:00' END + interval '12 hours'
FROM (
    SELECT e.id, row_number() OVER (PARTITION BY e.hotel_id ORDER BY e.id) AS rn
    FROM employees e
    JOIN positions p ON p.id = e.position_id
    WHERE p.name = 'администратор ресепшена' AND e.dismissal_date IS NULL
) AS r
CROSS JOIN generate_series(-7, 7) AS k
WHERE r.rn <= 2;


-- ── Гости и программа лояльности ─────────────────────────────────────────────

-- Гости заведены на стойке: без логина и пароля (их создаёт регистрация на сайте)
INSERT INTO guests (full_name, document_number, birth_date, phone, email, country_id)
SELECT v.full_name, v.doc, v.birth_date::date, v.phone, v.email, c.id
FROM (VALUES
    ('Ахметов Ерлан Серикович',        'N00000001',  '1985-03-12', '+77011234501',  'ahmetov.e@mail.kz',     'Казахстан'),
    ('Иванова Мария Петровна',         '4510123456', '1990-07-25', '+79161234502',  'm.ivanova@yandex.ru',   'Россия'),
    ('Сулейменова Айгерим Маратовна',  'N00000003',  '1993-11-02', '+77021234503',  'aigerim.s@gmail.com',   'Казахстан'),
    ('Петров Дмитрий Алексеевич',      '4512654321', '1978-01-30', '+79031234504',  NULL,                    'Россия'),
    ('Каримов Тимур Рустамович',       'AA1234567',  '1988-05-18', '+998901234505', 't.karimov@mail.uz',     'Узбекистан'),
    ('Нурланова Дана Ерлановна',       'N00000006',  '2000-09-09', '+77051234506',  'dana.n@mail.kz',        'Казахстан'),
    ('Жумабаев Нурсултан Кайратович',  'N00000007',  '1982-12-24', '+77071234507',  NULL,                    'Казахстан'),
    ('Смирнов Алексей Викторович',     '4509111222', '1975-04-04', '+79261234508',  'a.smirnov@mail.ru',     'Россия'),
    ('Абдуллаева Малика Шавкатовна',   'AB7654321',  '1995-02-14', '+998911234509', 'malika.a@gmail.com',    'Узбекистан'),
    ('Осмонов Бакыт Асанович',         'ID1234567',  '1987-08-08', '+996701234510', NULL,                    'Кыргызстан'),
    ('Ли Вэй',                         'E12345678',  '1980-10-10', '+861381234561', 'li.wei@163.com',        'Китай'),
    ('Йылмаз Мехмет',                  'U12345678',  '1983-06-15', '+905321234512', 'mehmet.y@gmail.com',    'Турция'),
    ('Шмидт Анна',                     'C01X00T47',  '1991-03-03', '+491511234561', 'anna.schmidt@web.de',   'Германия'),
    ('Тулегенов Асхат Болатович',      'N00000014',  '1970-01-20', '+77011234514',  NULL,                    'Казахстан'),
    ('Касымова Жанар Нурлановна',      'N00000015',  '1989-04-27', '+77021234515',  'zhanar.k@mail.kz',      'Казахстан'),
    ('Беков Арман Сакенович',          'N00000016',  '1996-12-01', '+77071234516',  'arman.bekov@gmail.com', 'Казахстан'),
    ('Ким Елена Сергеевна',            'N00000017',  '1984-08-19', '+77011234517',  'elena.kim@mail.kz',     'Казахстан'),
    ('Волков Игорь Николаевич',        '4515777888', '1972-11-11', '+79991234518',  NULL,                    'Россия'),
    ('Сапарова Алия Ериковна',         'N00000019',  '1998-06-06', '+77051234519',  'aliya.s@gmail.com',     'Казахстан'),
    ('Исмаилов Руслан Тахирович',      'N00000020',  '1979-02-02', '+77781234520',  NULL,                    'Казахстан'),
    ('Мамедова Сабина Эльдаровна',     'N00000021',  '1994-10-30', '+77011234521',  'sabina.m@mail.ru',      'Казахстан'),
    ('Новиков Павел Андреевич',        '4520333444', '1986-07-07', '+79121234522',  'p.novikov@gmail.com',   'Россия'),
    ('Есенова Гульнара Темировна',     'N00000023',  '1965-05-05', '+77021234523',  NULL,                    'Казахстан'),
    ('Турсунов Бахтиёр Алишерович',    'AC1122334',  '1990-01-01', '+998931234524', NULL,                    'Узбекистан'),
    ('Омарова Динара Кайратовна',      'N00000025',  '1992-09-21', '+77071234525',  'dinara.o@mail.kz',      'Казахстан'),
    ('Сергеев Константин Олегович',    '4518555666', '1981-03-29', '+79051234526',  'k.sergeev@yandex.ru',   'Россия'),
    ('Абенова Камила Даулетовна',      'N00000027',  '2001-12-12', '+77751234527',  'kamila.a@gmail.com',    'Казахстан'),
    ('Рахимов Ильяс Маратович',        'N00000028',  '1977-08-30', '+77011234528',  NULL,                    'Казахстан'),
    ('Федорова Ольга Ивановна',        '4511999000', '1969-02-17', '+79161234529',  'olga.f@mail.ru',        'Россия'),
    ('Байжанов Максат Ерболович',      'N00000030',  '1999-04-15', '+77021234530',  'maksat.b@gmail.com',    'Казахстан')
) AS v(full_name, doc, birth_date, phone, email, country)
JOIN countries c ON c.name = v.country;

INSERT INTO loyalty_cards (guest_id, card_number, level, bonus_points, issue_date)
SELECT g.id, v.card_number, v.level, v.bonus_points, CURRENT_DATE - v.days_ago
FROM (VALUES
    ('N00000001',  'QH-000001', 'gold',     12500, 900),
    ('4510123456', 'QH-000002', 'silver',    2300, 400),
    ('N00000003',  'QH-000003', 'silver',     800, 120),
    ('N00000006',  'QH-000004', 'silver',    1500, 200),
    ('4509111222', 'QH-000005', 'platinum', 48000, 1500),
    ('N00000014',  'QH-000006', 'gold',      9100, 1100),
    ('N00000017',  'QH-000007', 'silver',       0, 10),
    ('4520333444', 'QH-000008', 'gold',      7600, 650),
    ('N00000025',  'QH-000009', 'silver',     450, 60),
    ('4511999000', 'QH-000010', 'platinum', 31000, 1300)
) AS v(doc, card_number, level, bonus_points, days_ago)
JOIN guests g ON g.document_number = v.doc;


-- ── Бронирования ─────────────────────────────────────────────────────────────

-- Одна строка плана = бронь + её номер. start — сдвиг заезда от сегодняшнего дня,
-- nights — ночей, lead — за сколько дней до заезда оформлена бронь.
-- Статус согласован с датами: «завершена» — выезд уже был, «заселён» — гость сейчас в отеле,
-- «новая»/«подтверждена» — заезд впереди. Отменённые брони держат номер неактивным
-- (is_active = false), поэтому EXCLUDE-ограничение booking_rooms их не учитывает.
--
-- Сначала бронь, потом её номер — в одном операторе: data-modifying CTE вставляет
-- bookings и через RETURNING отдаёт id для booking_rooms.
WITH plan AS (
    SELECT g.id AS guest_id, h.id AS hotel_id, r.id AS room_id,
           CURRENT_DATE + v.start            AS check_in,
           CURRENT_DATE + v.start + v.nights AS check_out,
           v.status, v.source, v.cancel_reason, v.guests, v.lead
    FROM (VALUES
        -- Qonaq Astana Center
        ('N00000001',  'Qonaq Astana Center', '201', -30, 4, 'завершена',    'сайт',      NULL,                         1, 14),
        ('4510123456', 'Qonaq Astana Center', '202', -21, 5, 'завершена',    'агрегатор', NULL,                         2, 30),
        ('N00000003',  'Qonaq Astana Center', '304', -14, 3, 'завершена',    'телефон',   NULL,                         1,  5),
        ('4512654321', 'Qonaq Astana Center', '201',  -3, 6, 'заселён',      'стойка',    NULL,                         1,  0),
        ('AA1234567',  'Qonaq Astana Center', '405',  -1, 4, 'заселён',      'сайт',      NULL,                         2, 10),
        ('N00000006',  'Qonaq Astana Center', '506',  -2, 7, 'заселён',      'агрегатор', NULL,                         3, 21),
        ('N00000007',  'Qonaq Astana Center', '202',   2, 3, 'подтверждена', 'телефон',   NULL,                         2,  7),
        ('4509111222', 'Qonaq Astana Center', '203',   5, 4, 'подтверждена', 'сайт',      NULL,                         1, 12),
        ('AB7654321',  'Qonaq Astana Center', '201',   4, 2, 'новая',        'сайт',      NULL,                         1,  3),
        ('ID1234567',  'Qonaq Astana Center', '307',  10, 5, 'новая',        'агрегатор', NULL,                         2,  6),
        ('N00000014',  'Qonaq Astana Center', '504',  14, 3, 'новая',        'телефон',   NULL,                         2,  4),
        ('N00000015',  'Qonaq Astana Center', '202',   7, 3, 'отменена',     'сайт',      'Изменились планы поездки',   1,  9),
        ('4515777888', 'Qonaq Astana Center', '203', -10, 2, 'отменена',     'телефон',   'Отмена по звонку гостя',     1, 15),
        ('N00000017',  'Qonaq Astana Center', '302',  20, 4, 'подтверждена', 'сайт',      NULL,                         2, 25),
        ('N00000016',  'Qonaq Astana Center', '208',   1, 1, 'новая',        'стойка',    NULL,                         1,  1),
        -- Qonaq Almaty Park
        ('N00000019',  'Qonaq Almaty Park',   '201', -40, 3, 'завершена',    'сайт',      NULL,                         1, 10),
        ('N00000020',  'Qonaq Almaty Park',   '305', -12, 5, 'завершена',    'телефон',   NULL,                         3,  8),
        ('E12345678',  'Qonaq Almaty Park',   '404',  -7, 3, 'завершена',    'агрегатор', NULL,                         2, 20),
        ('N00000021',  'Qonaq Almaty Park',   '201',  -4, 5, 'заселён',      'сайт',      NULL,                         2,  6),
        ('4520333444', 'Qonaq Almaty Park',   '402',   0, 3, 'заселён',      'агрегатор', NULL,                         1,  2),
        ('N00000023',  'Qonaq Almaty Park',   '506',   3, 7, 'подтверждена', 'телефон',   NULL,                         2, 18),
        ('4518555666', 'Qonaq Almaty Park',   '402',   3, 2, 'подтверждена', 'стойка',    NULL,                         1,  2),
        ('AC1122334',  'Qonaq Almaty Park',   '201',   6, 2, 'новая',        'сайт',      NULL,                         1,  4),
        ('N00000025',  'Qonaq Almaty Park',   '303',   9, 4, 'новая',        'агрегатор', NULL,                         3,  7),
        ('N00000027',  'Qonaq Almaty Park',   '209',  12, 3, 'отменена',     'агрегатор', 'Нашли другой отель',         2, 10),
        -- Qonaq Shymkent
        ('N00000028',  'Qonaq Shymkent',      '201', -18, 2, 'завершена',    'стойка',    NULL,                         1,  0),
        ('4511999000', 'Qonaq Shymkent',      '302',  -6, 4, 'завершена',    'сайт',      NULL,                         2, 16),
        ('N00000030',  'Qonaq Shymkent',      '302',  -1, 3, 'заселён',      'телефон',   NULL,                         1,  3),
        ('U12345678',  'Qonaq Shymkent',      '405',   2, 5, 'подтверждена', 'агрегатор', NULL,                         2, 30),
        ('C01X00T47',  'Qonaq Shymkent',      '206',   8, 6, 'новая',        'сайт',      NULL,                         1, 11),
        ('N00000001',  'Qonaq Shymkent',      '501',  25, 2, 'новая',        'телефон',   NULL,                         1,  2),
        ('4510123456', 'Qonaq Shymkent',      '201',  15, 3, 'отменена',     'сайт',      'Перенос командировки',       1,  8),
        -- столицы
        ('4518555666', 'Qonaq Moscow Arbat',  '404', -9, 3, 'завершена',    'сайт',      NULL,                         2, 20),
        ('4509111222', 'Qonaq Moscow Arbat',  '506', -1, 5, 'заселён',      'агрегатор', NULL,                         1, 14),
        ('N00000014',  'Qonaq Moscow Arbat',  '202',  6, 4, 'подтверждена', 'телефон',   NULL,                         2, 10),
        ('AC1122334',  'Qonaq Tashkent City', '303', -2, 4, 'заселён',      'стойка',    NULL,                         2,  1),
        ('AB7654321',  'Qonaq Tashkent City', '201', 11, 3, 'новая',        'сайт',      NULL,                         1,  5),
        ('ID1234567',  'Qonaq Bishkek',       '305',  4, 2, 'новая',        'телефон',   NULL,                         3,  3),
        ('N00000016',  'Qonaq Bishkek',       '208',-15, 2, 'завершена',    'агрегатор', NULL,                         1,  9)
    ) AS v(doc, hotel, room, start, nights, status, source, cancel_reason, guests, lead)
    JOIN guests g ON g.document_number = v.doc
    JOIN hotels h ON h.name = v.hotel
    JOIN rooms r  ON r.hotel_id = h.id AND r.room_number = v.room
),
new_bookings AS (
    INSERT INTO bookings (guest_id, hotel_id, booking_date, check_in_plan, check_out_plan,
                          status, source, cancel_reason)
    SELECT guest_id, hotel_id, (check_in - lead) + time '10:30', check_in, check_out,
           status, source, cancel_reason
    FROM plan
    RETURNING id, guest_id, hotel_id, check_in_plan
)
INSERT INTO booking_rooms (booking_id, room_id, date_from, date_to, guests_count, is_active)
SELECT nb.id, p.room_id, p.check_in, p.check_out, p.guests, p.status <> 'отменена'
FROM new_bookings nb
JOIN plan p ON p.guest_id = nb.guest_id
           AND p.hotel_id = nb.hotel_id
           AND p.check_in = nb.check_in_plan;

-- Фактические заселения: у «завершена» — заезд и выезд, у «заселён» — только заезд.
-- Оформляет первый работающий администратор ресепшена отеля.
INSERT INTO stays (booking_room_id, actual_check_in, actual_check_out, employee_id)
SELECT br.id,
       br.date_from + time '14:00',
       CASE WHEN b.status = 'завершена' THEN br.date_to + time '11:30' END,
       (SELECT e.id
        FROM employees e
        JOIN positions p ON p.id = e.position_id
        WHERE e.hotel_id = b.hotel_id
          AND p.name = 'администратор ресепшена'
          AND e.dismissal_date IS NULL
        ORDER BY e.id
        LIMIT 1)
FROM booking_rooms br
JOIN bookings b ON b.id = br.booking_id
WHERE br.is_active AND b.status IN ('заселён', 'завершена');

-- Номера, где гость сейчас живёт, помечаем занятыми
UPDATE rooms r SET status = 'занят'
WHERE EXISTS (
    SELECT 1
    FROM stays s
    JOIN booking_rooms br ON br.id = s.booking_room_id
    WHERE br.room_id = r.id AND s.actual_check_out IS NULL
);


-- ── Услуги ───────────────────────────────────────────────────────────────────

INSERT INTO service_categories (name) VALUES
    ('Питание'), ('СПА и отдых'), ('Трансфер'), ('Прачечная');

-- Каждая услуга есть во всех отелях; в пятизвёздочном дороже, в трёхзвёздочном дешевле
INSERT INTO services (category_id, hotel_id, name, price, unit)
SELECT c.id, h.id, v.name,
       round(v.price * CASE h.stars WHEN 5 THEN 1.3 WHEN 4 THEN 1.0 ELSE 0.8 END, -2),
       v.unit
FROM (VALUES
    ('Питание',     'Завтрак «шведский стол»', 6000,  'чел.'),
    ('Питание',     'Ужин в ресторане',        12000, 'чел.'),
    ('Питание',     'Доставка в номер',        2500,  'заказ'),
    ('СПА и отдых', 'Сауна',                   8000,  'час'),
    ('СПА и отдых', 'Массаж классический',     15000, 'сеанс'),
    ('Трансфер',    'Трансфер из аэропорта',   9000,  'поездка'),
    ('Прачечная',   'Стирка и глажка',         2000,  'кг')
) AS v(category, name, price, unit)
JOIN service_categories c ON c.name = v.category
CROSS JOIN hotels h;

-- Заказы услуг по заселениям; price_at_order фиксирует цену на момент заказа
INSERT INTO service_orders (booking_id, service_id, employee_id, order_date, quantity, price_at_order)
SELECT b.id, s.id, st.employee_id, o.order_date, o.quantity, s.price
FROM bookings b
JOIN booking_rooms br ON br.booking_id = b.id AND br.is_active
JOIN stays st         ON st.booking_room_id = br.id
CROSS JOIN LATERAL (VALUES
    -- завтрак на всех гостей на каждую ночь
    ('Завтрак «шведский стол»', br.date_from + time '20:00', (br.date_to - br.date_from) * br.guests_count, true),
    -- трансфер для тех, кто бронировал онлайн
    ('Трансфер из аэропорта',   br.date_from + time '12:30', 1, b.source IN ('сайт', 'агрегатор')),
    -- стирка для длинных проживаний
    ('Стирка и глажка',         br.date_from + time '20:30', 3, br.date_to - br.date_from >= 4)
) AS o(service_name, order_date, quantity, wanted)
JOIN services s ON s.hotel_id = b.hotel_id AND s.name = o.service_name
WHERE o.wanted;
