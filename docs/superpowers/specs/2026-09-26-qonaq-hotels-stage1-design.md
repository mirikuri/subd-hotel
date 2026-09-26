# Qonaq Hotels — Stage 1 Design Spec
*2026-09-26 · Stage 1 only · ASP.NET Core 8 Razor Pages*

---

## 1. Intent and Scope

Build Stage 1 (minimum viable) of the Qonaq Hotels website: a public landing page, an employee login, and an admin stub. This is a university project. Stages 2 and 3 are out of scope here.

**Success criteria (from ТЗ §11):**

- `hotel_schema.sql` + seed run on a clean DB without errors.
- Homepage (`/`) opens with all 9 blocks and texts from ТЗ §6.
- Counters, room cards, and hotel cards pull from DB; adding a hotel in DB appears on the page without code changes.
- "Войти" button is visible at 360, 768, and 1920 px and links to `/login`.
- Search form blocks check-out ≤ check-in in browser and on server.
- `admin` with correct password reaches `/admin` and sees their name and role.
- Wrong password gives "Неверный логин или пароль"; login `ADMIN` works identically to `admin`.
- After 5 wrong passwords the account is locked for 15 min; `users.locked_until` is set.
- `is_active = false` blocks login even with a correct password.
- `/admin` without login redirects to `/login`; after login it redirects back.
- After "Выйти", the browser back button does not show the admin panel.
- `users.password_hash` contains no plaintext passwords; no password appears in code.

---

## 2. Tech Stack

| Layer | Choice |
|---|---|
| Backend | ASP.NET Core 8, Razor Pages |
| ORM | EF Core 8 + Npgsql, Database First (no migrations ever) |
| DB | PostgreSQL 16, schema `db/schema.sql` |
| Auth | Cookie Authentication (built-in), no ASP.NET Identity tables |
| Password hashing | `PasswordHasher<User>` (PBKDF2 with salt) |
| Frontend | Razor templates + CSS, Bootstrap 5, no SPA frameworks |
| Rate limiting | ASP.NET Core built-in `RateLimiter` (fixed-window) |
| Language | Russian UI |

---

## 3. Project Layout

```
QonaqHotels/
├── Data/
│   ├── HotelDbContext.cs          ← scaffold output (--no-onconfiguring)
│   └── Seeder.cs                  ← C# admin seeder
├── Models/                        ← scaffold output: all entity classes
├── Pages/
│   ├── Shared/
│   │   ├── _Layout.cshtml         ← public layout (sticky header + footer)
│   │   ├── _AdminLayout.cshtml    ← admin layout (sidebar + topbar)
│   │   └── _ValidationScriptsPartial.cshtml
│   ├── Admin/
│   │   ├── _ViewStart.cshtml      ← sets Layout = "_AdminLayout" for all admin pages
│   │   └── Index.cshtml + .cshtml.cs
│   ├── Index.cshtml + .cshtml.cs  ← homepage
│   ├── Search.cshtml              ← Stage 1 stub ("скоро заработает")
│   ├── Login.cshtml + .cshtml.cs
│   ├── Logout.cshtml + .cshtml.cs ← OnGet → redirect /; OnPost → SignOut + antiforgery
│   ├── AccessDenied.cshtml        ← 403 page in _Layout
│   └── Error.cshtml               ← used by UseStatusCodePagesWithReExecute("/Error/{0}")
├── wwwroot/
│   ├── css/site.css
│   ├── js/site.js
│   └── img/                       ← placeholder.webp + named images
├── Program.cs
├── appsettings.json               ← placeholder connection string, no secrets
└── appsettings.Development.json   ← .gitignored, real connection string in dev
```

---

## 4. Schema and Scaffolding

### 4.1 Schema fixes

Four changes to `db/schema.sql` before scaffolding:

**Fix 1 — Ordering bug (known):** move the `positions` table definition above `employees`.

**Fix 2 — Add `countries` and `cities` tables** (required by ТЗ seed SQL and homepage city dropdown):

```sql
CREATE TABLE countries (
    id   int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name varchar(80) NOT NULL UNIQUE
);

CREATE TABLE cities (
    id          int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    country_id  int NOT NULL REFERENCES countries(id) ON DELETE RESTRICT,
    name        varchar(100) NOT NULL,
    UNIQUE (country_id, name)
);
```

Place both tables before `hotels`.

**Fix 3 — `hotels.city` → `hotels.city_id`:** replace the `city varchar(100) NOT NULL` column with `city_id int NOT NULL REFERENCES cities(id) ON DELETE RESTRICT`.

**Fix 4 — `guests.country` → `guests.country_id`:** replace `country varchar(60)` with `country_id int REFERENCES countries(id) ON DELETE RESTRICT`.

**Fix 5 — Add lockout/audit fields to `users`:**

```sql
failed_attempts  int NOT NULL DEFAULT 0,
locked_until     timestamptz,
last_login_at    timestamptz
```

**Fix 6 — Extend `users.role` CHECK** to only include roles the ТЗ uses (keep the existing values so nothing breaks):
No change needed — existing `CHECK (role IN ('админ', 'ресепшен', 'бухгалтер', 'управляющий арендой'))` is compatible; Stage 1 only creates the `админ` role.

**Updated seed SQL** (ТЗ §6, adjusted for new schema):

```sql
INSERT INTO countries (name) VALUES ('Казахстан') ON CONFLICT DO NOTHING;

INSERT INTO cities (country_id, name)
SELECT c.id, v.name
FROM (VALUES ('Астана'), ('Алматы'), ('Шымкент')) AS v(name)
JOIN countries c ON c.name = 'Казахстан'
ON CONFLICT DO NOTHING;

INSERT INTO room_types (name, capacity, description) VALUES
    ('Стандарт',    2, '...'),
    ... ; -- unchanged from ТЗ §6

INSERT INTO hotels (name, city_id, address, stars, phone)
SELECT v.name, c.id, v.address, v.stars, v.phone
FROM (VALUES
    ('Qonaq Astana Center', 'Астана',  'пр. Мангилик Ел, 55', 5::smallint, '+7 700 000 00 01'),
    ('Qonaq Almaty Park',   'Алматы',  'пр. Абая, 120',       4::smallint, '+7 700 000 00 02'),
    ('Qonaq Shymkent',      'Шымкент', 'ул. Тауке хана, 18',  3::smallint, '+7 700 000 00 03')
) AS v(name, city, address, stars, phone)
JOIN cities c ON c.name = v.city;

-- rooms insert unchanged
```

### 4.2 Scaffold command

After applying the fixed schema + seed to a clean `gosti` DB:

```bash
dotnet ef dbcontext scaffold \
  "Host=localhost;Database=gosti;Username=postgres" \
  Npgsql.EntityFrameworkCore.PostgreSQL \
  --output-dir Models \
  --context-dir Data \
  --context HotelDbContext \
  --no-onconfiguring \
  --data-annotations
```

All tables are scaffolded. Stage 1 uses only `hotels`, `cities`, `room_types`, `users`, `employees`. The rest are present but unused in Stage 1.

### 4.3 Connection string

- Dev: `appsettings.Development.json` (not committed) or user-secrets.
- Prod: environment variable `ConnectionStrings__DefaultConnection`.
- `appsettings.json` contains a placeholder string — `"Host=localhost;Database=gosti;Username=gosti_app;Password=CHANGE_ME"`.
- The application connects as `gosti_app`, a PostgreSQL role with SELECT/INSERT/UPDATE/DELETE only (not a superuser).

### 4.4 room_is_free() (Stage 2, referenced here for completeness)

Called via EF parameterised raw SQL — never string concatenation:

```csharp
context.Database.SqlQuery<bool>(
    $"SELECT room_is_free({roomId}, {checkIn}, {checkOut})"
)
```

Npgsql maps interpolated `FormattableString` parameters safely.

### 4.5 No EF migrations

The schema is managed entirely by `db/schema.sql`. `dotnet ef migrations` is never used. Re-scaffold if the schema changes.

---

## 5. Homepage — Page Model

File: `Pages/Index.cshtml.cs`

**Four DB queries, all `AsNoTracking()`:**

| # | Data | EF call |
|---|---|---|
| 1 | Hotel count, room count, city count | Three `.CountAsync()` calls (batched by EF) |
| 2 | Room types (first 6 by id) | `room_types.OrderBy(r => r.Id).Take(6).ToListAsync()` |
| 3 | Hotels with city | `hotels.Include(h => h.City).OrderBy(h => h.City.Name).ToListAsync()` |
| 4 | Cities for search dropdown | `cities.OrderBy(c => c.Name).ToListAsync()` |

**Image fallback logic** (in `OnGetAsync`, not in Razor):

```csharp
string RoomImage(int id) =>
    System.IO.File.Exists(Path.Combine(_env.WebRootPath, $"img/room-{id}.webp"))
        ? $"/img/room-{id}.webp"
        : "/img/placeholder.webp";
```

Same pattern for hotels.

**Empty-table behavior:** if a list is empty, the block is hidden via `@if (Model.Hotels.Any())` in Razor. The page never throws on an empty table.

---

## 6. Login — Page Model

File: `Pages/Login.cshtml.cs`

### GET
If user is already authenticated → `RedirectToPage("/Admin/Index")`.

### POST (antiforgery token required)

Validation (model-level): login required, max 50 chars; password required.

Server logic (in order):

1. Case-insensitive user lookup:
   ```csharp
   var user = await _db.Users
       .Include(u => u.Employee)
       .FirstOrDefaultAsync(u => u.Login.ToLower() == model.Login.ToLower());
   ```
2. **Timing-safe check:** always call `PasswordHasher.VerifyHashedPassword`. If user is null, verify against a dummy hash to prevent timing-based enumeration.
3. If `user.LockedUntil > DateTime.UtcNow` → error "Слишком много попыток. Попробуйте через {N} мин."
4. If password fails: increment `failed_attempts`. On 5th failure: set `locked_until = UtcNow + 15 min`, reset `failed_attempts = 0`. Save. Return "Неверный логин или пароль."
5. If `!user.IsActive` or `employee.DismissalDate <= DateOnly.FromDateTime(DateTime.UtcNow)` → "Учётная запись отключена. Обратитесь к администратору."
6. Password correct: clear `failed_attempts`, clear `locked_until`, set `last_login_at = UtcNow`. If `SuccessRehashNeeded` → update `password_hash`. Save.
7. Create claims principal with: `NameIdentifier` = user.Id, `Name` = employee full name, `Role` = user.Role, custom claim `hotel_id` = employee.HotelId.
8. `HttpContext.SignInAsync(...)` with `AuthenticationProperties`:
   - Without "remember me": `IsPersistent = false`, `ExpiresUtc = UtcNow + 8h`.
   - With "remember me": `IsPersistent = true`, `ExpiresUtc = UtcNow + 7 days`.
9. Redirect to `ReturnUrl` (validated with `Url.IsLocalUrl`) or `/Admin`.

### Error display
Error message in `TempData["LoginError"]`, rendered as a red alert above the submit button. Login value is preserved in `Input.Login`; password field is cleared.

---

## 7. Logout

File: `Pages/Logout.cshtml.cs`

- `OnGet` → `RedirectToPage("/Index")`.
- `OnPost` → `await HttpContext.SignOutAsync()` → `RedirectToPage("/Index")`.
- The form in `_Layout.cshtml` (or admin topbar) submits POST with antiforgery token.
- No GET-based logout link anywhere.

---

## 8. Admin Stub

File: `Pages/Admin/Index.cshtml.cs`

Reads `User.Identity.Name` (employee full name) and `User.FindFirst("role")?.Value` from claims. Displays "Здравствуйте, {Name} ({role})".

The left sidebar in `_AdminLayout.cshtml` lists all Stage 2/3 sections as disabled links with `(скоро)`:
- Шахматка, Брони, Заселение и выселение, Гости, Поиск номера, Заказы услуг, Справочники, Отчёты, Пользователи, Журнал.

---

## 9. Authorization Setup (Program.cs)

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => {
        options.Cookie.Name = "qonaq.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/AccessDenied";
    });

builder.Services.AddAuthorization(options => {
    options.AddPolicy("RequireReceptionist", p =>
        p.RequireRole("ресепшен", "админ"));
    options.AddPolicy("RequireAdmin", p =>
        p.RequireRole("админ"));
});

// ...

app.MapRazorPages();
```

The entire `/Admin` folder is protected via `AddRazorPagesOptions` in `Program.cs`:

```csharp
builder.Services.AddRazorPages(options => {
    options.Conventions.AuthorizeFolder("/Admin");
    // Role-specific pages in Stage 2/3:
    // options.Conventions.AuthorizePage("/Admin/Users", "RequireAdmin");
});
```

Individual admin PageModel classes in Stage 2/3 may also add `[Authorize(Policy = "RequireAdmin")]` as a belt-and-suspenders check.

**Security headers middleware** (custom or `NWebsec`):
```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: strict-origin-when-cross-origin
Cache-Control: no-store  ← on /Admin/* responses only
```

**Rate limiting:** fixed-window policy, 10 POST `/login` requests per minute per IP:

```csharp
builder.Services.AddRateLimiter(o =>
    o.AddFixedWindowLimiter("login", opts => {
        opts.Window = TimeSpan.FromMinutes(1);
        opts.PermitLimit = 10;
    }));
// Applied to Login.cshtml.cs via [EnableRateLimiting("login")] on OnPost
```

---

## 10. Seeder (Data/Seeder.cs)

Called from `Program.cs` after `app.Build()`, before `app.Run()`.

```
if (await db.Users.AnyAsync()) return;  // already seeded

var hotel = await db.Hotels.OrderBy(h => h.Id).FirstOrDefaultAsync();
if (hotel == null) {
    logger.LogWarning("Seeder: no hotels found — cannot create admin user. " +
                      "Run the seed SQL from ТЗ §6 first.");
    return;
}

var password = Environment.GetEnvironmentVariable("ADMIN_INITIAL_PASSWORD")
    ?? throw new InvalidOperationException(
        "ADMIN_INITIAL_PASSWORD environment variable is not set.");

// Find or create the "администратор" position (not guaranteed to exist in Stage 1 seed)
var position = await db.Positions.FirstOrDefaultAsync(p => p.Name == "администратор");
if (position == null) {
    position = new Position { Name = "администратор" };
    db.Positions.Add(position);
    await db.SaveChangesAsync();
}

// Fields come from the scaffolded Employee model; exact column names confirmed at scaffold time
var employee = new Employee {
    FirstName = "Администратор",
    LastName = "Системы",    // or FullName, depending on scaffold output
    HotelId = hotel.Id,
    PositionId = position.Id
};
db.Employees.Add(employee);
await db.SaveChangesAsync();

var hasher = new PasswordHasher<User>();
var user = new User { Login = "admin", Role = "админ", IsActive = true };
user.PasswordHash = hasher.HashPassword(user, password);
user.EmployeeId = employee.Id;
db.Users.Add(user);
await db.SaveChangesAsync();
```

No password or hash is logged at any level.

---

## 11. Frontend JavaScript (wwwroot/js/site.js)

Five behaviors, no external dependencies:

| Behavior | Implementation detail |
|---|---|
| Header on scroll | `IntersectionObserver` on a 1px sentinel at 80px from top; toggles `.header--scrolled` class which changes background, text color, and adds box-shadow |
| Show/hide password | Click on eye icon toggles `input.type` between `"password"` and `"text"` |
| Caps Lock warning | `keydown` on password field: `event.getModifierState("CapsLock")` shows/hides a `<small class="text-warning">` |
| Date validation | `change` on check-in field: sets `checkOut.min = checkIn.value + 1 day`; prevents same-day selection |
| Login spinner | `submit` on login form: disables submit button, replaces text with Bootstrap spinner HTML |

---

## 12. Visual Style Summary

| Token | Value |
|---|---|
| Dark (header, footer) | `#1E2A3A` |
| Accent (buttons, stars, links) | `#C8A165` |
| Page background | `#F7F5F1` |
| Body text | `#222222` |
| Error | `#C0392B` |
| Heading font | Montserrat 600 (Google Fonts, with Cyrillic) |
| Body font | Inter 400, 16px base (Google Fonts, with Cyrillic) |

Header: 72px desktop / 60px mobile, `position: sticky`. Transparent with white text over the hero; white background with dark text after scroll (`.header--scrolled`). Burger menu below 768px.

Bootstrap 5 grid for all multi-column blocks. Breakpoints: 360, 768, 1200, 1920 px.

Images: WebP format. Hero ≤400 KB, all others ≤150 KB. `loading="lazy"` on everything except hero. Meaningful `alt` text in Russian. Falls back to `placeholder.webp` if named file is missing.

---

## 13. Out of Scope (Stage 1)

- Pages `/rooms`, `/hotels`, `/contacts`, `/loyalty`, `/search` (real functionality)
- All `/admin/*` sub-pages except `/admin` stub
- Finances, rental, reports, audit log
- Kazakh and English localisation
- Excel export
