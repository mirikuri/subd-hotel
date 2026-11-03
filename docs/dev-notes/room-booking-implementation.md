# Шахматка + комнаты в бронях: как это работает

## Что сделано

Реализованы три связанных фичи, которые закрывают полный цикл: **выделение дней на шахматке → создание брони с конкретной комнатой → сохранение в БД**.

До этого таблица `booking_rooms` существовала в схеме, но UI её полностью игнорировал: формы броней не имели поля «Номер», а шахматка не позволяла создавать новые брони.

---

## 1. Шахматка: выделение диапазона и переход к созданию брони

### Как данные попадают в ячейки

Каждая пустая ячейка таблицы (`_Grid.cshtml`) теперь несёт три `data`-атрибута:

```html
<td class="chess-empty ..."
    data-room-id="@room.RoomId"
    data-room-num="@room.RoomNumber"
    data-date="@day.ToString("yyyy-MM-dd")">
</td>
```

Это позволяет JavaScript'у знать, какой комнате и какому дню соответствует каждая ячейка, не обращаясь к серверу.

### Логика двух кликов (admin-chess.js)

JS хранит объект состояния выделения:

```js
var sel = { roomId: null, roomNum: null, dateFrom: null, dateTo: null };
```

**Клик 1** на пустую ячейку:
- Записывает `roomId`, `roomNum`, `dateFrom = дата ячейки`, `dateTo = null`
- Добавляет класс `chess-sel-start` на ячейку (золотой контур)
- Показывает action bar: `«Комната 205 · выберите конец периода»`

**Клик 2** на пустую ячейку той же строки:
- Определяет диапазон: `from = min(dateFrom, date)`, `to = max(dateFrom, date)`
- Добавляет `chess-sel-range` всем ячейкам диапазона
- Action bar меняется на: `«Комната 205 · 1 – 5 ноя [Создать бронь] [✕]»`

**Клик на другую строку** или навигация — сбрасывает выделение (`clearSel()`).

### URL для перехода к форме

Кнопка «Создать бронь» формирует URL:

```
/Admin/Bookings/Create?roomId=5&hotelId=1&dateFrom=2026-11-01&dateTo=2026-11-06
```

> **Важно про `dateTo`:** значение = последняя выделенная ячейка + 1 день. Это потому, что дата выезда в системе **исключительная**: если гость занимает комнаты 1–5 ноября, `date_to = 6 ноября`. Шахматка показывает ячейку занятой при условии `date_from ≤ день < date_to`.

---

## 2. Форма «Создать бронь»: поле «Номер»

### GET — предзаполнение из шахматки

`Create.cshtml.cs` принимает query-параметры:

```csharp
public async Task OnGetAsync(int? roomId, int? hotelId, DateOnly? dateFrom, DateOnly? dateTo)
```

Если переданы `hotelId` + даты — сразу вызывается `LoadAvailableRoomsAsync`, и выпадающий список рендерится сервером с уже готовыми вариантами. `roomId` из URL становится pre-selected.

### AJAX — динамическое обновление

Когда пользователь меняет отель или даты вручную, JS отправляет запрос:

```
GET /Admin/Bookings/Create?handler=AvailableRooms&hotelId=1&dateFrom=2026-11-01&dateTo=2026-11-06
```

Сервер возвращает JSON `[{value: "5", label: "205 · Стандарт (эт. 2)"}, ...]`, JS перестраивает `<option>` в select.

### POST — запись в базу

```csharp
// 1. Вставить бронь, получить её id
INSERT INTO bookings (...) RETURNING id  →  bookingId

// 2. Если выбрана комната — привязать её
if (RoomId.HasValue)
    INSERT INTO booking_rooms (booking_id, room_id, date_from, date_to)
    VALUES (@bookingId, @roomId, @checkIn, @checkOut)
```

---

## 3. Форма «Изменить бронь»: поле «Номер»

### GET — загрузка текущей комнаты

Два отдельных запроса:

```sql
-- основной запрос брони, дополнен hotel_id
SELECT b.id, ..., b.hotel_id FROM bookings b JOIN guests g ... JOIN hotels h ...

-- текущая назначенная комната
SELECT br.room_id FROM booking_rooms br
WHERE br.booking_id = @id AND br.is_active
LIMIT 1
```

Затем `LoadAvailableRoomsAsync` с `excludeBookingId = id` — чтобы текущая бронь не блокировала саму себя (подробнее в разделе про СУБД).

### POST — синхронизация

Логика намеренно простая — всегда перезаписывает:

```csharp
// Деактивировать старую запись (если была)
UPDATE booking_rooms SET is_active = false
WHERE booking_id = @id AND is_active

// Если выбрана новая комната — вставить
if (RoomId.HasValue)
    INSERT INTO booking_rooms (booking_id, room_id, date_from, date_to) ...
```

Это корректно обрабатывает все случаи: смена комнаты, смена дат при той же комнате, снятие комнаты, — без ветвлений `if roomChanged / if datesChanged`.

---

## 4. СУБД: как устроена защита от двойного бронирования

### Таблица `booking_rooms`

```sql
CREATE TABLE booking_rooms (
    id            int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    booking_id    int NOT NULL REFERENCES bookings(id) ON DELETE CASCADE,
    room_id       int NOT NULL REFERENCES rooms(id)    ON DELETE RESTRICT,
    date_from     date NOT NULL,
    date_to       date NOT NULL,
    guests_count  smallint NOT NULL DEFAULT 1,
    is_active     boolean  NOT NULL DEFAULT true,
    CHECK (date_to > date_from),
    EXCLUDE USING gist (
        room_id WITH =,
        daterange(date_from, date_to, '[)') WITH &&
    ) WHERE (is_active)
);
```

### EXCLUDE-ограничение с GiST-индексом

Это ключевая деталь. PostgreSQL не позволит вставить две строки, где:
- `room_id` одинаковый (`WITH =`)
- И диапазоны `[date_from, date_to)` пересекаются (`WITH &&`)
- При условии `is_active = true`

**Тип `daterange` и полуоткрытый интервал `[)`:**
- `[)` означает: левая граница включена, правая — нет
- Гость заезжает 1 ноября (этот день включён), выезжает 6 ноября (этот день свободен для следующего гостя)
- Два диапазона `[Nov1, Nov6)` и `[Nov5, Nov10)` пересекаются → `&&` = true → PostgreSQL откажет

**Оператор `&&` (overlaps):**
Два диапазона [A, B) и [C, D) пересекаются тогда и только тогда, когда `A < D AND C < B`. Именно эта формула используется и в SQL-запросе поиска свободных номеров.

**Условие `WHERE (is_active)`:**
Ограничение применяется только к активным записям. При деактивации (`is_active = false`) история сохраняется и не мешает новым вставкам. Это позволяет хранить аудит всех изменений без конфликтов.

Важно: защита от двойного бронирования работает **на уровне БД**, независимо от приложения. Даже если два запроса одновременно пройдут все проверки в коде, PostgreSQL откажет одному из них при INSERT с нарушением EXCLUDE.

### Запрос доступных номеров: формула перекрытия

```sql
SELECT r.id::text,
       r.room_number || ' · ' || rt.name ||
       CASE WHEN r.floor IS NOT NULL THEN ' (эт. ' || r.floor || ')' ELSE '' END
FROM rooms r
JOIN room_types rt ON rt.id = r.room_type_id
WHERE r.hotel_id = @hotelId
  AND r.status  != 'ремонт'
  AND NOT EXISTS (
      SELECT 1 FROM booking_rooms br
      JOIN bookings b ON b.id = br.booking_id
      WHERE br.room_id    = r.id
        AND br.is_active
        AND (@excludeId = 0 OR br.booking_id != @excludeId)
        AND br.date_to  > @dateFrom   -- ← чужой диапазон заканчивается после нашего начала
        AND br.date_from < @dateTo    -- ← чужой диапазон начинается до нашего конца
        AND b.status NOT IN ('отменена', 'завершена')
  )
ORDER BY r.floor, r.room_number
```

**Почему именно `br.date_to > @dateFrom AND br.date_from < @dateTo`:**

Стандартная формула пересечения интервалов [A, B) и [C, D):
```
пересекаются ⟺ A < D AND C < B
```
В нашем случае: `[br.date_from, br.date_to)` и `[@dateFrom, @dateTo)`:
```
br.date_from < @dateTo  AND  @dateFrom < br.date_to
```
Комната появится в результатах только если для неё **не существует** ни одной активной брони, пересекающейся с запрошенным периодом.

**Параметр `@excludeId`:**
При редактировании брони текущая запись `booking_rooms` с `booking_id = @id` уже существует и `is_active = true`. Без исключения она блокировала бы саму себя и комната не появлялась бы в списке доступных. `(@excludeId = 0 OR br.booking_id != @excludeId)` — если передан id редактируемой брони, её запись игнорируется при проверке.

### Семантика дат — сводная таблица

| Контекст | Значение `date_from` / `date_to` |
|----------|----------------------------------|
| `booking_rooms` | `date_from` — заезд (включительно), `date_to` — выезд (исключительно) |
| Шахматка: ячейка занята | `date_from ≤ день < date_to` |
| Выделение на шахматке | клик «Nov 1» + «Nov 5» → `dateFrom=Nov1, dateTo=Nov6` (+1 день) |
| `bookings.check_in_plan` | копируется в `booking_rooms.date_from` |
| `bookings.check_out_plan` | копируется в `booking_rooms.date_to` |

---

## 5. Изменённые файлы

| Файл | Что изменилось |
|------|----------------|
| `Pages/Admin/Chess/_Grid.cshtml` | `data-room-id`, `data-room-num`, `data-date` на пустых ячейках |
| `Pages/Admin/Chess/Index.cshtml` | `#chess-action-bar` (скрыт по умолчанию) |
| `wwwroot/js/admin-chess.js` | state machine двух кликов, action bar, redirect с параметрами |
| `wwwroot/css/admin.css` | стили `.chess-sel-start`, `.chess-sel-range`, `#chess-action-bar` |
| `Pages/Admin/Bookings/Create.cshtml.cs` | query-params в OnGet, `OnGetAvailableRoomsAsync` → JSON, INSERT booking_rooms |
| `Pages/Admin/Bookings/Create.cshtml` | select «Номер», pre-select из URL, AJAX-обновление |
| `Pages/Admin/Bookings/Edit.cshtml.cs` | загрузка текущей комнаты, `OnGetAvailableRoomsAsync` с excludeId, deactivate+insert |
| `Pages/Admin/Bookings/Edit.cshtml` | hidden `HotelId`, select «Номер» с pre-selected, AJAX-обновление по датам |
