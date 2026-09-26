-- db/seed.sql — демо-данные Stage 1
-- Запускать после schema.sql на чистой БД:
--   psql -h localhost -U postgres -d gosti -f db/seed.sql

INSERT INTO countries (name) VALUES ('Казахстан');

INSERT INTO cities (country_id, name)
SELECT c.id, v.name
FROM countries c,
     (VALUES ('Астана'), ('Алматы'), ('Шымкент')) AS v(name)
WHERE c.name = 'Казахстан';

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
    ('Qonaq Shymkent',      'Шымкент', 'ул. Тауке хана, 18',   '3', '+7 700 000 00 03')
) AS v(name, city_name, address, stars, phone)
JOIN cities c ON c.name = v.city_name;

-- 40 номеров на отель: этажи 2–5, номера 1–10, типы по кругу
INSERT INTO rooms (hotel_id, room_type_id, room_number, floor)
SELECT h.id, ((n - 1) % 6) + 1, (f * 100 + n)::text, f
FROM hotels h,
     generate_series(2, 5) AS f,
     generate_series(1, 10) AS n;
