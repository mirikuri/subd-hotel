# Поиск номера, заявки с сайта, справочники, цены в USD — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Гость находит свободный номер на сайте и отправляет заявку; админка получает пять справочников с правами по ролям; все цены в долларах.

**Architecture:** Razor Pages + Npgsql, SQL прямо в коде, целостность — ограничениями PostgreSQL. Общий SQL «свободный номер / цена за период» живёт в одном классе `Data/RoomSearch.cs`, его используют и сайт, и админка. Заявки с сайта — сервис `Services/SiteBookingService.cs`, гарантия от двойной брони — существующее EXCLUDE-ограничение `booking_rooms`.

**Tech Stack:** .NET 8, ASP.NET Core Razor Pages, Npgsql 8, PostgreSQL 17, xUnit, Microsoft.AspNetCore.Mvc.Testing.

**Spec:** `docs/superpowers/specs/2026-11-10-room-search-and-catalogs-design.md`

## Global Constraints

- Коммиты без строки `Co-Authored-By` (правило пользователя для этого репозитория).
- В рабочей копии есть незакоммиченные правки пользователя: `Pages/Shared/_Layout.cshtml`, `Pages/Shared/_AdminLayout.cshtml`, `wwwroot/css/site.css`, `wwwroot/js/searchable-select.js`. Никогда не откатывать их (`git checkout`, `git stash`, перезапись файла целиком). Перед первым коммитом, который трогает эти файлы, спросить пользователя, закоммитить ли сначала его правки.
- Схема только в `db/schema.sql` (в `CREATE TABLE`, без `ALTER`), данные в `db/seed.sql`; база пересоздаётся `db/reset.sh -y gosti_test`.
- Новые ограничения — с явными именами из спеки; каждое имя добавить в `DbErrors.ByConstraint` с русским сообщением.
- Все SQL-параметры через `cmd.Parameters`, без конкатенации значений.
- Все цены — USD, выводятся только через `Money.Usd(decimal)`. В интерфейсе не должно остаться `₸` и «тг».
- «Сегодня» — только `Clock.Today`.
- Роли: `админ`, `ресепшен`, `бухгалтер` (`Models.Roles`), гость — `гость` (`Roles.Guest`, добавляется в задаче 1).
- Ограничения сайта: выезд > заезд; заезд ≥ сегодня; выезд ≤ сегодня + 1 год; ночей ≤ 30; гостей 1–6; не больше 3 заявок «новая» + «сайт» с будущим заездом у гостя; до 3 попыток назначить номер.
- Базовые цены за ночь, USD: Стандарт 50, Комфорт 65, Полулюкс 85, Люкс 130, Семейный 105, Бизнес-люкс 160; коэффициенты сезонов прежние (Лето 1.25, Зима 1.10, Весна 0.95, Осень 1.00), `round(..., 0)`.
- Цены услуг, USD: завтрак 12, ужин 25, доставка в номер 5, сауна 16, массаж 30, трансфер 18, стирка 4; коэффициенты звёздности прежние (5★ 1.3, 4★ 1.0, иначе 0.8), `round(..., 0)`.
- Тесты: `db/reset.sh -y gosti_test` один раз, затем `dotnet test` из корня репозитория. Тесты, которые пишут в БД, создают свои данные через `TestHotel` (задача 3) и удаляют их за собой.

## Review Focus

1. Неактивный гость (`guests.is_active = false`) с живой cookie отправляет заявку — отказ, бронь не создаётся (тест в задаче 5).
2. Подменённые параметры заявки: отель не в продаже, тип без свободных номеров, `guests` больше вместимости — страница подтверждения отвечает 404/сообщением, а не 500 (тест в задаче 6).
3. `returnUrl` на чужой сайт (`https://evil.example/`) после входа — редирект на `/` (тест в задаче 6).
4. Ресепшен отправляет POST полной формы номера с другим типом и этажом — меняется только статус (тест в задаче 9).
5. Бухгалтер отправляет POST создания сезона или удаления услуги — 403 (тест в задачах 10, 11).

---

## Файлы

| Файл | Ответственность |
|---|---|
| `db/schema.sql`, `db/seed.sql` | новые столбцы, `amenities`, `room_type_amenities`, USD |
| `src/QonaqHotels/Data/DbErrors.cs` | сообщения для новых ограничений |
| `src/QonaqHotels/Models/DomainValues.cs` | `Roles.Guest`, `RoomView`, `BedType` |
| `src/QonaqHotels/Models/Money.cs` | формат USD |
| `src/QonaqHotels/Program.cs` | политики `RequireStaff`, `RequireGuest`, `RequireAccountant` |
| `src/QonaqHotels/Data/RoomSearch.cs` | SQL свободных номеров, цены за период, минимальные цены |
| `src/QonaqHotels/Services/SiteBookingService.cs` | заявка, список и отмена броней гостя |
| `src/QonaqHotels/Pages/Search.cshtml(.cs)` | поиск на сайте |
| `src/QonaqHotels/Pages/Booking/Request.cshtml(.cs)`, `My.cshtml(.cs)` | подтверждение заявки, «Мои брони» |
| `src/QonaqHotels/Pages/Admin/Catalogs/**` | оглавление и пять справочников |
| `tests/QonaqHotels.Tests/AppFactory.cs` | `WebApplicationFactory` с тестовой аутентификацией |
| `tests/QonaqHotels.Tests/TestHotel.cs` | изолированные тестовые данные |

---

### Task 1: Схема, seed, доллары, сообщения ограничений

**Files:**
- Modify: `db/schema.sql`, `db/seed.sql`, `src/QonaqHotels/Data/DbErrors.cs`, `src/QonaqHotels/Models/DomainValues.cs`, `src/QonaqHotels/Models/Hotel.cs`, `src/QonaqHotels/Models/RoomType.cs`, `src/QonaqHotels/Models/Room.cs`, `src/QonaqHotels/Models/Service.cs`
- Create: `src/QonaqHotels/Models/Amenity.cs`, `src/QonaqHotels/Models/Money.cs`
- Test: `tests/QonaqHotels.Tests/SchemaTests.cs`, `tests/QonaqHotels.Tests/MoneyTests.cs`

**Interfaces:**
- Produces:
  - Столбцы и таблицы точно как в разделе «Изменения схемы» спеки; EXCLUDE в `seasons` получает имя `seasons_no_overlap`.
  - `Roles.Guest = "гость"`; `RoomView.All = ["город","парк","двор","горы"]`; `BedType.All = ["односпальная","двуспальная","две односпальные","king-size"]` (`string[]`, `static readonly`).
  - `Models.Money.Usd(decimal amount) -> string`.
  - `COMMENT ON COLUMN rates.price_per_night / services.price / service_orders.price_at_order IS 'USD'`.

- [ ] **Step 1: Тесты.** `MoneyTests`: `Usd(1250m) == "$1,250"`, `Usd(12.5m) == "$12.50"`, `Usd(0m) == "$0"`, `Usd(1000000m) == "$1,000,000"`. `SchemaTests` (через `TestDb.Create()`):
  - `Rates_AreUsd`: цена «Стандарт» в осеннем сезоне текущего года = 50, «Бизнес-люкс» летом = 200.
  - `Services_AreUsd`: «Завтрак «шведский стол»» в 4★ отеле = 12.
  - `NewColumns_Seeded`: у всех `room_types` заполнены `area_m2` и `bed_type`; у каждого типа ≥ 3 удобств; у всех отелей `check_in_time = '14:00'`.
  - `SeasonOverlap_HasNamedConstraint`: вставка сезона, пересекающего существующий, падает с `PostgresException`, `ConstraintName == "seasons_no_overlap"`, и `DbErrors.Describe(ex) == "Сезон пересекается с другим сезоном."`.
- [ ] **Step 2:** `dotnet test --filter "FullyQualifiedName~MoneyTests|FullyQualifiedName~SchemaTests"` — FAIL (нет `Money`, нет столбцов).
- [ ] **Step 3:** Правки `schema.sql` и `seed.sql` по спеке и Global Constraints; `seed.sql` заполняет площади (Стандарт 22, Комфорт 28, Полулюкс 38, Люкс 55, Семейный 48, Бизнес-люкс 60 м²), кровати, 8–10 удобств со значками Bootstrap Icons, вид из окна (этажи 2–3 — `двор`/`город` по чётности номера, 4–5 — `парк`/`горы`), e-mail отелей. `DbErrors.ByConstraint`: `hotels_name_key`, `room_types_name_key`, `amenities_name_key`, `service_categories_name_key`, `room_types_area_check`, `room_types_bed_type_check`, `rooms_view_check`, `seasons_no_overlap`, `rooms_hotel_id_room_number_key` («Номер с таким названием в отеле уже есть.»), `rates_price_per_night_check` («Цена должна быть больше нуля.»). `Money.Usd` — `CultureInfo.InvariantCulture`, формат `"$#,0"` для целых, `"$#,0.00"` иначе. Модели получают новые свойства.
- [ ] **Step 4:** `db/reset.sh -y gosti_test && dotnet test` — всё PASS (старые тесты тоже).
- [ ] **Step 5:** Commit `feat(db): удобства, детали отелей и номеров, цены в USD`.

### Task 2: Роли и доступ к админке

**Files:**
- Modify: `src/QonaqHotels/Program.cs`, `src/QonaqHotels/Pages/Admin/Index.cshtml.cs`, `src/QonaqHotels/Pages/Shared/_AdminLayout.cshtml`
- Create: `tests/QonaqHotels.Tests/AppFactory.cs`
- Test: `tests/QonaqHotels.Tests/AccessTests.cs`

**Interfaces:**
- Produces:
  - Политики: `RequireStaff` (админ/ресепшен/бухгалтер), `RequireGuest` (`Roles.Guest`), `RequireAccountant` (бухгалтер/админ); существующие `RequireReceptionist`, `RequireAdmin` не меняются. `AuthorizeFolder("/Admin", "RequireStaff")`.
  - `AppFactory : WebApplicationFactory<Program>`: строка подключения = `TestDb.ConnectionString`, env `ADMIN_INITIAL_PASSWORD=Test12345`, тестовая схема аутентификации `Test`, ставшая схемой по умолчанию для authenticate/challenge/forbid. Обработчик читает заголовок `X-Test-Role` (роль) и `X-Test-Guest` (id гостя → claim `NameIdentifier = "guest:<id>"`); без заголовка — `NoResult` (challenge → 401, forbid → 403).
  - `AppFactory.Client(string? role, int? guestId = null) -> HttpClient` с `BaseAddress = https://localhost`, `AllowAutoRedirect = false`.

- [ ] **Step 1: Тесты** `AccessTests` (`[Theory]`):
  - анонимный `/Admin` → 401; гость `/Admin` и `/Admin/Bookings` → 403;
  - бухгалтер `/Admin/Chess` и `/Admin/Bookings` → 403, `/Admin` → 200;
  - ресепшен `/Admin/Chess` → 200, `/Admin/Employees` → 403;
  - админ `/Admin/Employees` → 200.
  - `Sidebar_ByRole`: HTML `/Admin` для бухгалтера не содержит `href="/Admin/Chess"` и `href="/Admin/Employees"`; ни для одной роли нет текста «Поиск номера».
- [ ] **Step 2:** `dotnet test --filter AccessTests` — FAIL (гость получает 200).
- [ ] **Step 3:** Политики и `AuthorizeFolder`; меню по `User.IsInRole` (Шахматка, Брони, Гости — ресепшен/админ; Сотрудники, Пользователи, Должности — админ; «Справочники» — все сотрудники, ссылка `/Admin/Catalogs`); пункт «Поиск номера» удалить. Правка `_AdminLayout.cshtml` — только точечными Edit поверх правок пользователя.
- [ ] **Step 4:** `dotnet test` — PASS.
- [ ] **Step 5:** Commit `fix(auth): админка только для сотрудников, меню по ролям` (сначала уточнить у пользователя про его правки в `_AdminLayout.cshtml`).

### Task 3: Общий SQL поиска и цены (`RoomSearch`)

**Files:**
- Create: `src/QonaqHotels/Data/RoomSearch.cs`, `tests/QonaqHotels.Tests/TestHotel.cs`
- Modify: `src/QonaqHotels/Pages/Admin/Bookings/BookingQueries.cs` (использовать `RoomSearch.FreeRoomCondition`)
- Test: `tests/QonaqHotels.Tests/RoomSearchTests.cs`

**Interfaces:**
- Produces (`QonaqHotels.Data`):
  - `public const string RoomSearch.FreeRoomCondition` — фрагмент `WHERE`, ссылается на алиас `r` (rooms) и параметры `@dateFrom`, `@dateTo`, `@excludeBookingId`, `@repair`; условие — ровно текущее из `BookingQueries.AvailableRoomsSql`.
  - `record RoomOffer(int HotelId, string HotelName, short Stars, string Address, int RoomTypeId, string RoomTypeName, short Capacity, decimal? AreaM2, string? BedType, string? Description, IReadOnlyList<(string Name, string? Icon)> Amenities, int FreeRooms, int Nights, decimal TotalPrice)` с вычисляемым `decimal AvgPerNight => Math.Round(TotalPrice / Nights, 2)`.
  - `static string? RoomSearch.Validate(DateOnly from, DateOnly to, int guests, DateOnly today)` — `null` или сообщение: «Дата выезда должна быть позже даты заезда.», «Нельзя бронировать прошедшие даты.», «Бронирование возможно максимум за год вперёд.», «Не больше 30 ночей.», «Гостей — от 1 до 6.».
  - `static Task<List<RoomOffer>> RoomSearch.SearchAsync(NpgsqlConnection conn, int cityId, DateOnly from, DateOnly to, int guests)` — сортировка по отелю, затем цене.
  - `static Task<RoomOffer?> RoomSearch.FindOfferAsync(NpgsqlConnection conn, int hotelId, int roomTypeId, DateOnly from, DateOnly to, int guests)`.
  - `static Task<decimal?> RoomSearch.PeriodPriceAsync(NpgsqlConnection conn, int roomTypeId, DateOnly from, DateOnly to)` — `null`, если хоть на одну ночь нет тарифа.
  - `static Task<int?> RoomSearch.FirstFreeRoomAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int hotelId, int roomTypeId, DateOnly from, DateOnly to)` — `ORDER BY floor NULLS LAST, room_number LIMIT 1`.
  - `static Task<Dictionary<int, decimal>> RoomSearch.MinNightlyRatesAsync(NpgsqlConnection conn, DateOnly today)` — ключ `room_type_id`, сезоны, пересекающие `[today, today + 1 год]`.
  - Цена за период — один SQL: `generate_series(@dateFrom, @dateTo - 1, '1 day')` × `seasons` по `d BETWEEN date_start AND date_end` × `rates`; тип исключается, если `count(rates) < ночей`.
- Produces (тесты): `sealed class TestHotel : IAsyncDisposable` — `static Task<TestHotel> CreateAsync(Db db, int rooms = 1, string roomType = "Люкс")` создаёт отдельный город в первой стране, отель, `rooms` номеров заданного типа и гостя с логином; свойства `CityId`, `HotelId`, `RoomTypeId`, `IReadOnlyList<int> RoomIds`, `GuestId`; `Task<int> BookAsync(int roomId, DateOnly from, DateOnly to, string status = "новая", string source = "стойка")`; `Task SetGuestActiveAsync(bool)`; `DisposeAsync` удаляет `booking_rooms`, `bookings`, `rooms`, `hotel`, `guest`, `city`.

- [ ] **Step 1: Тесты** `RoomSearchTests` (даты относительно `Clock.Today`, заезд через 10 дней):
  - `Search_HidesTypeWhenAllRoomsBooked`: 1 номер, бронь на [d, d+3) → `SearchAsync` на [d+1, d+2) пусто; на [d+3, d+5) — 1 предложение, `FreeRooms == 1`.
  - `Search_IgnoresCancelledBookingRooms`: бронь со статусом «отменена» (`is_active = false`) не мешает.
  - `Search_FiltersByCapacity`: тип «Люкс» (вместимость 2) при `guests = 3` не возвращается.
  - `Search_SkipsRepairRooms`: единственный номер в статусе «ремонт» → пусто.
  - `PeriodPrice_AcrossSeasonBoundary`: «Стандарт», ночи 30 ноября и 1 декабря (ближайшие, где 30 ноября позже сегодня) → 50 + 55 = 105.
  - `PeriodPrice_NullWithoutRate`: удалить в транзакции тариф одного сезона → `null` (откат в конце).
  - `Validate_Messages`: каждое из пяти сообщений на своих входах; `null` на валидных.
  - `AdminAvailableRooms_StillWork`: `BookingQueries.LoadAvailableRoomsAsync` на тестовом отеле возвращает свободный номер и не возвращает занятый.
- [ ] **Step 2:** `dotnet test --filter RoomSearchTests` — FAIL (нет класса).
- [ ] **Step 3:** Реализация `RoomSearch` и `TestHotel`; `BookingQueries.AvailableRoomsSql` подставляет `RoomSearch.FreeRoomCondition`.
- [ ] **Step 4:** `dotnet test` — PASS.
- [ ] **Step 5:** Commit `feat: общий SQL поиска свободных номеров и цены за период`.

### Task 4: Поиск на сайте и цены на главной

**Files:**
- Modify: `src/QonaqHotels/Pages/Search.cshtml`, `Search.cshtml.cs`, `src/QonaqHotels/Pages/Index.cshtml`, `Index.cshtml.cs`
- Test: `tests/QonaqHotels.Tests/SearchPageTests.cs`, `tests/QonaqHotels.Tests/IndexPageTests.cs`

**Interfaces:**
- Consumes: `RoomSearch.Validate`, `SearchAsync`, `MinNightlyRatesAsync`, `Money.Usd`, `TestHotel`.
- Produces: `SearchModel(Db db)` со свойствами `[BindProperty(SupportsGet)] int? City, DateOnly? From, DateOnly? To, int Guests = 2`, `List<City> Cities`, `List<RoomOffer> Offers`, `string? Error`; `IndexModel.MinPrices : Dictionary<int, decimal>`. Ссылка «Забронировать»: `/Booking/Request?hotelId=&roomTypeId=&from=yyyy-MM-dd&to=&guests=`.

- [ ] **Step 1: Тесты:**
  - `SearchPage_ReturnsOffersForTestCity` — `SearchModel` с параметрами `TestHotel.CityId` и свободными датами: `Offers.Count == 1`, `Error == null`.
  - `SearchPage_InvalidDates_ShowsError` — `To <= From` → `Error == "Дата выезда должна быть позже даты заезда."`, `Offers` пуст.
  - `SearchPage_NoParams_NoError` — без параметров только форма.
  - `IndexPage_MinPrices` — `MinPrices[id «Стандарт»] == 48` (весна: 50 × 0.95 = 47.5, `round` в PostgreSQL → 48; весна всегда попадает в ближайшие 12 месяцев).
  - HTTP через `AppFactory`: анонимный `GET /Search?city=...` → 200, тело содержит `$`, не содержит `₸`.
- [ ] **Step 2:** `dotnet test --filter "SearchPageTests|IndexPageTests"` — FAIL.
- [ ] **Step 3:** `SearchModel.OnGetAsync`, разметка карточек по спеке («осталось N номеров» при `FreeRooms <= 3`, «$X / ночь · $Y за N ночей»). Форма на главной (`#search-form`): поля `city` (select из `Cities`), `from`, `to`, `guests` (1–6); та же форма вверху `/Search` со значениями. Карточки типов на главной: «от $N за ночь», площадь, значки удобств.
- [ ] **Step 4:** `dotnet test` — PASS; `dotnet run --project src/QonaqHotels`, открыть `/` и `/Search` — видны цены в $.
- [ ] **Step 5:** Commit `feat(site): поиск номеров и цены от $ на главной`.

### Task 5: Сервис заявок с сайта

**Files:**
- Create: `src/QonaqHotels/Services/SiteBookingService.cs`
- Modify: `src/QonaqHotels/Program.cs` (регистрация `AddScoped<SiteBookingService>()`)
- Test: `tests/QonaqHotels.Tests/SiteBookingServiceTests.cs`

**Interfaces:**
- Consumes: `RoomSearch.Validate`, `FirstFreeRoomAsync`, `PeriodPriceAsync`, `DbErrors`.
- Produces:
  - `enum SiteBookingOutcome { Success, Invalid, GuestDisabled, TooManyRequests, NoRooms }`
  - `record SiteBookingResult(SiteBookingOutcome Outcome, int? BookingId, string? Message)`
  - `SiteBookingService(Db db)`:
    - `Task<SiteBookingResult> SubmitAsync(int guestId, int hotelId, int roomTypeId, DateOnly from, DateOnly to, int guests, string? comment)`
    - `Task<List<GuestBookingRow>> ListAsync(int guestId)`; `record GuestBookingRow(int Id, string HotelName, string RoomTypeName, string? RoomNumber, DateOnly From, DateOnly To, int Guests, decimal? Total, string Status)`
    - `Task<bool> CancelAsync(int guestId, int bookingId)`
  - Сообщения: `TooManyRequests` — «У вас уже 3 необработанные заявки.»; `NoRooms` — «Свободных номеров этого типа на эти даты не осталось.»; `GuestDisabled` — «Учётная запись отключена.»; `Success` — «Заявка №{id} отправлена, администратор свяжется с вами.».
  - Алгоритм `SubmitAsync`: валидация → транзакция → `SELECT is_active FROM guests WHERE id = @g FOR UPDATE` (блокировка строки гостя сериализует его заявки для счётчика) → счётчик → цикл до 3 попыток: `tx.SaveAsync("pick")`, `FirstFreeRoomAsync`, `INSERT bookings ... RETURNING id`, `INSERT booking_rooms`; на `23P01` — `tx.RollbackAsync("pick")` и следующая попытка; номер `null` → `NoRooms`. `comment` обрезается `Trim()`, пустой → `NULL`, длиннее 500 → `Invalid`.
  - `CancelAsync` — два `UPDATE` из спеки в одной транзакции, `cancel_reason = 'Отменено гостем'`.

- [ ] **Step 1: Тесты** (`TestHotel`, заезд через 10 дней):
  - `Submit_CreatesNewSiteBookingWithRoom`: `Success`; в БД `status = 'новая'`, `source = 'сайт'`, одна активная `booking_rooms` с `guests_count`.
  - `Submit_Concurrent_LastRoom`: 1 номер, `Task.WhenAll` двух `SubmitAsync` разных гостей → ровно один `Success`, второй `NoRooms`; в БД одна активная строка.
  - `Submit_Concurrent_TwoRooms`: 2 номера, две параллельные заявки → обе `Success`, разные `room_id`.
  - `Submit_FourthRequest_TooMany`: при 4 номерах четвёртая заявка того же гостя → `TooManyRequests`.
  - `Submit_DisabledGuest`: `SetGuestActiveAsync(false)` → `GuestDisabled`, броней нет.
  - `Submit_InvalidDates`: прошедшая дата → `Invalid`, сообщение из `Validate`.
  - `Cancel_OwnNew_True` — после отмены статус «отменена», `is_active = false`, номер снова находится `SearchAsync`.
  - `Cancel_Foreign_False`, `Cancel_Confirmed_False` (бронь через `BookAsync(..., "подтверждена")`).
  - `List_OnlyOwn`: бронь другого гостя не попадает в список.
- [ ] **Step 2:** `dotnet test --filter SiteBookingServiceTests` — FAIL.
- [ ] **Step 3:** Реализация по Interfaces.
- [ ] **Step 4:** `dotnet test` — PASS (параллельные тесты прогнать 3 раза подряд — стабильно).
- [ ] **Step 5:** Commit `feat: заявки на бронь с сайта`.

### Task 6: Страницы заявки и «Мои брони»

**Files:**
- Create: `src/QonaqHotels/Pages/Booking/Request.cshtml(.cs)`, `src/QonaqHotels/Pages/Booking/My.cshtml(.cs)`
- Modify: `src/QonaqHotels/Program.cs`, `src/QonaqHotels/Pages/RegisterLogin.cshtml.cs` (+ `.cshtml`: скрытое поле `returnUrl`), `src/QonaqHotels/Pages/Shared/_Layout.cshtml` (пункт «Мои брони» для `Roles.Guest`)
- Test: `tests/QonaqHotels.Tests/BookingPagesTests.cs`

**Interfaces:**
- Consumes: `SiteBookingService`, `RoomSearch.FindOfferAsync`, `AppFactory`.
- Produces: обе страницы `[Authorize(Policy = "RequireGuest")]`; id гостя — хелпер `static int? GuestClaims.GuestId(ClaimsPrincipal)` в `Services/GuestClaims.cs` (разбирает `NameIdentifier` вида `guest:<id>`). В `Program.cs` cookie `Events.OnRedirectToLogin`: путь начинается с `/Booking` → редирект на `/Register-Login?returnUrl=<исходный путь и query>`, иначе прежний `/Login`.

- [ ] **Step 1: Тесты** (через `AppFactory`, кроме отмеченных):
  - `Request_Anonymous_Challenged` — 401 в тестовой схеме; отдельный юнит-тест `OnRedirectToLogin` не нужен — проверить вручную в Step 4.
  - `Request_Staff_Forbidden` — роль `ресепшен` → 403.
  - `Request_Get_ShowsTotal` — гость `TestHotel.GuestId`, валидные параметры → 200, тело содержит `Money.Usd(total)`.
  - `Request_Get_NoOffer_404` — `roomTypeId` несуществующий или `guests = 6` для «Люкс» → 404.
  - `Request_Post_RedirectsToMy` — POST (с антифорджери-токеном из GET) → 302 на `/Booking/My`, бронь в БД.
  - `My_ShowsOnlyOwn` и `My_CancelForeign_NoChange`.
  - `RegisterLogin_ReturnUrl`: юнит-тест хелпера `RegisterLoginModel.SafeReturnUrl(string? url, IUrlHelper)` — `/Booking/Request?x=1` сохраняется, `https://evil.example/` и `//evil.example` → `/`.
- [ ] **Step 2:** `dotnet test --filter BookingPagesTests` — FAIL.
- [ ] **Step 3:** Реализация. `Request`: GET — `FindOfferAsync`, `null` → `NotFound()`; POST — `SubmitAsync`, при не-`Success` — та же страница с `Message`. `My`: таблица, кнопка «Отменить» только для «новая», `TempData` для сообщения об успехе. `RegisterLogin`: `returnUrl` в query/скрытом поле, после входа и регистрации `LocalRedirect(SafeReturnUrl(...))`.
- [ ] **Step 4:** `dotnet test` — PASS. Вручную: аноним жмёт «Забронировать» → `/Register-Login` → вход → назад на подтверждение → отправка → «Мои брони» → бронь видна в `/Admin/Chess` как «новая».
- [ ] **Step 5:** Commit `feat(site): подтверждение заявки и «Мои брони»`.

### Task 7: Оглавление справочников и «Отели»

**Files:**
- Create: `src/QonaqHotels/Pages/Admin/Catalogs/Index.cshtml(.cs)`, `Catalogs/Hotels/{Index,Create,Edit,Delete}.cshtml(.cs)`, `Catalogs/Hotels/_HotelsRows.cshtml`
- Modify: `src/QonaqHotels/Pages/Admin/AdminLists.cs` (`Cities`, `RoomTypes`, `ServiceCategories` запросы)
- Test: `tests/QonaqHotels.Tests/CatalogHotelsTests.cs`

**Interfaces:**
- Produces: шаблон CRUD для задач 8–11 — как `Admin/Positions`: `Index` с `FilterBarModel` и `OnGetSearchAsync`, partial строк, `Create/Edit` с `[BindProperty]` и DataAnnotations по длинам столбцов, ошибки `PostgresException` через `DbErrors.Describe/DescribeDelete`. Доступ: `Index` — `RequireStaff` (папка), `Create/Edit/Delete` — `RequireAdmin`. Каскад страна → город в формах — `OnGetCitiesAsync(int countryId)` как в `Admin/Bookings/Index.cshtml.cs`.

- [ ] **Step 1: Тесты:** ресепшен `GET /Admin/Catalogs/Hotels` → 200, `GET .../Create` → 403; админ создаёт отель через `CreateModel` (прямой вызов с `TestDb`) → строка в БД с `check_in_time`; дубль названия → ошибка формы «Отель с таким названием уже есть.»; удаление отеля с номерами → сообщение «Нельзя удалить: на запись ссылаются номера.», строка осталась. Созданные строки удалить в конце теста.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Реализация; оглавление — пять карточек со ссылками; добавить `hotels_name_key` в `DbErrors` с текстом выше, если в задаче 1 текст другой.
- [ ] **Step 4:** `dotnet test` — PASS.
- [ ] **Step 5:** Commit `feat(admin): справочник отелей`.

### Task 8: Типы номеров и удобства

**Files:**
- Create: `Pages/Admin/Catalogs/RoomTypes/{Index,Create,Edit,Delete}.cshtml(.cs)`, `_RoomTypesRows.cshtml`, `Pages/Admin/Catalogs/Amenities/Index.cshtml(.cs)` (список + добавить/переименовать/удалить на одной странице)
- Test: `tests/QonaqHotels.Tests/CatalogRoomTypesTests.cs`

**Interfaces:**
- Produces: `[BindProperty] int[] AmenityIds` в `Create/Edit`; сохранение типа и `room_type_amenities` в одной транзакции (`DELETE ... WHERE room_type_id = @id AND amenity_id <> ALL(@ids)`, затем `INSERT ... SELECT unnest(@ids) ON CONFLICT DO NOTHING`). Всё — `RequireAdmin`, кроме `Index` типов.

- [ ] **Step 1: Тесты:** правка набора удобств заменяет его ровно на выбранный; `bed_type` вне списка → сообщение из `DbErrors` (`room_types_bed_type_check`); удаление удобства, привязанного к типу → «Нельзя удалить: …», удаление типа с номерами → запрет.
- [ ] **Step 2:** FAIL. **Step 3:** реализация. **Step 4:** `dotnet test` PASS.
- [ ] **Step 5:** Commit `feat(admin): справочник типов номеров и удобств`.

### Task 9: Номера

**Files:**
- Create: `Pages/Admin/Catalogs/Rooms/{Index,Create,Edit,Delete}.cshtml(.cs)`, `_RoomsRows.cshtml`
- Test: `tests/QonaqHotels.Tests/CatalogRoomsTests.cs`

**Interfaces:**
- Produces: `Index` с фильтрами отель/этаж/тип/статус. `EditModel` — `[Authorize(Policy = "RequireReceptionist")]`; в `OnPostAsync`: если `!User.IsInRole(Roles.Admin)` — `UPDATE rooms SET status = @s WHERE id = @id` (статус только `свободен`/`уборка`/`ремонт`), иначе полный `UPDATE`. В разметке для не-админа остальные поля `disabled`. `Create`/`Delete` — `RequireAdmin`.

- [ ] **Step 1: Тесты:** ресепшен POST `Edit` с новым статусом, типом и этажом → в БД изменился только статус (Review Focus 4); ресепшен `GET Create` → 403; дубль номера в отеле → «Номер с таким названием в отеле уже есть.»; удаление номера с бронью → запрет.
- [ ] **Step 2:** FAIL. **Step 3:** реализация. **Step 4:** PASS.
- [ ] **Step 5:** Commit `feat(admin): справочник номеров, статус для ресепшена`.

### Task 10: Сезоны и тарифы

**Files:**
- Create: `Pages/Admin/Catalogs/Seasons/{Index,Create,Edit,Delete}.cshtml(.cs)`
- Test: `tests/QonaqHotels.Tests/CatalogSeasonsTests.cs`

**Interfaces:**
- Produces: `Index` — список сезонов. `Create`/`Delete` — `RequireAdmin`. `Edit` — `RequireAccountant`: таблица «тип → цена, $» (`[BindProperty] Dictionary<int, decimal?> Prices`); название и даты сезона редактируемы только админом (как в задаче 9: не-админ — только цены). Сохранение одной транзакцией: для каждой пары цена `null` → `DELETE FROM rates`, иначе `INSERT ... ON CONFLICT (room_type_id, season_id) DO UPDATE SET price_per_night = EXCLUDED.price_per_night`. `Delete` в одной транзакции удаляет тарифы сезона, затем сам сезон (моё решение: кроме `rates`, на сезон ничего не ссылается).

- [ ] **Step 1: Тесты:** бухгалтер меняет цену → новая цена в БД и `RoomSearch.PeriodPriceAsync` её видит (откатить изменение в конце); бухгалтер POST `Create` → 403 (Review Focus 5); пересекающийся сезон → «Сезон пересекается с другим сезоном.»; цена 0 → «Цена должна быть больше нуля.»; бухгалтер POST `Edit` с другими датами → даты не изменились.
- [ ] **Step 2:** FAIL. **Step 3:** реализация. **Step 4:** PASS.
- [ ] **Step 5:** Commit `feat(admin): сезоны и тарифы`.

### Task 11: Услуги и категории

**Files:**
- Create: `Pages/Admin/Catalogs/Services/{Index,Create,Edit,Delete}.cshtml(.cs)`, `_ServicesRows.cshtml`, `Pages/Admin/Catalogs/ServiceCategories/Index.cshtml(.cs)`
- Test: `tests/QonaqHotels.Tests/CatalogServicesTests.cs`

**Interfaces:**
- Produces: `Index` с фильтрами отель/категория/«продаётся». `Edit` — `RequireAccountant`; не-админ меняет только `price` и `is_active`. `Create`, `Delete`, категории — `RequireAdmin`. Удаление услуги из `service_orders` → сообщение «Нельзя удалить: на запись ссылаются заказы услуг. Снимите услугу с продажи.» (расширить `DescribeDelete` для `service_orders`).

- [ ] **Step 1: Тесты:** бухгалтер меняет цену и `is_active`, название не меняется; бухгалтер POST `Delete` → 403 (Review Focus 5); удаление услуги с заказами → сообщение выше; дубль категории → «Категория с таким названием уже есть.».
- [ ] **Step 2:** FAIL. **Step 3:** реализация. **Step 4:** PASS.
- [ ] **Step 5:** Commit `feat(admin): справочник услуг и категорий`.

### Task 12: Финальная проверка денег и доступа

**Files:**
- Modify: любые `.cshtml`, где цена выводится не через `Money.Usd`
- Test: `tests/QonaqHotels.Tests/AccessTests.cs` (расширить)

- [ ] **Step 1: Тест** `NoTenge_OnPages`: для набора страниц (`/`, `/Search?…`, `/Admin/Catalogs/Seasons/Edit?id=…`, `/Admin/Catalogs/Services`) под подходящей ролью тело не содержит `₸` и `тг`. Расширить `AccessTests` матрицей из спеки для `/Admin/Catalogs/*` (ресепшен/бухгалтер/админ × Index/Create).
- [ ] **Step 2:** `grep -rnE "₸|тг\b|KZT" src/QonaqHotels` — пусто.
- [ ] **Step 3:** `db/reset.sh -y gosti_test && dotnet test` — PASS. `db/reset.sh -y gosti`, `ADMIN_INITIAL_PASSWORD=... dotnet run --project src/QonaqHotels`, ручной сценарий из раздела «Проверка» спеки.
- [ ] **Step 4:** Commit `test: доступ к справочникам и отсутствие тенге`.
