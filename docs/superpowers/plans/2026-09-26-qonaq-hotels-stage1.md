# Qonaq Hotels Stage 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Stage 1 of the Qonaq Hotels website: public homepage (9 blocks, DB-driven), employee login with lockout, and admin stub — running on ASP.NET Core 8 + PostgreSQL.

**Architecture:** Database-first Razor Pages app. Schema in `db/schema.sql` is the single source of truth — no EF migrations ever. `db/seed.sql` populates demo data. EF Core models scaffolded into `src/QonaqHotels/Models/`. Login business logic lives in `LoginService` (injected into the PageModel) so it can be unit-tested without a live HTTP stack. Seeder runs at startup and creates the admin account from an env var.

**Tech Stack:** .NET 8 / ASP.NET Core 8 · Razor Pages · EF Core 8 + Npgsql · PostgreSQL 17 · Bootstrap 5 · xUnit · NixOS (dev shell via `shell.nix`)

**Spec:** `docs/superpowers/specs/2026-09-26-qonaq-hotels-stage1-design.md`

## Global Constraints

- .NET 8 only; no EF migrations — schema managed by `db/schema.sql` exclusively
- Cookie name `qonaq.auth`; `HttpOnly`, `Secure`, `SameSite=Lax`; 8 h sliding (7 d persistent with "remember me")
- `ADMIN_INITIAL_PASSWORD` env var — never hardcoded, never logged
- All user-facing copy in Russian (spec §6 texts verbatim)
- Bootstrap 5 allowed; no SPA frameworks
- All reads use `AsNoTracking()`; no string-concatenated SQL — EF or parameterised `FromSql` only
- Images WebP; hero ≤ 400 KB, others ≤ 150 KB; `loading="lazy"` on all except hero; fall back to `/img/placeholder.webp`
- Accent `#C8A165`, dark `#1E2A3A`, bg `#F7F5F1`, error `#C0392B`; Montserrat 600 / Inter 400 from Google Fonts

## Review Focus

1. **Login case-insensitivity** — `ADMIN`, `Admin`, `admin` must all resolve to the same user; the SQL `lower()` cast must be on both sides of the comparison, verified by a unit test.
2. **Timing-safe "user not found"** — the code must call `VerifyHashedPassword` even when no user row exists; a non-existent login must return the same generic message as a wrong password, with no short-circuit.
3. **Back-button cache after logout** — all `/Admin` responses must carry `Cache-Control: no-store`; verified by reading the response header in a test after `SignOut`.
4. **Server-side date validation on search** — client JS prevents bad dates, but `OnPostSearch` must re-validate `to > from` on the server even with JS disabled.
5. **Empty-table resilience on homepage** — if `room_types` or `hotels` has zero rows, `OnGetAsync` must complete without throwing and Razor must hide those sections cleanly.

---

## File Map

```
subdprojectGostinica/
├── db/
│   ├── schema.sql              ← modified (Task 1)
│   └── seed.sql                ← created  (Task 1)
├── shell.nix                   ← created  (Task 2)
├── .gitignore                  ← created  (Task 2)
├── QonaqHotels.sln             ← created  (Task 2)
├── src/QonaqHotels/
│   ├── QonaqHotels.csproj      ← created  (Task 2)
│   ├── Program.cs              ← created  (Task 4)
│   ├── appsettings.json        ← created  (Task 2)
│   ├── appsettings.Development.json  ← created (Task 2, gitignored)
│   ├── Data/
│   │   ├── HotelDbContext.cs   ← scaffold (Task 3)
│   │   └── Seeder.cs           ← created  (Task 5)
│   ├── Models/                 ← scaffold (Task 3)
│   ├── Services/
│   │   └── LoginService.cs     ← created  (Task 10)
│   ├── Pages/
│   │   ├── _ViewImports.cshtml ← created  (Task 2)
│   │   ├── _ViewStart.cshtml   ← created  (Task 2)
│   │   ├── Shared/
│   │   │   ├── _Layout.cshtml        ← created (Task 6)
│   │   │   └── _AdminLayout.cshtml   ← created (Task 8)
│   │   ├── Admin/
│   │   │   ├── _ViewStart.cshtml     ← created (Task 8)
│   │   │   └── Index.cshtml[.cs]     ← created (Task 11)
│   │   ├── Index.cshtml[.cs]   ← created (Task 9)
│   │   ├── Login.cshtml[.cs]   ← created (Task 10)
│   │   ├── Logout.cshtml[.cs]  ← created (Task 11)
│   │   ├── Search.cshtml       ← created (Task 11)
│   │   ├── AccessDenied.cshtml ← created (Task 11)
│   │   └── Error.cshtml[.cs]   ← created (Task 11)
│   └── wwwroot/
│       ├── css/site.css        ← created (Task 6)
│       ├── js/site.js          ← created (Task 7)
│       └── img/placeholder.webp ← created (Task 9)
└── tests/QonaqHotels.Tests/
    ├── QonaqHotels.Tests.csproj  ← created (Task 2)
    ├── LoginServiceTests.cs      ← created (Task 10)
    └── IndexPageTests.cs         ← created (Task 9)
```

---

### Task 1: Schema overhaul and seed data

**Files:**
- Modify: `db/schema.sql`
- Create: `db/seed.sql`

**What changes in schema.sql (six edits, in order):**

**Edit 1 — Add `countries` and `cities` before `hotels`:**
```sql
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
```

**Edit 2 — `hotels`: replace `city varchar(100) NOT NULL` with `city_id int NOT NULL REFERENCES cities(id) ON DELETE RESTRICT`:**
```sql
CREATE TABLE hotels (
    id      int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name    varchar(150) NOT NULL,
    city_id int NOT NULL REFERENCES cities(id) ON DELETE RESTRICT,
    address varchar(250) NOT NULL,
    stars   smallint NOT NULL CHECK (stars BETWEEN 1 AND 5),
    phone   varchar(20)
);
```

**Edit 3 — Move `positions` above `employees`** (cut the `positions` block at lines 81–86, paste it before the `employees` block at line 68).

**Edit 4 — `guests`: replace `country varchar(60)` with `country_id int REFERENCES countries(id) ON DELETE RESTRICT`:**
```sql
CREATE TABLE guests (
    id               int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    full_name        varchar(150) NOT NULL,
    document_number  varchar(40) NOT NULL UNIQUE,
    birth_date       date,
    phone            varchar(20),
    email            varchar(120),
    country_id       int REFERENCES countries(id) ON DELETE RESTRICT
);
```

**Edit 5 — Add lockout/audit fields to `users`:**
```sql
CREATE TABLE users (
    id              int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    employee_id     int NOT NULL UNIQUE REFERENCES employees(id) ON DELETE CASCADE,
    login           varchar(50) NOT NULL UNIQUE,
    password_hash   varchar(255) NOT NULL,
    role            varchar(30) NOT NULL
                    CHECK (role IN ('админ', 'ресепшен', 'бухгалтер', 'управляющий арендой')),
    is_active       boolean NOT NULL DEFAULT true,
    failed_attempts int NOT NULL DEFAULT 0,
    locked_until    timestamptz,
    last_login_at   timestamptz
);
```

- [ ] **Step 1: Apply edits 1–5 to `db/schema.sql`** using the DDL above. Use a text editor — make only the listed changes, do not touch other tables.

- [ ] **Step 2: Create `db/seed.sql`**

```sql
-- db/seed.sql — демо-данные Stage 1
-- Запускать после schema.sql на чистой БД

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

-- 40 комнат на отель: этажи 2–5, номера 1–10, типы по кругу
INSERT INTO rooms (hotel_id, room_type_id, room_number, floor)
SELECT h.id, ((n - 1) % 6) + 1, (f * 100 + n)::text, f
FROM hotels h,
     generate_series(2, 5) AS f,
     generate_series(1, 10) AS n;
```

- [ ] **Step 3: Drop and recreate DB, apply schema, apply seed**

```bash
psql -h localhost -U postgres \
  -c "DROP DATABASE IF EXISTS gosti" \
  -c "CREATE DATABASE gosti ENCODING 'UTF8' LC_COLLATE 'ru_RU.UTF-8' LC_CTYPE 'ru_RU.UTF-8' TEMPLATE template0"
psql -h localhost -U postgres -d gosti -f db/schema.sql
psql -h localhost -U postgres -d gosti -f db/seed.sql
```

- [ ] **Step 4: Verify**

```bash
psql -h localhost -U postgres -d gosti -c "
  SELECT 'countries' AS t, count(*) FROM countries
  UNION ALL SELECT 'cities', count(*) FROM cities
  UNION ALL SELECT 'hotels', count(*) FROM hotels
  UNION ALL SELECT 'room_types', count(*) FROM room_types
  UNION ALL SELECT 'rooms', count(*) FROM rooms;
"
```

Expected: countries=1, cities=3, hotels=3, room_types=6, rooms=120.

Also verify `users` table has the new columns:
```bash
psql -h localhost -U postgres -d gosti -c "\d users"
```
Expect to see `failed_attempts`, `locked_until`, `last_login_at` columns.

---

### Task 2: Dev environment, project scaffold, solution

**Files:**
- Create: `shell.nix`
- Create: `.gitignore`
- Create: `QonaqHotels.sln`
- Create: `src/QonaqHotels/QonaqHotels.csproj`
- Create: `src/QonaqHotels/appsettings.json`
- Create: `src/QonaqHotels/appsettings.Development.json`
- Create: `src/QonaqHotels/Pages/_ViewImports.cshtml`
- Create: `src/QonaqHotels/Pages/_ViewStart.cshtml`
- Create: `tests/QonaqHotels.Tests/QonaqHotels.Tests.csproj`

- [ ] **Step 1: Create `shell.nix` in project root**

```nix
{ pkgs ? import <nixpkgs> {} }:
pkgs.mkShell {
  buildInputs = [
    pkgs.dotnet-sdk_8
    pkgs.postgresql
  ];
  shellHook = ''
    export DOTNET_CLI_TELEMETRY_OPTOUT=1
  '';
}
```

Enter the dev shell: `nix-shell` (all subsequent commands run inside it).

- [ ] **Step 2: Enter shell and verify .NET**

```bash
nix-shell --run "dotnet --version"
```
Expected: `8.0.x`

- [ ] **Step 3: Create solution and projects**

```bash
cd /home/meta/Projects/univer/subdprojectGostinica

dotnet new sln -n QonaqHotels

mkdir -p src tests

dotnet new webapp -n QonaqHotels -o src/QonaqHotels --framework net8.0

dotnet new xunit -n QonaqHotels.Tests -o tests/QonaqHotels.Tests --framework net8.0

dotnet sln add src/QonaqHotels/QonaqHotels.csproj
dotnet sln add tests/QonaqHotels.Tests/QonaqHotels.Tests.csproj

dotnet add tests/QonaqHotels.Tests/QonaqHotels.Tests.csproj \
  reference src/QonaqHotels/QonaqHotels.csproj
```

- [ ] **Step 4: Add NuGet packages**

```bash
cd src/QonaqHotels

# EF Core + Npgsql
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.*
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.*

# EF Core tools (global, needed for scaffold)
dotnet tool install --global dotnet-ef --version 8.*

cd ../..

# Test packages
cd tests/QonaqHotels.Tests
dotnet add package Microsoft.EntityFrameworkCore.InMemory --version 8.*
dotnet add package Microsoft.AspNetCore.Mvc.Testing --version 8.*
dotnet add package Moq --version 4.*
cd ../..
```

- [ ] **Step 5: Edit `src/QonaqHotels/QonaqHotels.csproj`** — add `UserSecretsId` and `Nullable` setting:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UserSecretsId>qonaq-hotels-dev</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.*">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Create `src/QonaqHotels/appsettings.json`**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=gosti;Username=gosti_app;Password=CHANGE_ME"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 7: Create `src/QonaqHotels/appsettings.Development.json`** (actual dev connection, will be gitignored):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=gosti;Username=postgres;Password="
  }
}
```

- [ ] **Step 8: Create `.gitignore` in project root**

```
# .NET
obj/
bin/
*.user
.vs/
.vscode/settings.json

# Secrets
appsettings.Development.json
appsettings.*.json
!appsettings.json

# dotnet tools
.config/

# NixOS
result
```

- [ ] **Step 9: Create `src/QonaqHotels/Pages/_ViewImports.cshtml`**

```cshtml
@using QonaqHotels
@using QonaqHotels.Models
@namespace QonaqHotels.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

- [ ] **Step 10: Create `src/QonaqHotels/Pages/_ViewStart.cshtml`**

```cshtml
@{
    Layout = "_Layout";
}
```

- [ ] **Step 11: Verify build**

```bash
dotnet build QonaqHotels.sln
```
Expected: Build succeeded, 0 errors.

---

### Task 3: EF Core Database-First scaffold

**Files:**
- Create: `src/QonaqHotels/Data/HotelDbContext.cs` (scaffold output)
- Create: `src/QonaqHotels/Models/*.cs` (scaffold output, ~25 files)

- [ ] **Step 1: Run scaffold**

```bash
cd src/QonaqHotels

dotnet ef dbcontext scaffold \
  "Host=localhost;Database=gosti;Username=postgres" \
  Npgsql.EntityFrameworkCore.PostgreSQL \
  --output-dir Models \
  --context-dir Data \
  --context HotelDbContext \
  --no-onconfiguring \
  --data-annotations \
  --force
```

- [ ] **Step 2: Verify key entity shapes**

Check `Models/User.cs` contains:
```csharp
public int FailedAttempts { get; set; }
public DateTime? LockedUntil { get; set; }
public DateTime? LastLoginAt { get; set; }
```

Check `Models/Hotel.cs` contains:
```csharp
public int CityId { get; set; }
public virtual City City { get; set; } = null!;
```

Check `Models/Employee.cs` contains:
```csharp
public string FullName { get; set; } = null!;
public DateOnly HireDate { get; set; }
public DateOnly? DismissalDate { get; set; }
```

- [ ] **Step 3: Wire DbContext in Program.cs (temporary stub — Task 4 replaces it)**

Add to `src/QonaqHotels/Program.cs` temporarily so the project builds:
```csharp
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();
builder.Services.AddDbContext<HotelDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();
```

- [ ] **Step 4: Build to confirm scaffold compiles**

```bash
dotnet build QonaqHotels.sln
```

---

### Task 4: Program.cs — auth, policies, rate limiting, security headers

**Files:**
- Modify: `src/QonaqHotels/Program.cs` (full replacement)

- [ ] **Step 1: Write the full `Program.cs`**

```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Razor Pages: protect all /Admin pages
builder.Services.AddRazorPages(options => {
    options.Conventions.AuthorizeFolder("/Admin");
});

// EF Core + PostgreSQL
builder.Services.AddDbContext<HotelDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Login service
builder.Services.AddScoped<LoginService>();

// Cookie authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => {
        options.Cookie.Name        = "qonaq.auth";
        options.Cookie.HttpOnly    = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite    = SameSiteMode.Lax;
        options.SlidingExpiration  = true;
        options.ExpireTimeSpan     = TimeSpan.FromHours(8);
        options.LoginPath          = "/Login";
        options.AccessDeniedPath   = "/AccessDenied";
    });

// Role policies
builder.Services.AddAuthorization(options => {
    options.AddPolicy("RequireReceptionist", p => p.RequireRole("ресепшен", "админ"));
    options.AddPolicy("RequireAdmin",        p => p.RequireRole("админ"));
});

// Rate limiter: max 10 login POSTs per minute per IP
builder.Services.AddRateLimiter(o =>
    o.AddFixedWindowLimiter("login", opts => {
        opts.Window               = TimeSpan.FromMinutes(1);
        opts.PermitLimit          = 10;
        opts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opts.QueueLimit           = 0;
    }));

var app = builder.Build();

// Startup seeder
using (var scope = app.Services.CreateScope()) {
    var db     = scope.ServiceProvider.GetRequiredService<HotelDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await Seeder.SeedAsync(db, logger);
}

if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error/500");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Error/{0}");
app.UseHttpsRedirection();

// Security headers on all responses (before static files so they apply everywhere)
app.Use(async (ctx, next) => {
    ctx.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    ctx.Response.Headers.Append("X-Frame-Options", "DENY");
    ctx.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    if (ctx.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase))
        ctx.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();

public partial class Program { } // needed for WebApplicationFactory in tests
```

- [ ] **Step 2: Add `using QonaqHotels.Data;` stub for Seeder** (Seeder is created in Task 5; add `// TODO: Task 5` comment to avoid compile error):

In Program.cs after `using QonaqHotels.Services;` add `using QonaqHotels.Data;` and replace the seeder block with a comment:
```csharp
// TODO Task 5: Seeder.SeedAsync called here
```

- [ ] **Step 3: Build**

```bash
dotnet build src/QonaqHotels/
```

---

### Task 5: Seeder (`Data/Seeder.cs`)

**Files:**
- Create: `src/QonaqHotels/Data/Seeder.cs`

**Interfaces:**
- Produces: `Seeder.SeedAsync(HotelDbContext db, ILogger logger)` — called from `Program.cs`

- [ ] **Step 1: Create `src/QonaqHotels/Data/Seeder.cs`**

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Models;

namespace QonaqHotels.Data;

public static class Seeder
{
    public static async Task SeedAsync(HotelDbContext db, ILogger logger)
    {
        if (await db.Users.AnyAsync()) return;

        var hotel = await db.Hotels.OrderBy(h => h.Id).FirstOrDefaultAsync();
        if (hotel == null)
        {
            logger.LogWarning(
                "Seeder: таблица hotels пуста — учётная запись администратора не создана. " +
                "Выполните db/seed.sql перед запуском приложения.");
            return;
        }

        var password = Environment.GetEnvironmentVariable("ADMIN_INITIAL_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "Переменная окружения ADMIN_INITIAL_PASSWORD не задана. " +
                "Установите её перед первым запуском.");

        // Find or create "администратор" position
        var position = await db.Positions
            .FirstOrDefaultAsync(p => p.Name == "администратор");
        if (position == null)
        {
            position = new Position { Name = "администратор" };
            db.Positions.Add(position);
            await db.SaveChangesAsync();
        }

        var employee = new Employee
        {
            FullName    = "Администратор Системы",
            HotelId     = hotel.Id,
            PositionId  = position.Id,
            HireDate    = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var user = new User
        {
            EmployeeId      = employee.Id,
            Login           = "admin",
            Role            = "админ",
            IsActive        = true,
            FailedAttempts  = 0
        };
        var hasher = new PasswordHasher<User>();
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        logger.LogInformation("Seeder: учётная запись 'admin' создана.");
    }
}
```

- [ ] **Step 2: Restore the seeder call in `Program.cs`** (replace the `// TODO Task 5` comment):

```csharp
using (var scope = app.Services.CreateScope()) {
    var db     = scope.ServiceProvider.GetRequiredService<HotelDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await Seeder.SeedAsync(db, logger);
}
```

- [ ] **Step 3: Smoke-test seeder**

```bash
ADMIN_INITIAL_PASSWORD=DevPass123! dotnet run --project src/QonaqHotels
```

Expected log: `Seeder: учётная запись 'admin' создана.`

Verify in DB:
```bash
psql -h localhost -U postgres -d gosti -c "SELECT login, role, is_active FROM users;"
```
Expected: one row `admin | админ | t`.

- [ ] **Step 4: Verify password hash is stored (not plaintext)**

```bash
psql -h localhost -U postgres -d gosti \
  -c "SELECT length(password_hash) > 20 AS hash_looks_ok FROM users WHERE login='admin';"
```
Expected: `t`.

---

### Task 6: Public layout and CSS

**Files:**
- Modify: `src/QonaqHotels/Pages/Shared/_Layout.cshtml` (scaffold default → replace)
- Create: `src/QonaqHotels/wwwroot/css/site.css`

- [ ] **Step 1: Create `src/QonaqHotels/wwwroot/css/site.css`**

```css
/* ── Variables ──────────────────────────────────────────── */
:root {
  --c-dark:   #1E2A3A;
  --c-accent: #C8A165;
  --c-bg:     #F7F5F1;
  --c-text:   #222222;
  --c-error:  #C0392B;
  --font-heading: 'Montserrat', sans-serif;
  --font-body:    'Inter', sans-serif;
}

/* ── Reset & base ───────────────────────────────────────── */
*, *::before, *::after { box-sizing: border-box; }
body {
  margin: 0;
  background: var(--c-bg);
  color: var(--c-text);
  font-family: var(--font-body);
  font-size: 16px;
  line-height: 1.6;
}
h1, h2, h3, h4 {
  font-family: var(--font-heading);
  font-weight: 600;
}
a { color: var(--c-accent); }
img { max-width: 100%; height: auto; }

/* ── Header ─────────────────────────────────────────────── */
.site-header {
  position: sticky;
  top: 0;
  z-index: 100;
  height: 72px;
  display: flex;
  align-items: center;
  padding: 0 2rem;
  transition: background .3s, box-shadow .3s, color .3s;
  background: transparent;
  color: #fff;
}
.site-header.scrolled {
  background: #fff;
  color: var(--c-dark);
  box-shadow: 0 2px 8px rgba(0,0,0,.12);
}
.site-header .logo { font-family: var(--font-heading); font-weight: 600; font-size: 1.25rem; text-decoration: none; }
.site-header:not(.scrolled) .logo { color: #fff; }
.site-header.scrolled .logo { color: var(--c-dark); }

.nav-links { display: flex; gap: 1.5rem; list-style: none; margin: 0; padding: 0; flex: 1; justify-content: center; }
.nav-links a { text-decoration: none; font-size: .95rem; }
.site-header:not(.scrolled) .nav-links a { color: rgba(255,255,255,.9); }
.site-header.scrolled .nav-links a { color: var(--c-dark); }
.nav-links a:hover { color: var(--c-accent); }

.header-right { display: flex; align-items: center; gap: 1rem; }
.btn-login {
  background: var(--c-accent);
  color: #fff;
  border: none;
  padding: .4rem 1rem;
  border-radius: 4px;
  font-family: var(--font-heading);
  font-size: .85rem;
  text-decoration: none;
  cursor: pointer;
}
.btn-login:hover { background: #b08a50; color: #fff; }

/* burger */
.burger { display: none; flex-direction: column; gap: 5px; cursor: pointer; background: none; border: none; padding: 4px; }
.burger span { display: block; width: 24px; height: 2px; background: currentColor; transition: .25s; }

/* ── Footer ─────────────────────────────────────────────── */
.site-footer {
  background: var(--c-dark);
  color: rgba(255,255,255,.8);
  padding: 3rem 2rem 1.5rem;
}
.footer-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 2rem; max-width: 1200px; margin: 0 auto; }
.footer-col h4 { color: #fff; font-size: .9rem; text-transform: uppercase; letter-spacing: .05em; margin-bottom: .75rem; }
.footer-col a { display: block; color: rgba(255,255,255,.7); text-decoration: none; margin-bottom: .35rem; font-size: .9rem; }
.footer-col a:hover { color: var(--c-accent); }
.footer-bottom { max-width: 1200px; margin: 2rem auto 0; padding-top: 1rem; border-top: 1px solid rgba(255,255,255,.1); display: flex; justify-content: space-between; font-size: .8rem; color: rgba(255,255,255,.5); }

/* ── Buttons ─────────────────────────────────────────────── */
.btn-primary {
  background: var(--c-accent);
  color: #fff;
  border: none;
  padding: .65rem 1.75rem;
  border-radius: 4px;
  font-family: var(--font-heading);
  font-size: .9rem;
  cursor: pointer;
  text-decoration: none;
  display: inline-block;
}
.btn-primary:hover { background: #b08a50; color: #fff; }

/* ── Hero ────────────────────────────────────────────────── */
.hero {
  position: relative;
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  text-align: center;
  color: #fff;
  overflow: hidden;
}
.hero-bg {
  position: absolute; inset: 0;
  background: url('/img/hero.webp') center/cover no-repeat;
}
.hero-bg::after {
  content: '';
  position: absolute; inset: 0;
  background: linear-gradient(to bottom, rgba(0,0,0,.45) 0%, rgba(0,0,0,.3) 60%, rgba(0,0,0,.6) 100%);
}
.hero-content { position: relative; z-index: 1; padding: 2rem; max-width: 750px; }
.hero-content h1 { font-size: clamp(2rem, 5vw, 3.5rem); margin-bottom: .75rem; }
.hero-content p  { font-size: 1.1rem; opacity: .9; margin-bottom: 2rem; }

/* Search form */
.search-bar {
  position: relative; z-index: 1;
  background: rgba(255,255,255,.95);
  border-radius: 8px;
  padding: 1rem;
  display: flex;
  gap: .75rem;
  align-items: flex-end;
  width: 100%;
  max-width: 900px;
  margin: 0 auto;
}
.search-bar select,
.search-bar input[type="date"],
.search-bar input[type="number"] {
  flex: 1; padding: .5rem .75rem; border: 1px solid #ccc; border-radius: 4px;
  font-family: var(--font-body); font-size: .9rem; color: var(--c-text);
}
.search-bar label { display: block; font-size: .78rem; color: #555; margin-bottom: .2rem; font-family: var(--font-heading); }
.search-field { flex: 1; }

/* ── Sections ────────────────────────────────────────────── */
.section { padding: 5rem 2rem; }
.section-inner { max-width: 1200px; margin: 0 auto; }
.section-title { text-align: center; font-size: clamp(1.5rem, 3vw, 2.2rem); margin-bottom: .5rem; }
.section-sub   { text-align: center; color: #666; margin-bottom: 3rem; }

/* ── Cards ───────────────────────────────────────────────── */
.card-grid { display: grid; gap: 1.5rem; }
.card-grid.cols-3 { grid-template-columns: repeat(3, 1fr); }
.card-grid.cols-4 { grid-template-columns: repeat(4, 1fr); }
.card {
  background: #fff;
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 2px 8px rgba(0,0,0,.06);
  transition: transform .2s, box-shadow .2s;
}
.card:hover { transform: translateY(-4px); box-shadow: 0 6px 20px rgba(0,0,0,.1); }
.card img { width: 100%; height: 220px; object-fit: cover; }
.card-body { padding: 1.25rem; }
.card-body h3 { margin: 0 0 .4rem; font-size: 1.05rem; }
.card-body p  { margin: 0 0 1rem; color: #666; font-size: .9rem; }

/* Stars */
.stars { color: var(--c-accent); font-size: 1rem; }

/* ── Benefits strip ─────────────────────────────────────── */
.benefits { background: var(--c-dark); color: #fff; padding: 3rem 2rem; }
.benefits-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 2rem; max-width: 1000px; margin: 0 auto; text-align: center; }
.benefit-icon { font-size: 2rem; margin-bottom: .5rem; }
.benefit-title { font-family: var(--font-heading); font-weight: 600; margin-bottom: .25rem; }
.benefit-desc  { font-size: .85rem; opacity: .75; }

/* ── About strip ─────────────────────────────────────────── */
.about-strip { display: grid; grid-template-columns: 1fr 1fr; gap: 3rem; align-items: center; }
.about-counters { display: flex; gap: 2rem; margin-top: 1.5rem; }
.counter-num  { font-size: 2.5rem; font-family: var(--font-heading); font-weight: 600; color: var(--c-accent); }
.counter-lbl  { font-size: .85rem; color: #666; }

/* ── Loyalty banner ─────────────────────────────────────── */
.loyalty-banner {
  background: url('/img/loyalty-bg.webp') center/cover no-repeat;
  position: relative;
  padding: 4rem 2rem;
  text-align: center;
  color: #fff;
}
.loyalty-banner::before { content: ''; position: absolute; inset: 0; background: rgba(30,42,58,.6); }
.loyalty-banner > * { position: relative; }
.loyalty-cards-row { display: flex; justify-content: center; gap: 2rem; margin: 2rem 0; }
.loyalty-card-item {
  background: rgba(255,255,255,.12);
  border: 1px solid rgba(200,161,101,.5);
  border-radius: 8px;
  padding: 1.5rem 2rem;
  min-width: 160px;
}
.loyalty-card-item .level { font-family: var(--font-heading); font-weight: 600; color: var(--c-accent); font-size: 1.1rem; }

/* ── Reviews ─────────────────────────────────────────────── */
.reviews-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 1.5rem; }
.review-card { background: #fff; border-radius: 8px; padding: 1.5rem; box-shadow: 0 2px 8px rgba(0,0,0,.06); }
.review-author { font-weight: 600; margin-bottom: .25rem; }
.review-city   { color: #888; font-size: .85rem; margin-bottom: .5rem; }
.review-text   { color: #555; font-size: .9rem; }

/* ── Admin panel ─────────────────────────────────────────── */
.admin-layout { display: flex; min-height: 100vh; }
.admin-sidebar {
  width: 240px;
  background: var(--c-dark);
  color: #fff;
  flex-shrink: 0;
  padding: 1.5rem 0;
}
.admin-sidebar .sidebar-logo { padding: .5rem 1.5rem 1.5rem; font-family: var(--font-heading); font-size: 1rem; }
.sidebar-link {
  display: block;
  padding: .6rem 1.5rem;
  color: rgba(255,255,255,.7);
  text-decoration: none;
  font-size: .9rem;
  transition: background .15s;
}
.sidebar-link:hover, .sidebar-link.active { background: rgba(255,255,255,.08); color: #fff; }
.sidebar-link.disabled { color: rgba(255,255,255,.3); pointer-events: none; cursor: default; }
.sidebar-link .soon { font-size: .7rem; margin-left: .35rem; opacity: .6; }
.admin-main { flex: 1; display: flex; flex-direction: column; }
.admin-topbar {
  height: 60px;
  background: #fff;
  border-bottom: 1px solid #eee;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  padding: 0 1.5rem;
  gap: 1rem;
}
.admin-content { flex: 1; padding: 2rem; background: var(--c-bg); }

/* ── Login page ──────────────────────────────────────────── */
.login-page {
  min-height: 100vh;
  background: url('/img/login-bg.webp') center/cover no-repeat;
  display: flex;
  align-items: center;
  justify-content: center;
}
.login-card {
  background: #fff;
  border-radius: 12px;
  padding: 2.5rem;
  width: 400px;
  box-shadow: 0 8px 40px rgba(0,0,0,.25);
}
.login-card .logo-block { text-align: center; margin-bottom: 1.5rem; }
.login-card h1 { font-size: 1.2rem; text-align: center; margin-bottom: 1.5rem; }
.form-group { margin-bottom: 1rem; }
.form-group label { display: block; font-size: .85rem; margin-bottom: .3rem; font-family: var(--font-heading); }
.form-group input[type="text"],
.form-group input[type="password"] {
  width: 100%; padding: .6rem .75rem; border: 1px solid #ccc; border-radius: 4px;
  font-family: var(--font-body); font-size: .95rem;
}
.password-wrapper { position: relative; }
.password-wrapper input { padding-right: 2.5rem; }
.btn-eye { position: absolute; right: .6rem; top: 50%; transform: translateY(-50%); background: none; border: none; cursor: pointer; color: #888; padding: 0; font-size: 1.1rem; }
.caps-warning { font-size: .78rem; color: var(--c-error); margin-top: .2rem; display: none; }
.caps-warning.visible { display: block; }
.alert-error { background: #fde8e8; color: var(--c-error); border: 1px solid #f5c6c6; border-radius: 4px; padding: .6rem .9rem; font-size: .9rem; margin-bottom: 1rem; }
.login-hint { font-size: .8rem; color: #888; text-align: center; margin-top: .75rem; }
.back-link { display: block; text-align: center; margin-top: 1rem; font-size: .85rem; color: #666; text-decoration: none; }
.back-link:hover { color: var(--c-accent); }

/* ── Responsive ──────────────────────────────────────────── */
@media (max-width: 1199px) {
  .card-grid.cols-3 { grid-template-columns: repeat(2, 1fr); }
  .card-grid.cols-4 { grid-template-columns: repeat(2, 1fr); }
  .footer-grid { grid-template-columns: repeat(2, 1fr); }
  .reviews-grid { grid-template-columns: repeat(2, 1fr); }
}
@media (max-width: 767px) {
  .site-header { height: 60px; padding: 0 1rem; }
  .nav-links { display: none; }
  .nav-links.open { display: flex; flex-direction: column; position: absolute; top: 60px; left: 0; right: 0; background: var(--c-dark); padding: 1rem; }
  .burger { display: flex; }
  .card-grid.cols-3,
  .card-grid.cols-4 { grid-template-columns: 1fr; }
  .benefits-grid { grid-template-columns: repeat(2, 1fr); }
  .footer-grid { grid-template-columns: 1fr; }
  .reviews-grid { grid-template-columns: 1fr; }
  .about-strip { grid-template-columns: 1fr; }
  .search-bar { flex-direction: column; }
  .loyalty-cards-row { flex-direction: column; align-items: center; }
  .login-card { width: 100%; margin: 1rem; }
  .admin-sidebar { display: none; }
}
@media (max-width: 359px) {
  .section { padding: 3rem 1rem; }
}
```

- [ ] **Step 2: Write `src/QonaqHotels/Pages/Shared/_Layout.cshtml`**

```cshtml
@{
    var user = Context.User;
    var isAuth = user.Identity?.IsAuthenticated ?? false;
    var userName = user.Identity?.Name ?? "";
}
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="description" content="Отели Qonaq в Астане, Алматы и Шымкенте. Бронирование напрямую, бесплатная отмена, оплата при заезде." />
    <title>@(ViewData["Title"] != null ? ViewData["Title"] + " — " : "")Qonaq Hotels — сеть отелей в Казахстане</title>
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500&family=Montserrat:wght@600;700&display=swap" rel="stylesheet" />
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.css" />
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
    @await RenderSectionAsync("Styles", required: false)
</head>
<body>
    <div id="header-sentinel" style="position:absolute;top:80px;height:1px;pointer-events:none;"></div>
    <header class="site-header" id="site-header">
        <a href="/" class="logo">QONAQ <span style="font-size:.7em;opacity:.7;display:block;font-weight:400;letter-spacing:.15em">HOTELS</span></a>
        <ul class="nav-links" id="nav-links">
            <li><a href="/#rooms">Номера</a></li>
            <li><a href="/#hotels">Наши отели</a></li>
            <li><a href="/#services">Услуги</a></li>
            <li><a href="/#contacts">Контакты</a></li>
        </ul>
        <div class="header-right">
            <a href="tel:+77000000001" style="font-size:.85rem;text-decoration:none;color:inherit">+7 700 000 00 01</a>
            @if (isAuth) {
                <div class="dropdown" style="position:relative">
                    <button class="btn-login" onclick="this.nextElementSibling.classList.toggle('show')">
                        <i class="bi bi-person-circle"></i> @userName
                    </button>
                    <div class="dropdown-menu" style="display:none;position:absolute;right:0;top:110%;background:#fff;border-radius:6px;box-shadow:0 4px 16px rgba(0,0,0,.15);min-width:180px;z-index:200">
                        <a class="dropdown-item" href="/admin" style="display:block;padding:.5rem 1rem;color:var(--c-dark);text-decoration:none">Панель управления</a>
                        <form method="post" action="/logout">
                            <input type="hidden" name="__RequestVerificationToken" value="@Antiforgery.GetAndStoreTokens(Context).RequestToken" />
                            <button type="submit" style="display:block;width:100%;padding:.5rem 1rem;background:none;border:none;text-align:left;cursor:pointer;color:var(--c-error)">Выйти</button>
                        </form>
                    </div>
                </div>
            } else {
                <a href="/login" class="btn-login"><i class="bi bi-person"></i> Войти</a>
            }
            <button class="burger" id="burger" aria-label="Меню">
                <span></span><span></span><span></span>
            </button>
        </div>
    </header>

    @RenderBody()

    <footer class="site-footer">
        <div class="footer-grid">
            <div class="footer-col">
                <div style="font-family:var(--font-heading);font-weight:600;font-size:1.1rem;color:#fff;margin-bottom:.5rem">QONAQ HOTELS</div>
                <p style="font-size:.85rem">Сеть отелей в городах Казахстана</p>
            </div>
            <div class="footer-col">
                <h4>Отель</h4>
                <a href="/#rooms">Номера</a>
                <a href="/#hotels">Наши отели</a>
                <a href="/#services">Услуги</a>
                <a href="/#contacts">Контакты</a>
            </div>
            <div class="footer-col">
                <h4>Гостям</h4>
                <a href="/loyalty">Программа лояльности</a>
                <a href="#">Политика конфиденциальности</a>
            </div>
            <div class="footer-col">
                <h4>Контакты</h4>
                <a href="tel:+77000000001">+7 700 000 00 01</a>
                <a href="mailto:booking@qonaq-hotels.kz">booking@qonaq-hotels.kz</a>
                <p style="font-size:.85rem;margin:.5rem 0">г. Астана, пр. Мангилик Ел, 55</p>
                <div style="display:flex;gap:.75rem;margin-top:.5rem">
                    <a href="#" aria-label="Instagram"><i class="bi bi-instagram" style="font-size:1.2rem"></i></a>
                    <a href="#" aria-label="WhatsApp"><i class="bi bi-whatsapp" style="font-size:1.2rem"></i></a>
                    <a href="#" aria-label="Telegram"><i class="bi bi-telegram" style="font-size:1.2rem"></i></a>
                </div>
            </div>
        </div>
        <div class="footer-bottom">
            <span>© 2026 Qonaq Hotels. Учебный проект</span>
            <a href="/login" style="color:rgba(255,255,255,.4)">Вход для сотрудников</a>
        </div>
    </footer>

    <script src="~/js/site.js" asp-append-version="true"></script>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

Note: The `@Antiforgery.GetAndStoreTokens(Context).RequestToken` in the layout requires injecting `IAntiforgery` — use `@inject Microsoft.AspNetCore.Antiforgery.IAntiforgery Antiforgery` at the top of the file, or use the `<form>` tag helper with `asp-antiforgery="true"` instead. Use the tag helper approach:

```cshtml
@* Replace the logout form above with: *@
<form method="post" action="/logout" asp-antiforgery="true">
    <button type="submit" ...>Выйти</button>
</form>
```

- [ ] **Step 3: Build**

```bash
dotnet build src/QonaqHotels/
```

---

### Task 7: JavaScript (`wwwroot/js/site.js`)

**Files:**
- Create: `src/QonaqHotels/wwwroot/js/site.js`

- [ ] **Step 1: Write `site.js`**

```javascript
'use strict';

// ── 1. Header scroll effect ────────────────────────────────
(function () {
    const header   = document.getElementById('site-header');
    const sentinel = document.getElementById('header-sentinel');
    if (!header || !sentinel) return;
    const obs = new IntersectionObserver(
        ([entry]) => header.classList.toggle('scrolled', !entry.isIntersecting),
        { threshold: 0 }
    );
    obs.observe(sentinel);
})();

// ── 2. Burger menu ────────────────────────────────────────
(function () {
    const burger = document.getElementById('burger');
    const nav    = document.getElementById('nav-links');
    if (!burger || !nav) return;
    burger.addEventListener('click', () => nav.classList.toggle('open'));
})();

// ── 3. Show/hide password ─────────────────────────────────
document.querySelectorAll('[data-toggle-password]').forEach(btn => {
    btn.addEventListener('click', () => {
        const input = btn.closest('.password-wrapper')?.querySelector('input');
        if (!input) return;
        const isPassword = input.type === 'password';
        input.type = isPassword ? 'text' : 'password';
        btn.querySelector('i').className = isPassword ? 'bi bi-eye-slash' : 'bi bi-eye';
    });
});

// ── 4. Caps Lock warning ──────────────────────────────────
document.querySelectorAll('[data-capslock-warning]').forEach(input => {
    const warning = document.getElementById(input.dataset.capslockWarning);
    if (!warning) return;
    const update = (e) => warning.classList.toggle('visible', e.getModifierState?.('CapsLock') ?? false);
    input.addEventListener('keydown', update);
    input.addEventListener('keyup',  update);
});

// ── 5. Search date validation ─────────────────────────────
(function () {
    const checkIn  = document.getElementById('search-checkin');
    const checkOut = document.getElementById('search-checkout');
    if (!checkIn || !checkOut) return;

    // Default: today and tomorrow
    const today    = new Date();
    const tomorrow = new Date(today);
    tomorrow.setDate(today.getDate() + 1);
    const fmt = d => d.toISOString().slice(0, 10);

    checkIn.min  = fmt(today);
    checkOut.min = fmt(tomorrow);
    if (!checkIn.value)  checkIn.value  = fmt(today);
    if (!checkOut.value) checkOut.value = fmt(tomorrow);

    checkIn.addEventListener('change', () => {
        if (!checkIn.value) return;
        const minOut = new Date(checkIn.value);
        minOut.setDate(minOut.getDate() + 1);
        checkOut.min = fmt(minOut);
        if (checkOut.value && checkOut.value <= checkIn.value)
            checkOut.value = fmt(minOut);
    });
})();

// ── 6. Login spinner ──────────────────────────────────────
(function () {
    const form = document.getElementById('login-form');
    if (!form) return;
    form.addEventListener('submit', () => {
        const btn = form.querySelector('[type=submit]');
        if (!btn) return;
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status" aria-hidden="true"></span>Вход…';
    });
})();

// ── 7. Dropdown close on outside click ───────────────────
document.addEventListener('click', e => {
    document.querySelectorAll('.dropdown-menu.show').forEach(menu => {
        if (!menu.closest('.dropdown')?.contains(e.target))
            menu.classList.remove('show');
    });
});
```

- [ ] **Step 2: Fix dropdown show/hide** — the CSS uses `display:none` / `.show`, but the CSS rule says `display:none`. Update the dropdown CSS to support `.show`:

In `site.css`, add:
```css
.dropdown-menu.show { display: block !important; }
```

---

### Task 8: Admin layout

**Files:**
- Create: `src/QonaqHotels/Pages/Shared/_AdminLayout.cshtml`
- Create: `src/QonaqHotels/Pages/Admin/_ViewStart.cshtml`
- Create: `src/QonaqHotels/Pages/Admin/` directory

- [ ] **Step 1: Create `src/QonaqHotels/Pages/Admin/_ViewStart.cshtml`**

```cshtml
@{
    Layout = "_AdminLayout";
}
```

- [ ] **Step 2: Create `src/QonaqHotels/Pages/Shared/_AdminLayout.cshtml`**

```cshtml
@{
    var userName = Context.User.Identity?.Name ?? "Сотрудник";
    var role     = Context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
    var hotelId  = Context.User.FindFirst("hotel_id")?.Value ?? "";
}
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@(ViewData["Title"] != null ? ViewData["Title"] + " — " : "")Панель · Qonaq Hotels</title>
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500&family=Montserrat:wght@600&display=swap" rel="stylesheet" />
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.css" />
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
</head>
<body>
<div class="admin-layout">
    <aside class="admin-sidebar">
        <div class="sidebar-logo">QONAQ HOTELS</div>
        <a class="sidebar-link active" href="/admin"><i class="bi bi-house me-2"></i>Главная</a>
        <a class="sidebar-link disabled" href="#"><i class="bi bi-grid-3x3 me-2"></i>Шахматка<span class="soon">(скоро)</span></a>
        <a class="sidebar-link disabled" href="#"><i class="bi bi-calendar3 me-2"></i>Брони<span class="soon">(скоро)</span></a>
        <a class="sidebar-link disabled" href="#"><i class="bi bi-door-open me-2"></i>Заселение<span class="soon">(скоро)</span></a>
        <a class="sidebar-link disabled" href="#"><i class="bi bi-people me-2"></i>Гости<span class="soon">(скоро)</span></a>
        <a class="sidebar-link disabled" href="#"><i class="bi bi-search me-2"></i>Поиск номера<span class="soon">(скоро)</span></a>
        <a class="sidebar-link disabled" href="#"><i class="bi bi-bag me-2"></i>Услуги<span class="soon">(скоро)</span></a>
        <a class="sidebar-link disabled" href="#"><i class="bi bi-book me-2"></i>Справочники<span class="soon">(скоро)</span></a>
        <a class="sidebar-link disabled" href="#"><i class="bi bi-bar-chart me-2"></i>Отчёты<span class="soon">(скоро)</span></a>
        <a class="sidebar-link disabled" href="#"><i class="bi bi-person-gear me-2"></i>Пользователи<span class="soon">(скоро)</span></a>
        <a class="sidebar-link disabled" href="#"><i class="bi bi-journal-text me-2"></i>Журнал<span class="soon">(скоро)</span></a>
    </aside>
    <div class="admin-main">
        <header class="admin-topbar">
            <span style="font-size:.85rem;color:#666">@userName &nbsp;<span style="background:var(--c-accent);color:#fff;padding:.15rem .5rem;border-radius:4px;font-size:.75rem">@role</span></span>
            <form method="post" action="/logout" asp-antiforgery="true" style="margin:0">
                <button type="submit" class="btn-login" style="font-size:.8rem;padding:.3rem .75rem">
                    <i class="bi bi-box-arrow-right"></i> Выйти
                </button>
            </form>
        </header>
        <main class="admin-content">
            @RenderBody()
        </main>
    </div>
</div>
<script src="~/js/site.js" asp-append-version="true"></script>
</body>
</html>
```

- [ ] **Step 3: Build**

```bash
dotnet build src/QonaqHotels/
```

---

### Task 9: Homepage (`Pages/Index`)

**Files:**
- Modify: `src/QonaqHotels/Pages/Index.cshtml`
- Modify: `src/QonaqHotels/Pages/Index.cshtml.cs`
- Create: `src/QonaqHotels/wwwroot/img/placeholder.webp`
- Create: `tests/QonaqHotels.Tests/IndexPageTests.cs`

**Interfaces:**
- Consumes: `HotelDbContext` (Task 3), `IWebHostEnvironment` (built-in)
- Produces: `IndexModel.Hotels`, `IndexModel.RoomTypes`, `IndexModel.Cities` — used by `Index.cshtml`

- [ ] **Step 1: Write failing tests (`tests/QonaqHotels.Tests/IndexPageTests.cs`)**

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Moq;
using QonaqHotels.Data;
using QonaqHotels.Models;
using QonaqHotels.Pages;

namespace QonaqHotels.Tests;

public class IndexPageTests
{
    private static HotelDbContext CreateContext(string dbName)
    {
        var opts = new DbContextOptionsBuilder<HotelDbContext>()
            .UseInMemoryDatabase(dbName).Options;
        return new HotelDbContext(opts);
    }

    private static Mock<IWebHostEnvironment> MockEnv(string webRoot = "/fake")
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.WebRootPath).Returns(webRoot);
        return env;
    }

    [Fact]
    public async Task OnGetAsync_EmptyTables_DoesNotThrow()
    {
        using var db  = CreateContext("empty-index");
        var model = new IndexModel(db, MockEnv().Object);
        await model.OnGetAsync(); // must not throw
        Assert.Empty(model.Hotels);
        Assert.Empty(model.RoomTypes);
        Assert.Empty(model.Cities);
    }

    [Fact]
    public async Task OnGetAsync_WithData_PopulatesCollections()
    {
        using var db = CreateContext("with-data-index");
        var country = new Country { Name = "Казахстан" };
        db.Countries.Add(country);
        var city = new City { Name = "Астана", Country = country };
        db.Cities.Add(city);
        db.Hotels.Add(new Hotel { Name = "H1", City = city, Address = "a", Stars = 4 });
        db.RoomTypes.Add(new RoomType { Name = "Стандарт", Capacity = 2 });
        await db.SaveChangesAsync();

        var model = new IndexModel(db, MockEnv().Object);
        await model.OnGetAsync();

        Assert.Single(model.Hotels);
        Assert.Single(model.RoomTypes);
        Assert.Single(model.Cities);
    }

    [Fact]
    public void RoomImage_FileMissing_ReturnsPlaceholder()
    {
        using var db    = CreateContext("img-test");
        var model = new IndexModel(db, MockEnv("/nonexistent-dir").Object);
        var img = model.RoomImage(99);
        Assert.Equal("/img/placeholder.webp", img);
    }

    [Fact]
    public async Task OnGetAsync_MoreThan6RoomTypes_Returns6()
    {
        using var db = CreateContext("six-types");
        for (int i = 1; i <= 8; i++)
            db.RoomTypes.Add(new RoomType { Name = $"Type{i}", Capacity = 2 });
        await db.SaveChangesAsync();

        var country = new Country { Name = "Казахстан" };
        db.Countries.Add(country);
        await db.SaveChangesAsync();

        var model = new IndexModel(db, MockEnv().Object);
        await model.OnGetAsync();
        Assert.Equal(6, model.RoomTypes.Count);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test tests/QonaqHotels.Tests/ --filter "IndexPageTests" -v normal
```
Expected: compilation error (`IndexModel` not yet complete).

- [ ] **Step 3: Write `src/QonaqHotels/Pages/Index.cshtml.cs`**

```csharp
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Pages;

public class IndexModel : PageModel
{
    private readonly HotelDbContext _db;
    private readonly IWebHostEnvironment _env;

    public IndexModel(HotelDbContext db, IWebHostEnvironment env)
    {
        _db  = db;
        _env = env;
    }

    public List<Hotel>    Hotels    { get; private set; } = [];
    public List<RoomType> RoomTypes { get; private set; } = [];
    public List<City>     Cities    { get; private set; } = [];
    public int HotelCount  { get; private set; }
    public int RoomCount   { get; private set; }
    public int CityCount   { get; private set; }

    public async Task OnGetAsync()
    {
        Hotels = await _db.Hotels
            .AsNoTracking()
            .Include(h => h.City)
            .OrderBy(h => h.City.Name)
            .ToListAsync();

        RoomTypes = await _db.RoomTypes
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .Take(6)
            .ToListAsync();

        Cities = await _db.Cities
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync();

        HotelCount = await _db.Hotels.AsNoTracking().CountAsync();
        RoomCount  = await _db.Rooms.AsNoTracking().CountAsync();
        CityCount  = await _db.Cities.AsNoTracking().CountAsync();
    }

    public string RoomImage(int id)
    {
        var path = Path.Combine(_env.WebRootPath ?? "", "img", $"room-{id}.webp");
        return File.Exists(path) ? $"/img/room-{id}.webp" : "/img/placeholder.webp";
    }

    public string HotelImage(int id)
    {
        var path = Path.Combine(_env.WebRootPath ?? "", "img", $"hotel-{id}.webp");
        return File.Exists(path) ? $"/img/hotel-{id}.webp" : "/img/placeholder.webp";
    }
}
```

- [ ] **Step 4: Run tests again**

```bash
dotnet test tests/QonaqHotels.Tests/ --filter "IndexPageTests"
```
Expected: all 4 pass.

- [ ] **Step 5: Create a minimal placeholder image**

```bash
# Create a 1x1 grey WebP placeholder (requires ImageMagick or use a pre-made file)
# If ImageMagick available:
convert -size 800x600 xc:#CCCCCC \
  -font DejaVu-Sans -pointsize 48 -fill '#888888' \
  -gravity center -annotate 0 "Qonaq Hotels" \
  src/QonaqHotels/wwwroot/img/placeholder.webp

# If not available, create a minimal valid WebP manually or copy any small image:
mkdir -p src/QonaqHotels/wwwroot/img
# Copy a placeholder from a free source or use the Python one-liner below:
python3 -c "
import struct, zlib
# Write a 1x1 grey PNG that browsers accept
data = b'\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01\x08\x00\x00\x00\x00:~\x9bU\x00\x00\x00\nIDATx\x9cc\xf8\x0f\x00\x00\x01\x01\x00\x05\x18\xd8N\x00\x00\x00\x00IEND\xaeB\x60\x82'
open('src/QonaqHotels/wwwroot/img/placeholder.webp', 'wb').write(data)
"
```

Note: For production use, download a proper 800×600 grey WebP from any stock site or design tool.

- [ ] **Step 6: Write `src/QonaqHotels/Pages/Index.cshtml`** (9 blocks):

The page must include all 9 sections from the spec. Key structural elements:

```cshtml
@page
@model IndexModel
@{
    ViewData["Title"] = null; // browser tab shows default from _Layout
}

@* Block 1: Hero *@
<section class="hero" id="top">
    <div class="hero-bg"></div>
    <div class="hero-content">
        <h1>Как дома — в любом городе Казахстана</h1>
        <p>Отели Qonaq в Астане, Алматы и Шымкенте. Бронируйте напрямую — без комиссий и переплат.</p>
    </div>
    <div class="search-bar">
        <form method="get" action="/search" id="search-form">
            <div class="search-field">
                <label for="search-city">Город</label>
                <select name="city" id="search-city">
                    <option value="">Все города</option>
                    @foreach (var city in Model.Cities) {
                        <option value="@city.Id">@city.Name</option>
                    }
                </select>
            </div>
            <div class="search-field">
                <label for="search-checkin">Заезд</label>
                <input type="date" id="search-checkin" name="from" />
            </div>
            <div class="search-field">
                <label for="search-checkout">Выезд</label>
                <input type="date" id="search-checkout" name="to" />
            </div>
            <div class="search-field" style="max-width:100px">
                <label for="search-guests">Гостей</label>
                <input type="number" id="search-guests" name="guests" value="1" min="1" max="6" />
            </div>
            <button type="submit" class="btn-primary">Найти номер</button>
        </form>
    </div>
</section>

@* Block 2: Benefits *@
<section class="benefits">
    <div class="benefits-grid">
        <div><div class="benefit-icon"><i class="bi bi-tag"></i></div><div class="benefit-title">Лучшая цена</div><div class="benefit-desc">Напрямую дешевле, чем на сайтах-агрегаторах</div></div>
        <div><div class="benefit-icon"><i class="bi bi-x-circle"></i></div><div class="benefit-title">Бесплатная отмена</div><div class="benefit-desc">До 18:00 дня заезда без штрафа</div></div>
        <div><div class="benefit-icon"><i class="bi bi-credit-card"></i></div><div class="benefit-title">Оплата при заезде</div><div class="benefit-desc">Ничего не нужно платить заранее</div></div>
        <div><div class="benefit-icon"><i class="bi bi-gift"></i></div><div class="benefit-title">Бонусы за проживание</div><div class="benefit-desc">Копите баллы и тратьте в следующих поездках</div></div>
    </div>
</section>

@* Block 3: About *@
<section class="section" id="about">
    <div class="section-inner about-strip">
        <div>
            <img src="/img/about.webp" alt="Стойка регистрации Qonaq Hotels" loading="lazy" style="border-radius:8px;width:100%;object-fit:cover;height:360px" />
        </div>
        <div>
            <h2>Сеть отелей Qonaq</h2>
            <p>Qonaq по-казахски — «гость». Вокруг этого слова мы и строим наши отели: встречаем тепло, селим быстро и помним, что вы любите.</p>
            <p>В сети есть и уютные 3★, и деловые 5★. Все отели стоят рядом с бизнес-центрами, вокзалами и главными местами города. Заезд с 14:00, выезд до 12:00, стойка регистрации работает круглосуточно.</p>
            <div class="about-counters">
                <div><div class="counter-num">@Model.HotelCount</div><div class="counter-lbl">отелей в сети</div></div>
                <div><div class="counter-num">@Model.RoomCount</div><div class="counter-lbl">номеров</div></div>
                <div><div class="counter-num">@Model.CityCount</div><div class="counter-lbl">городов</div></div>
            </div>
        </div>
    </div>
</section>

@* Block 4: Room Types *@
@if (Model.RoomTypes.Any()) {
<section class="section" id="rooms" style="background:#fff">
    <div class="section-inner">
        <h2 class="section-title">Номера</h2>
        <p class="section-sub">От компактного стандарта до семейного люкса</p>
        <div class="card-grid cols-3">
            @foreach (var rt in Model.RoomTypes) {
                <div class="card">
                    <img src="@Model.RoomImage(rt.Id)" alt="Номер @rt.Name" loading="lazy" />
                    <div class="card-body">
                        <h3>@rt.Name</h3>
                        <p>Вместимость: до @rt.Capacity чел.</p>
                        <p>@rt.Description</p>
                        <a href="/rooms#type-@rt.Id" class="btn-primary">Подробнее</a>
                    </div>
                </div>
            }
        </div>
    </div>
</section>
}

@* Block 5: Hotels *@
@if (Model.Hotels.Any()) {
<section class="section" id="hotels">
    <div class="section-inner">
        <h2 class="section-title">Наши отели</h2>
        <p class="section-sub">Выберите город — остальное мы возьмём на себя</p>
        <div class="card-grid cols-3">
            @foreach (var h in Model.Hotels) {
                <div class="card">
                    <img src="@Model.HotelImage(h.Id)" alt="@h.Name" loading="lazy" />
                    <div class="card-body">
                        <div class="stars">@string.Concat(Enumerable.Repeat("★", h.Stars))@string.Concat(Enumerable.Repeat("☆", 5 - h.Stars))</div>
                        <h3>@h.Name</h3>
                        <p><i class="bi bi-geo-alt"></i> @h.City.Name, @h.Address</p>
                        @if (h.Phone != null) {
                            <p><a href="tel:@h.Phone">@h.Phone</a></p>
                        }
                    </div>
                </div>
            }
        </div>
    </div>
</section>
}

@* Block 6: Services *@
<section class="section" id="services" style="background:#fff">
    <div class="section-inner">
        <h2 class="section-title">Услуги</h2>
        <div class="card-grid cols-4">
            <div class="card"><img src="/img/service-restaurant.webp" alt="Ресторан Дастархан" loading="lazy" /><div class="card-body"><h3>Ресторан «Дастархан»</h3><p>Казахская и европейская кухня. Завтрак «шведский стол» с 7:00 до 10:30.</p></div></div>
            <div class="card"><img src="/img/service-spa.webp" alt="Спа и фитнес" loading="lazy" /><div class="card-body"><h3>Спа и фитнес</h3><p>Бассейн, сауна и тренажёрный зал. Для гостей бесплатно с 7:00 до 22:00.</p></div></div>
            <div class="card"><img src="/img/service-conference.webp" alt="Конференц-залы" loading="lazy" /><div class="card-body"><h3>Конференц-залы</h3><p>Залы на 20–150 человек с проектором и звуком. Кофе-брейк по запросу.</p></div></div>
            <div class="card"><img src="/img/service-transfer.webp" alt="Трансфер" loading="lazy" /><div class="card-body"><h3>Трансфер</h3><p>Встретим в аэропорту или на вокзале и довезём до отеля.</p></div></div>
        </div>
    </div>
</section>

@* Block 7: Loyalty *@
<section class="loyalty-banner">
    <h2>Карта гостя Qonaq</h2>
    <p>Получите карту при первом заселении и копите баллы за каждую ночь.</p>
    <div class="loyalty-cards-row">
        <div class="loyalty-card-item"><div class="level">Silver</div><div>С первого заселения</div><div style="font-size:.85rem;opacity:.8">3% от стоимости баллами</div></div>
        <div class="loyalty-card-item"><div class="level">Gold</div><div>От 10 ночей</div><div style="font-size:.85rem;opacity:.8">5% баллами + поздний выезд до 14:00</div></div>
        <div class="loyalty-card-item"><div class="level">Platinum</div><div>От 30 ночей</div><div style="font-size:.85rem;opacity:.8">10% баллами + повышение категории</div></div>
    </div>
    <a href="/loyalty" class="btn-primary">Подробнее</a>
</section>

@* Block 8: Reviews *@
<section class="section">
    <div class="section-inner">
        <h2 class="section-title">Отзывы гостей</h2>
        <div class="reviews-grid">
            <div class="review-card"><div class="review-author">Айгерим</div><div class="review-city">Алматы</div><div class="stars">★★★★★</div><p class="review-text">Была в командировке три дня. Тихий номер, быстрый заезд, отличный завтрак.</p></div>
            <div class="review-card"><div class="review-author">Даниэль</div><div class="review-city">Берлин</div><div class="stars">★★★★☆</div><p class="review-text">Удобно добираться до выставки, персонал говорит по-английски. У кровати не хватает розеток.</p></div>
            <div class="review-card"><div class="review-author">Мадина</div><div class="review-city">Шымкент</div><div class="stars">★★★★★</div><p class="review-text">Отдыхали семьёй в семейном номере. Детям понравился бассейн, нам — тишина.</p></div>
        </div>
    </div>
</section>

@* Block 9: Contacts *@
<section class="section" id="contacts" style="background:#fff">
    <div class="section-inner">
        <h2 class="section-title">Контакты</h2>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:2rem;align-items:start">
            <div>
                <p><strong>Отдел бронирования:</strong><br /><a href="tel:+77000000001">+7 700 000 00 01</a><br /><a href="mailto:booking@qonaq-hotels.kz">booking@qonaq-hotels.kz</a></p>
                <p><strong>Отдел продаж:</strong><br /><a href="tel:+77000000009">+7 700 000 00 09</a><br /><a href="mailto:sales@qonaq-hotels.kz">sales@qonaq-hotels.kz</a></p>
                <p><strong>Головной офис:</strong><br />г. Астана, пр. Мангилик Ел, 55</p>
            </div>
            <div>
                <iframe src="https://2gis.kz/astana?m=71.430564%2C51.128422%2F14" width="100%" height="300" frameborder="0" allowfullscreen style="border-radius:8px"></iframe>
                <p style="text-align:center;margin-top:.5rem"><a href="https://2gis.kz/astana" target="_blank" rel="noopener">Открыть в 2ГИС</a></p>
            </div>
        </div>
    </div>
</section>
```

- [ ] **Step 7: Build and run**

```bash
ADMIN_INITIAL_PASSWORD=DevPass123! dotnet run --project src/QonaqHotels
```

Open `https://localhost:5001/` and verify all 9 blocks are visible.

---

### Task 10: Login page and `LoginService`

**Files:**
- Create: `src/QonaqHotels/Services/LoginService.cs`
- Modify: `src/QonaqHotels/Pages/Login.cshtml`
- Modify: `src/QonaqHotels/Pages/Login.cshtml.cs`
- Create: `tests/QonaqHotels.Tests/LoginServiceTests.cs`

**Interfaces:**
- Produces: `LoginService.AttemptLoginAsync(string login, string password)` → `LoginAttemptResult`

- [ ] **Step 1: Write failing tests (`tests/QonaqHotels.Tests/LoginServiceTests.cs`)**

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Models;
using QonaqHotels.Services;

namespace QonaqHotels.Tests;

public class LoginServiceTests
{
    private static HotelDbContext CreateContext(string name)
    {
        var opts = new DbContextOptionsBuilder<HotelDbContext>()
            .UseInMemoryDatabase(name).Options;
        return new HotelDbContext(opts);
    }

    private static async Task<User> SeedUser(
        HotelDbContext db,
        string login = "admin",
        string password = "Pass123!",
        bool isActive = true,
        DateOnly? dismissalDate = null,
        DateTime? lockedUntil = null,
        int failedAttempts = 0)
    {
        var country  = new Country  { Name = "Казахстан" };
        var city     = new City     { Name = "Астана", Country = country };
        var hotel    = new Hotel    { Name = "H", City = city, Address = "a", Stars = 3 };
        var position = new Position { Name = "администратор" };
        db.Countries.Add(country); db.Cities.Add(city);
        db.Hotels.Add(hotel); db.Positions.Add(position);
        await db.SaveChangesAsync();

        var emp = new Employee {
            FullName = "Тест Тестов", HotelId = hotel.Id, PositionId = position.Id,
            HireDate = new DateOnly(2024, 1, 1), DismissalDate = dismissalDate
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var hasher = new PasswordHasher<User>();
        var user = new User {
            Login = login, Role = "админ", IsActive = isActive,
            EmployeeId = emp.Id, FailedAttempts = failedAttempts,
            LockedUntil = lockedUntil
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // Review Focus #1: case-insensitive login
    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Admin")]
    public async Task AttemptLogin_CaseInsensitive_Succeeds(string loginInput)
    {
        using var db = CreateContext($"case-{loginInput}");
        await SeedUser(db, login: "admin", password: "Pass123!");
        var svc = new LoginService(db, new PasswordHasher<User>());

        var result = await svc.AttemptLoginAsync(loginInput, "Pass123!");

        Assert.Equal(LoginResult.Success, result.Outcome);
    }

    // Review Focus #2: non-existent user returns generic error
    [Fact]
    public async Task AttemptLogin_UserNotFound_ReturnsGenericError()
    {
        using var db  = CreateContext("not-found");
        var svc = new LoginService(db, new PasswordHasher<User>());

        var result = await svc.AttemptLoginAsync("nobody", "any");

        Assert.Equal(LoginResult.InvalidCredentials, result.Outcome);
        Assert.Equal("Неверный логин или пароль", result.ErrorMessage);
    }

    [Fact]
    public async Task AttemptLogin_WrongPassword_IncrementsCounter()
    {
        using var db = CreateContext("incr-counter");
        var user = await SeedUser(db);
        var svc  = new LoginService(db, new PasswordHasher<User>());

        await svc.AttemptLoginAsync("admin", "wrong");

        var updated = await db.Users.FindAsync(user.Id);
        Assert.Equal(1, updated!.FailedAttempts);
    }

    [Fact]
    public async Task AttemptLogin_FiveWrongPasswords_LocksAccount()
    {
        using var db = CreateContext("lockout");
        var user = await SeedUser(db, failedAttempts: 4);
        var svc  = new LoginService(db, new PasswordHasher<User>());

        var result = await svc.AttemptLoginAsync("admin", "wrong");

        Assert.Equal(LoginResult.InvalidCredentials, result.Outcome);
        var updated = await db.Users.FindAsync(user.Id);
        Assert.NotNull(updated!.LockedUntil);
        Assert.True(updated.LockedUntil > DateTime.UtcNow);
        Assert.Equal(0, updated.FailedAttempts);
    }

    [Fact]
    public async Task AttemptLogin_Locked_ReturnsLockoutError()
    {
        using var db = CreateContext("locked");
        await SeedUser(db, lockedUntil: DateTime.UtcNow.AddMinutes(10));
        var svc = new LoginService(db, new PasswordHasher<User>());

        var result = await svc.AttemptLoginAsync("admin", "Pass123!");

        Assert.Equal(LoginResult.LockedOut, result.Outcome);
        Assert.NotNull(result.MinutesRemaining);
    }

    [Fact]
    public async Task AttemptLogin_Inactive_ReturnsDisabledError()
    {
        using var db = CreateContext("inactive");
        await SeedUser(db, isActive: false);
        var svc = new LoginService(db, new PasswordHasher<User>());

        var result = await svc.AttemptLoginAsync("admin", "Pass123!");

        Assert.Equal(LoginResult.Disabled, result.Outcome);
    }

    [Fact]
    public async Task AttemptLogin_DismissedEmployee_ReturnsDisabledError()
    {
        using var db = CreateContext("dismissed");
        await SeedUser(db, dismissalDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        var svc = new LoginService(db, new PasswordHasher<User>());

        var result = await svc.AttemptLoginAsync("admin", "Pass123!");

        Assert.Equal(LoginResult.Disabled, result.Outcome);
    }

    [Fact]
    public async Task AttemptLogin_Success_ClearsCounterAndUpdatesLastLogin()
    {
        using var db = CreateContext("success");
        var user = await SeedUser(db, failedAttempts: 2);
        var svc  = new LoginService(db, new PasswordHasher<User>());

        await svc.AttemptLoginAsync("admin", "Pass123!");

        var updated = await db.Users.FindAsync(user.Id);
        Assert.Equal(0, updated!.FailedAttempts);
        Assert.Null(updated.LockedUntil);
        Assert.NotNull(updated.LastLoginAt);
    }
}
```

- [ ] **Step 2: Run to confirm compile failure**

```bash
dotnet test tests/QonaqHotels.Tests/ --filter "LoginServiceTests" -v normal
```
Expected: compile error (`LoginService`, `LoginResult` not found).

- [ ] **Step 3: Create `src/QonaqHotels/Services/LoginService.cs`**

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QonaqHotels.Data;
using QonaqHotels.Models;

namespace QonaqHotels.Services;

public enum LoginResult { Success, InvalidCredentials, LockedOut, Disabled }

public record LoginAttemptResult(
    LoginResult Outcome,
    User?       User,
    string?     ErrorMessage,
    int?        MinutesRemaining
);

public class LoginService
{
    private readonly HotelDbContext     _db;
    private readonly PasswordHasher<User> _hasher;

    public LoginService(HotelDbContext db, PasswordHasher<User> hasher)
    {
        _db     = db;
        _hasher = hasher;
    }

    public async Task<LoginAttemptResult> AttemptLoginAsync(string login, string password)
    {
        var user = await _db.Users
            .AsNoTracking() // re-fetch for update below
            .Include(u => u.Employee)
            .FirstOrDefaultAsync(u => u.Login.ToLower() == login.ToLower());

        // Timing-safe: always hash-verify even when user is null
        if (user == null)
        {
            var dummy = new User();
            var dummyHash = _hasher.HashPassword(dummy, "dummy_qonaq_sentinel_2026");
            _hasher.VerifyHashedPassword(dummy, dummyHash, password);
            return new LoginAttemptResult(LoginResult.InvalidCredentials, null,
                "Неверный логин или пароль", null);
        }

        // Re-attach for mutations
        var tracked = await _db.Users.Include(u => u.Employee)
            .FirstAsync(u => u.Id == user.Id);

        // Check lockout before verifying password
        if (tracked.LockedUntil.HasValue && tracked.LockedUntil > DateTime.UtcNow)
        {
            var mins = (int)Math.Ceiling((tracked.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            return new LoginAttemptResult(LoginResult.LockedOut, null,
                $"Слишком много попыток. Попробуйте через {mins} мин.", mins);
        }

        var verify = _hasher.VerifyHashedPassword(tracked, tracked.PasswordHash, password);

        if (verify == PasswordVerificationResult.Failed)
        {
            tracked.FailedAttempts++;
            if (tracked.FailedAttempts >= 5)
            {
                tracked.LockedUntil     = DateTime.UtcNow.AddMinutes(15);
                tracked.FailedAttempts  = 0;
            }
            await _db.SaveChangesAsync();
            return new LoginAttemptResult(LoginResult.InvalidCredentials, null,
                "Неверный логин или пароль", null);
        }

        // Password correct — check account status
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!tracked.IsActive ||
            (tracked.Employee.DismissalDate.HasValue &&
             tracked.Employee.DismissalDate <= today))
        {
            return new LoginAttemptResult(LoginResult.Disabled, null,
                "Учётная запись отключена. Обратитесь к администратору", null);
        }

        // Success
        tracked.FailedAttempts = 0;
        tracked.LockedUntil    = null;
        tracked.LastLoginAt    = DateTime.UtcNow;

        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
            tracked.PasswordHash = _hasher.HashPassword(tracked, password);

        await _db.SaveChangesAsync();
        return new LoginAttemptResult(LoginResult.Success, tracked, null, null);
    }
}
```

- [ ] **Step 4: Register LoginService and PasswordHasher in `Program.cs`**

```csharp
// After builder.Services.AddDbContext<...>:
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<PasswordHasher<User>>();
```

Add `using QonaqHotels.Models;` and `using Microsoft.AspNetCore.Identity;` at the top of Program.cs.

- [ ] **Step 5: Run tests**

```bash
dotnet test tests/QonaqHotels.Tests/ --filter "LoginServiceTests"
```
Expected: all 8 tests pass.

- [ ] **Step 6: Write `src/QonaqHotels/Pages/Login.cshtml.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using QonaqHotels.Services;
using System.Security.Claims;

namespace QonaqHotels.Pages;

[EnableRateLimiting("login")]
public class LoginModel : PageModel
{
    private readonly LoginService _loginService;

    public LoginModel(LoginService loginService) => _loginService = loginService;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Введите логин")]
        [MaxLength(50)]
        public string Login { get; set; } = "";

        [Required(ErrorMessage = "Введите пароль")]
        public string Password { get; set; } = "";

        public bool RememberMe { get; set; }
    }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Admin/Index");
        ViewData["ReturnUrl"] = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!ModelState.IsValid)
            return Page();

        var result = await _loginService.AttemptLoginAsync(Input.Login, Input.Password);

        if (result.Outcome != LoginResult.Success)
        {
            ErrorMessage = result.ErrorMessage;
            Input.Password = "";
            return Page();
        }

        var user = result.User!;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name,           user.Employee.FullName),
            new(ClaimTypes.Role,           user.Role),
            new("hotel_id",                user.Employee.HotelId.ToString())
        };
        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var props = new AuthenticationProperties
        {
            IsPersistent = Input.RememberMe,
            ExpiresUtc   = Input.RememberMe
                ? DateTimeOffset.UtcNow.AddDays(7)
                : DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            props);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectToPage("/Admin/Index");
    }
}
```

- [ ] **Step 7: Write `src/QonaqHotels/Pages/Login.cshtml`**

```cshtml
@page
@model LoginModel
@{
    Layout = null; // custom full-page layout
    ViewData["Title"] = "Вход для сотрудников";
}
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="utf-8" /><meta name="viewport" content="width=device-width,initial-scale=1" />
    <title>Вход — Qonaq Hotels</title>
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400&family=Montserrat:wght@600&display=swap" rel="stylesheet" />
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.css" />
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
</head>
<body>
<div class="login-page">
    <div class="login-card">
        <div class="logo-block">
            <div style="font-family:var(--font-heading);font-size:1.4rem;font-weight:600;color:var(--c-dark)">QONAQ</div>
            <div style="font-size:.65rem;letter-spacing:.15em;color:#888">HOTELS</div>
        </div>
        <h1>Вход для сотрудников</h1>

        @if (Model.ErrorMessage != null) {
            <div class="alert-error">@Model.ErrorMessage</div>
        }

        <form method="post" id="login-form" asp-antiforgery="true">
            <div class="form-group">
                <label asp-for="Input.Login">Логин</label>
                <input asp-for="Input.Login" type="text" autocomplete="username" />
                <span asp-validation-for="Input.Login" class="text-danger" style="font-size:.8rem"></span>
            </div>
            <div class="form-group">
                <label asp-for="Input.Password">Пароль</label>
                <div class="password-wrapper">
                    <input asp-for="Input.Password" type="password"
                           autocomplete="current-password"
                           data-capslock-warning="caps-warning"
                           id="password-input" />
                    <button type="button" class="btn-eye" data-toggle-password aria-label="Показать пароль">
                        <i class="bi bi-eye"></i>
                    </button>
                </div>
                <small class="caps-warning" id="caps-warning">Включён Caps Lock</small>
                <span asp-validation-for="Input.Password" class="text-danger" style="font-size:.8rem"></span>
            </div>
            <div class="form-group" style="display:flex;align-items:center;gap:.5rem">
                <input asp-for="Input.RememberMe" type="checkbox" id="remember-me" />
                <label for="remember-me" style="margin:0;font-size:.85rem">Запомнить меня</label>
            </div>
            <input type="hidden" asp-for="@ViewData["ReturnUrl"]" name="returnUrl" />
            <button type="submit" class="btn-primary" style="width:100%;margin-top:.5rem">Войти</button>
        </form>
        <p class="login-hint">Нет доступа или забыли пароль? Обратитесь к администратору сети.</p>
        <a href="/" class="back-link">← На главную</a>
    </div>
</div>
<script src="~/js/site.js" asp-append-version="true"></script>
</body>
</html>
```

---

### Task 11: Remaining pages

**Files:**
- Create: `src/QonaqHotels/Pages/Logout.cshtml` + `.cshtml.cs`
- Create: `src/QonaqHotels/Pages/AccessDenied.cshtml`
- Create: `src/QonaqHotels/Pages/Error.cshtml` + `.cshtml.cs`
- Create: `src/QonaqHotels/Pages/Search.cshtml`
- Create: `src/QonaqHotels/Pages/Admin/Index.cshtml` + `.cshtml.cs`

- [ ] **Step 1: Create `src/QonaqHotels/Pages/Logout.cshtml.cs`**

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QonaqHotels.Pages;

public class LogoutModel : PageModel
{
    public IActionResult OnGet()  => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Index");
    }
}
```

Create `src/QonaqHotels/Pages/Logout.cshtml`:
```cshtml
@page
@model LogoutModel
```
(Empty body — GET redirects immediately; POST is handled and redirects.)

- [ ] **Step 2: Create `src/QonaqHotels/Pages/AccessDenied.cshtml`**

```cshtml
@page
@{
    ViewData["Title"] = "Нет доступа";
}
<section class="section">
    <div class="section-inner" style="text-align:center;padding:4rem 2rem">
        <h1 style="font-size:4rem;color:var(--c-accent)">403</h1>
        <h2>Нет доступа</h2>
        <p>У вас нет прав для просмотра этой страницы.</p>
        <a href="/" class="btn-primary">На главную</a>
    </div>
</section>
```

- [ ] **Step 3: Create `src/QonaqHotels/Pages/Error.cshtml.cs`**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QonaqHotels.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public int StatusCode { get; set; }

    public void OnGet(int statusCode)
    {
        StatusCode = statusCode;
    }
}
```

Create `src/QonaqHotels/Pages/Error.cshtml`:
```cshtml
@page "/Error/{statusCode:int}"
@model ErrorModel
@{
    ViewData["Title"] = Model.StatusCode == 404 ? "Страница не найдена" : "Ошибка";
}
<section class="section">
    <div class="section-inner" style="text-align:center;padding:4rem 2rem">
        <h1 style="font-size:4rem;color:var(--c-accent)">@Model.StatusCode</h1>
        @if (Model.StatusCode == 404) {
            <h2>Страница не найдена</h2>
            <p>Возможно, она была удалена или вы перешли по неверной ссылке.</p>
        } else {
            <h2>Произошла ошибка</h2>
            <p>Что-то пошло не так. Попробуйте обновить страницу.</p>
        }
        <a href="/" class="btn-primary">На главную</a>
    </div>
</section>
```

- [ ] **Step 4: Create `src/QonaqHotels/Pages/Search.cshtml.cs`** (validates dates server-side — Review Focus #4)

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QonaqHotels.Pages;

public class SearchModel : PageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To   { get; set; }

    public string? DateError { get; private set; }

    public void OnGet()
    {
        if (From.HasValue && To.HasValue && To <= From)
            DateError = "Дата выезда должна быть позже даты заезда.";
    }
}
```

Create `src/QonaqHotels/Pages/Search.cshtml`:
```cshtml
@page
@model SearchModel
@{
    ViewData["Title"] = "Поиск номеров";
}
<section class="section">
    <div class="section-inner" style="text-align:center;padding:4rem 2rem">
        <h1>Поиск номеров</h1>
        @if (Model.DateError != null) {
            <p style="color:var(--c-error)">@Model.DateError</p>
        }
        <p style="color:#666">Поиск номеров скоро заработает.</p>
        <a href="/" class="btn-primary">На главную</a>
    </div>
</section>
```

- [ ] **Step 5: Create `src/QonaqHotels/Pages/Admin/Index.cshtml.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace QonaqHotels.Pages.Admin;

[Authorize]
public class IndexModel : PageModel
{
    public string FullName { get; private set; } = "";
    public string Role     { get; private set; } = "";

    public void OnGet()
    {
        FullName = User.Identity?.Name ?? "Сотрудник";
        Role     = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
    }
}
```

Create `src/QonaqHotels/Pages/Admin/Index.cshtml`:
```cshtml
@page
@model QonaqHotels.Pages.Admin.IndexModel
@{
    ViewData["Title"] = "Панель управления";
}
<h2>Здравствуйте, @Model.FullName!</h2>
<p style="color:#666">Роль: <strong>@Model.Role</strong></p>
<p style="margin-top:2rem;color:#888">Разделы панели появятся в следующих этапах разработки.</p>
```

- [ ] **Step 6: Build and full run**

```bash
dotnet build QonaqHotels.sln
ADMIN_INITIAL_PASSWORD=DevPass123! dotnet run --project src/QonaqHotels
```

- [ ] **Step 7: Review Focus #3 — verify Cache-Control on admin pages**

```bash
curl -sk -c /tmp/cj.txt \
  -d "Input.Login=admin&Input.Password=DevPass123!&__RequestVerificationToken=$(curl -sk https://localhost:5001/login | grep -o 'value="[^"]*"' | head -1 | cut -d'"' -f2)" \
  https://localhost:5001/login
curl -sk -b /tmp/cj.txt -I https://localhost:5001/admin | grep -i cache-control
```
Expected: `Cache-Control: no-store`

- [ ] **Step 8: Review Focus #5 — verify empty table resilience** by temporarily commenting out the DB call and replacing with an empty list, then visiting `/`.

---

### Task 12: Acceptance criteria check

Run through every item in spec §11 manually with the app running:

- [ ] `hotel_schema.sql` + `db/seed.sql` apply to a clean DB without error (Task 1 Step 3 already proved this)
- [ ] `https://localhost:5001/` opens, all 9 blocks visible with correct text
- [ ] Counters reflect DB: add a hotel via `psql`, reload — count increases
- [ ] "Войти" button visible on 360 px, 768 px, 1920 px (use browser DevTools responsive mode)
- [ ] Search form: set checkout = same as checkin → form should not submit (browser validation) and if submitted without JS → server returns form with validation error
- [ ] Login with `admin` + correct password → redirected to `/admin` with full name and role
- [ ] Login with `ADMIN` + correct password → same result
- [ ] Login with wrong password → stays on `/login`, sees "Неверный логин или пароль"
- [ ] 5 wrong passwords in a row → login blocked, `locked_until` set in DB
- [ ] Set `is_active = false` in DB: `psql -d gosti -c "UPDATE users SET is_active=false WHERE login='admin';"` → login fails with disabled message
- [ ] Restore: `UPDATE users SET is_active=true WHERE login='admin';`
- [ ] Open `/admin` in a new tab without a session → redirected to `/login?ReturnUrl=%2Fadmin`
- [ ] After login, redirected back to `/admin`
- [ ] Click "Выйти", then browser back button → admin page does NOT render (no-store header enforced)
- [ ] Check `users` table in DB: `password_hash` column shows a PBKDF2 hash (starts with `AQ`), no plaintext

- [ ] **Step 1: Run all tests**

```bash
dotnet test QonaqHotels.sln -v normal
```
Expected: all tests pass.

- [ ] **Step 2: Commit**

```bash
git init   # if not already done
git add db/schema.sql db/seed.sql shell.nix .gitignore QonaqHotels.sln src/ tests/
git commit -m "feat: Stage 1 — homepage, login, admin stub"
```

---

## Verification

```bash
# Full test run
dotnet test QonaqHotels.sln

# App smoke test (confirm 9 blocks, login, admin)
ADMIN_INITIAL_PASSWORD=DevPass123! dotnet run --project src/QonaqHotels
# Open https://localhost:5001 in browser

# DB state check
psql -h localhost -U postgres -d gosti -c "
  SELECT login, role, is_active,
         length(password_hash) > 20 AS hash_ok,
         failed_attempts, locked_until
  FROM users;"
```
