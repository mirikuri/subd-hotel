# Admin Validation, Identification & Live Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add client-side form validation, disambiguation info in tables/dropdowns, and live AJAX filtering to all four admin sections (Bookings, Employees, Users, Positions).

**Architecture:** Shared JS modules (`admin-validation.js`, `admin-filter.js`) provide reusable utilities. Each Index page renders its rows via a `_XxxRows.cshtml` partial and adds an `OnGetSearchAsync()` handler that returns that partial with filtered SQL results. A shared `_FilterBar.cshtml` partial (parametric model) renders the filter form consistently.

**Tech Stack:** ASP.NET Core 8 Razor Pages, Npgsql 8 (raw SQL), Bootstrap 5, Vanilla JS (no extra dependencies)

**Spec:** `docs/superpowers/specs/2026-10-12-admin-validation-search-filter-design.md`

## Global Constraints

- No new npm/NuGet dependencies — vanilla JS only, existing Bootstrap + Npgsql
- All SQL uses `NpgsqlParameter` — no string interpolation of user input ever
- Null filter params use `(object?)x ?? DBNull.Value` pattern (existing codebase style)
- Phone validation: optional fields — empty string is valid; non-empty must match `^\+[0-9]{7,15}$`
- Error spans injected after the input element with class `field-error text-danger small`
- `is-invalid` Bootstrap class added to inputs on error, removed on valid
- `_FilterBar.cshtml` lives at `Pages/Admin/_FilterBar.cshtml` (found by all admin sub-pages)
- Row partials live in their section folder: `Pages/Admin/Bookings/_BookingsRows.cshtml`, etc.
- `FilterBarModel` class lives in `Pages/Admin/FilterBarModel.cs`, namespace `QonaqHotels.Pages.Admin`

## Review Focus

- **SQL special characters in search box** (`%`, `_`, `'`) — parameterized queries prevent injection, but `LIKE '%' || @q || '%'` passes through `%`/`_` as wildcards. Expected: they are treated as literals, not wildcards. Fix: use `REPLACE(lower(@q), '%', '\%')` with `ESCAPE '\'` in each search handler.
- **Empty filter form submit** — all params null → should return all records (same as initial page load). Expected: tbody filled with full list, not empty.
- **DismissalDate after page load** — on Employee Edit, if `IsWorking` is unchecked the dismissal block appears, but `dismissalInput.max` is only set when `validateDismissalDate()` runs. Expected: `max` and `min` are set on `DOMContentLoaded`, not only on change. Fix: call `revalidate()` once in each init script.
- **Submit gate on page load** — `bindSubmitGate` calls `check()` once on bind. If a prefilled phone has invalid format (legacy data), the submit button starts disabled. Expected: button disabled until user fixes it. This is correct behavior, but make sure `check()` runs after all validators are registered.
- **Positions `OnGet()` is synchronous** — adding `OnGetSearchAsync()` next to a sync `OnGet()` is fine in Razor Pages; they coexist. No change to `OnGet()` needed.

---

## Task 1: Shared JS Validation Module

**Files:**
- Create: `src/QonaqHotels/wwwroot/js/admin-validation.js`

**Interfaces:**
- Produces: `window.adminValidation` object with `validatePhone(input)`, `validateName(input)`, `validateDateRange(startInput, endInput, allowPastStart)`, `validateDismissalDate(hireDateInput, dismissalInput)`, `bindSubmitGate(form, validators)`

- [ ] **Step 1: Create the file**

```javascript
// src/QonaqHotels/wwwroot/js/admin-validation.js
(function (exports) {
    function showError(input, msg) {
        let span = input.nextElementSibling;
        if (!span || !span.classList.contains('field-error')) {
            span = document.createElement('span');
            span.className = 'field-error text-danger small';
            input.after(span);
        }
        span.textContent = msg;
        input.classList.add('is-invalid');
    }

    function clearError(input) {
        const span = input.nextElementSibling;
        if (span && span.classList.contains('field-error')) span.textContent = '';
        input.classList.remove('is-invalid');
    }

    // Returns true if valid. Phone is optional — empty is valid.
    function validatePhone(input) {
        const v = input.value.trim();
        if (v === '') { clearError(input); return true; }
        if (/^\+[0-9]{7,15}$/.test(v)) { clearError(input); return true; }
        showError(input, 'Формат: +XXXXXXXXXXX (7–15 цифр после +)');
        return false;
    }

    // Returns true if valid. Minimum 2 non-whitespace characters.
    function validateName(input) {
        const v = input.value.trim();
        if (v.length >= 2) { clearError(input); return true; }
        showError(input, 'Минимум 2 символа');
        return false;
    }

    // Sets up date range validation (checkIn / checkOut pair).
    // allowPastStart: true for edit forms where existing past dates are allowed.
    // Returns the revalidate function (call it once after setup to set initial state).
    function validateDateRange(startInput, endInput, allowPastStart) {
        const today = new Date().toISOString().split('T')[0];
        if (!allowPastStart) startInput.min = today;

        function revalidate() {
            const s = startInput.value;
            const e = endInput.value;
            if (s) {
                const minEnd = new Date(s);
                minEnd.setDate(minEnd.getDate() + 1);
                endInput.min = minEnd.toISOString().split('T')[0];
            }
            if (s && e && e <= s) {
                showError(endInput, 'Дата выезда должна быть позже даты заезда');
                return false;
            }
            clearError(endInput);
            return true;
        }

        startInput.addEventListener('change', revalidate);
        endInput.addEventListener('change', revalidate);
        return revalidate;
    }

    // Sets up hire/dismissal date pair validation.
    // Returns the revalidate function.
    function validateDismissalDate(hireDateInput, dismissalInput) {
        function revalidate() {
            const today = new Date().toISOString().split('T')[0];
            dismissalInput.max = today;
            const h = hireDateInput.value;
            if (h) dismissalInput.min = h;
            const d = dismissalInput.value;
            if (d && h && d < h) {
                showError(dismissalInput, 'Дата увольнения не может быть раньше даты приёма');
                return false;
            }
            if (d && d > today) {
                showError(dismissalInput, 'Нельзя указать дату увольнения в будущем');
                return false;
            }
            clearError(dismissalInput);
            return true;
        }

        hireDateInput.addEventListener('change', revalidate);
        dismissalInput.addEventListener('change', revalidate);
        return revalidate;
    }

    // Disables submit button while any validator returns false.
    // validators: array of functions returning bool.
    function bindSubmitGate(form, validators) {
        const btn = form.querySelector('[type=submit]');
        function check() {
            const ok = validators.every(v => v());
            btn.disabled = !ok;
        }
        form.querySelectorAll('input, select, textarea').forEach(el => {
            el.addEventListener('input', check);
            el.addEventListener('change', check);
        });
        check(); // run once on bind
    }

    exports.adminValidation = { validatePhone, validateName, validateDateRange, validateDismissalDate, bindSubmitGate };
})(window);
```

- [ ] **Step 2: Verify the file was created at the right path**

Run: `ls src/QonaqHotels/wwwroot/js/`  
Expected: `admin-validation.js` and `site.js` both listed.

- [ ] **Step 3: Commit**

```bash
git add src/QonaqHotels/wwwroot/js/admin-validation.js
git commit -m "feat: add shared admin-validation.js (phone, name, date, submit gate)"
```

---

## Task 2: Shared AJAX Filter Module

**Files:**
- Create: `src/QonaqHotels/wwwroot/js/admin-filter.js`

**Interfaces:**
- Produces: `window.initFilter({ formId, tbodyId, searchUrl })`

- [ ] **Step 1: Create the file**

```javascript
// src/QonaqHotels/wwwroot/js/admin-filter.js
function initFilter({ formId, tbodyId, searchUrl }) {
    const form = document.getElementById(formId);
    const tbody = document.getElementById(tbodyId);
    const resetBtn = document.getElementById(formId + '-reset');
    let timer = null;

    function runSearch() {
        const params = new URLSearchParams();
        params.set('handler', 'Search');
        new FormData(form).forEach((v, k) => { if (v !== '') params.set(k, v); });

        tbody.innerHTML = '<tr><td colspan="99" class="text-center text-muted py-3">Загрузка…</td></tr>';

        fetch(searchUrl + '?' + params.toString(), {
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
        .then(function(r) { return r.text(); })
        .then(function(html) { tbody.innerHTML = html; })
        .catch(function() {
            tbody.innerHTML = '<tr><td colspan="99" class="text-center text-danger py-3">Ошибка загрузки</td></tr>';
        });
    }

    form.querySelectorAll('input[type=text]').forEach(function(input) {
        input.addEventListener('input', function() {
            clearTimeout(timer);
            timer = setTimeout(runSearch, 250);
        });
    });

    form.querySelectorAll('select').forEach(function(sel) {
        sel.addEventListener('change', runSearch);
    });

    resetBtn.addEventListener('click', function() {
        form.reset();
        runSearch();
    });
}
```

- [ ] **Step 2: Commit**

```bash
git add src/QonaqHotels/wwwroot/js/admin-filter.js
git commit -m "feat: add shared admin-filter.js AJAX live-search module"
```

---

## Task 3: FilterBarModel Class + `_FilterBar.cshtml` Partial

**Files:**
- Create: `src/QonaqHotels/Pages/Admin/FilterBarModel.cs`
- Create: `src/QonaqHotels/Pages/Admin/_FilterBar.cshtml`

**Interfaces:**
- Produces: `FilterBarModel` (namespace `QonaqHotels.Pages.Admin`) with `FormId` and `List<FilterField> Fields`
- Produces: `_FilterBar.cshtml` partial accepting `FilterBarModel` — call with `<partial name="_FilterBar" model="..."/>`

- [ ] **Step 1: Create FilterBarModel.cs**

```csharp
// src/QonaqHotels/Pages/Admin/FilterBarModel.cs
namespace QonaqHotels.Pages.Admin;

public class FilterBarModel
{
    public string FormId { get; set; } = "filter-form";
    public List<FilterField> Fields { get; set; } = [];
}

public class FilterField
{
    public string Type { get; set; } = "text";   // "text" | "select"
    public string Name { get; set; } = "";
    public string Placeholder { get; set; } = "";
    public List<(string Value, string Label)> Options { get; set; } = [];
}
```

- [ ] **Step 2: Create _FilterBar.cshtml**

```razor
@* src/QonaqHotels/Pages/Admin/_FilterBar.cshtml *@
@using QonaqHotels.Pages.Admin
@model FilterBarModel

<form id="@Model.FormId" class="row g-2 mb-3 align-items-end">
    @foreach (var f in Model.Fields)
    {
        <div class="col-auto">
            @if (f.Type == "select")
            {
                <select name="@f.Name" class="form-select form-select-sm">
                    <option value="">@f.Placeholder</option>
                    @foreach (var opt in f.Options)
                    {
                        <option value="@opt.Value">@opt.Label</option>
                    }
                </select>
            }
            else
            {
                <input type="text" name="@f.Name" class="form-control form-control-sm"
                       placeholder="@f.Placeholder" autocomplete="off" />
            }
        </div>
    }
    <div class="col-auto">
        <button type="button" id="@(Model.FormId)-reset"
                class="btn btn-outline-secondary btn-sm">Сбросить</button>
    </div>
</form>
```

- [ ] **Step 3: Verify build passes**

Run: `dotnet build src/QonaqHotels/QonaqHotels.csproj`  
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Commit**

```bash
git add src/QonaqHotels/Pages/Admin/FilterBarModel.cs src/QonaqHotels/Pages/Admin/_FilterBar.cshtml
git commit -m "feat: add FilterBarModel and _FilterBar partial for admin search bars"
```

---

## Task 4: Employee Form Validation (Create + Edit)

**Files:**
- Modify: `src/QonaqHotels/Pages/Admin/Employees/Create.cshtml`
- Modify: `src/QonaqHotels/Pages/Admin/Employees/Edit.cshtml`

**Interfaces:**
- Consumes: `window.adminValidation` from `admin-validation.js` (Task 1)

- [ ] **Step 1: Add `<script>` tags to `_AdminLayout.cshtml`**

`_AdminLayout.cshtml` is at `src/QonaqHotels/Pages/Shared/_AdminLayout.cshtml`. It currently ends with:
```html
<script src="~/js/site.js" asp-append-version="true"></script>
</body>
```

Change it to:
```html
<script src="~/js/site.js" asp-append-version="true"></script>
<script src="~/js/admin-validation.js" asp-append-version="true"></script>
<script src="~/js/admin-filter.js" asp-append-version="true"></script>
</body>
```

Note: The layout has no `@RenderSection("Scripts")`. This means page-level scripts must be written as plain inline `<script>` blocks at the bottom of the page content (not inside `@section Scripts`). The existing Bookings/Edit.cshtml already uses this pattern.

- [ ] **Step 2: Add validation script to Employee Create.cshtml**

Append this block at the end of `Create.cshtml` (after the closing `</div></div>`). Plain inline `<script>` — no `@section`:

```html
<script>
(function() {
    var v = window.adminValidation;
    var form = document.querySelector('form');
    var nameInput = form.querySelector('#FullName');
    var phoneInput = form.querySelector('#Phone');
    var hireInput = form.querySelector('#HireDate');

    hireInput.max = new Date(Date.now() + 30*24*60*60*1000).toISOString().split('T')[0];

    var validators = [
        function() { return v.validateName(nameInput); },
        function() { return v.validatePhone(phoneInput); }
    ];
    v.bindSubmitGate(form, validators);
})();
</script>
```

- [ ] **Step 3: Add validation script to Employee Edit.cshtml**

Append at the end of `Edit.cshtml` (plain inline `<script>`):

```html
<script>
(function() {
    var v = window.adminValidation;
    var form = document.querySelector('form');
    var nameInput = form.querySelector('#FullName');
    var phoneInput = form.querySelector('#Phone');
    var hireInput = form.querySelector('#HireDate');
    var dismissalInput = form.querySelector('#DismissalDate');

    hireInput.max = new Date(Date.now() + 30*24*60*60*1000).toISOString().split('T')[0];

    var revalidateDismissal = null;
    if (dismissalInput) {
        revalidateDismissal = v.validateDismissalDate(hireInput, dismissalInput);
        revalidateDismissal(); // set initial min/max
    }

    var validators = [
        function() { return v.validateName(nameInput); },
        function() { return v.validatePhone(phoneInput); },
        function() { return revalidateDismissal ? revalidateDismissal() : true; }
    ];
    v.bindSubmitGate(form, validators);
})();
</script>
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/QonaqHotels/QonaqHotels.csproj`  
Expected: `Build succeeded.`

- [ ] **Step 5: Manual verification**

Start the app (`dotnet run --project src/QonaqHotels`), log in as admin, open `/Admin/Employees/Create`:
- Type a single letter in ФИО → red error under field, Submit disabled
- Type valid name (2+ chars) → error clears, Submit re-enables
- Type `12345` in Phone → red error
- Type `+99455123456` → error clears
- Clear phone → no error (optional field)

- [ ] **Step 6: Commit**

```bash
git add src/QonaqHotels/Pages/Shared/_AdminLayout.cshtml \
        src/QonaqHotels/Pages/Admin/Employees/Create.cshtml \
        src/QonaqHotels/Pages/Admin/Employees/Edit.cshtml
git commit -m "feat: add client-side validation to Employee Create and Edit forms"
```

---

## Task 5: Booking Form Validation (Create + Edit)

**Files:**
- Modify: `src/QonaqHotels/Pages/Admin/Bookings/Create.cshtml`
- Modify: `src/QonaqHotels/Pages/Admin/Bookings/Edit.cshtml`

**Interfaces:**
- Consumes: `window.adminValidation` from `admin-validation.js` (Task 1)

- [ ] **Step 1: Add validation script to Booking Create.cshtml**

Append at the end of `Create.cshtml` (after the closing `</div></div>`):

```html
<script>
(function() {
    var v = window.adminValidation;
    var form = document.querySelector('form');
    var checkIn = form.querySelector('#CheckInPlan');
    var checkOut = form.querySelector('#CheckOutPlan');
    var revalidate = v.validateDateRange(checkIn, checkOut, false);
    revalidate();
    v.bindSubmitGate(form, [revalidate]);
})();
</script>
```

- [ ] **Step 2: Add validation script to Booking Edit.cshtml**

The existing `<script>` block in Edit.cshtml (cancel toggle) is already inline. Append a second inline `<script>` block after the existing one:

```html
<script>
(function() {
    var v = window.adminValidation;
    var form = document.querySelector('form');
    var checkIn = form.querySelector('#CheckInPlan');
    var checkOut = form.querySelector('#CheckOutPlan');
    var revalidate = v.validateDateRange(checkIn, checkOut, true);
    revalidate();
    v.bindSubmitGate(form, [revalidate]);
})();
</script>
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/QonaqHotels/QonaqHotels.csproj`  
Expected: `Build succeeded.`

- [ ] **Step 4: Manual verification**

Open `/Admin/Bookings/Create`:
- Set CheckIn to yesterday's date (use the date input) → field should not allow past if `min` is applied correctly; but note: HTML date inputs respect `min` visually but don't prevent direct typing. The JS `revalidate()` is called on `change` — manually type a past date, then change CheckOut → verify error appears.
- Set CheckIn = today, CheckOut = today → error under CheckOut
- Set CheckOut = tomorrow → error clears, Submit re-enables

- [ ] **Step 5: Commit**

```bash
git add src/QonaqHotels/Pages/Admin/Bookings/Create.cshtml \
        src/QonaqHotels/Pages/Admin/Bookings/Edit.cshtml
git commit -m "feat: add date range validation to Booking Create and Edit forms"
```

---

## Task 6: Identification Improvements in Tables and Dropdowns

**Files:**
- Modify: `src/QonaqHotels/Pages/Admin/Bookings/Index.cshtml` (add #ID under guest name)
- Modify: `src/QonaqHotels/Pages/Admin/Employees/Index.cshtml` (add #ID under full name)
- Modify: `src/QonaqHotels/Pages/Admin/Users/Index.cshtml.cs` (add hotel name to query and UserRow)
- Modify: `src/QonaqHotels/Pages/Admin/Users/Index.cshtml` (show hotel name under employee name)
- Modify: `src/QonaqHotels/Pages/Admin/Users/Create.cshtml.cs` (fix employee dropdown)
- Modify: `src/QonaqHotels/Pages/Admin/Users/Create.cshtml` (update option format — no change needed, SelectList handles it)

**Interfaces:**
- Modifies `UserRow` to add `public string HotelName { get; set; } = ""`

- [ ] **Step 1: Add `#ID` to Bookings Index**

In `Pages/Admin/Bookings/Index.cshtml`, change the guest cell from:
```razor
<td>@b.GuestName</td>
```
to:
```razor
<td>
    @b.GuestName
    <div class="text-muted small">#@b.Id</div>
</td>
```

- [ ] **Step 2: Add `#ID` to Employees Index**

In `Pages/Admin/Employees/Index.cshtml`, change the name cell from:
```razor
<td>@e.FullName</td>
```
to:
```razor
<td>
    @e.FullName
    <div class="text-muted small">#@e.Id</div>
</td>
```

- [ ] **Step 3: Add HotelName to UserRow and update Users Index SQL**

In `Pages/Admin/Users/Index.cshtml.cs`:

1. Add `HotelName` property to `UserRow`:
```csharp
public class UserRow
{
    public int Id { get; set; }
    public string EmployeeName { get; set; } = "";
    public string HotelName { get; set; } = "";    // ADD THIS
    public string Login { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LockedUntil { get; set; }
}
```

2. Update the SQL query in `OnGetAsync()`:
```csharp
using var cmd = new NpgsqlCommand(
    "SELECT u.id, e.full_name, h.name, u.login, u.role, u.last_login_at, u.is_active, u.locked_until " +
    "FROM users u " +
    "JOIN employees e ON e.id = u.employee_id " +
    "JOIN hotels h ON h.id = e.hotel_id " +
    "ORDER BY e.full_name", conn);
```

3. Update the reader to include the hotel name (columns shift by 1):
```csharp
Users.Add(new UserRow
{
    Id           = r.GetInt32(0),
    EmployeeName = r.GetString(1),
    HotelName    = r.GetString(2),    // NEW
    Login        = r.GetString(3),
    Role         = r.GetString(4),
    LastLoginAt  = r.IsDBNull(5) ? null : r.GetDateTime(5),
    IsActive     = r.GetBoolean(6),
    LockedUntil  = r.IsDBNull(7) ? null : r.GetDateTime(7)
});
```

- [ ] **Step 4: Show hotel name in Users Index view**

In `Pages/Admin/Users/Index.cshtml`, change the employee cell from:
```razor
<td>@u.EmployeeName</td>
```
to:
```razor
<td>
    @u.EmployeeName
    <div class="text-muted small">@u.HotelName</div>
</td>
```

- [ ] **Step 5: Fix employee dropdown in Users/Create**

In `Pages/Admin/Users/Create.cshtml.cs`, update the `LoadLists()` method:

```csharp
private void LoadLists()
{
    using var conn = _db.Open();
    var employees = new List<(int Id, string Name)>();
    using var cmd = new NpgsqlCommand(
        "SELECT e.id, e.full_name || ' — ' || h.name || ', ' || p.name " +
        "FROM employees e " +
        "JOIN hotels h ON h.id = e.hotel_id " +
        "JOIN positions p ON p.id = e.position_id " +
        "WHERE e.dismissal_date IS NULL " +
        "AND NOT EXISTS (SELECT 1 FROM users u WHERE u.employee_id = e.id) " +
        "ORDER BY e.full_name", conn);
    using var r = cmd.ExecuteReader();
    while (r.Read()) employees.Add((r.GetInt32(0), r.GetString(1)));
    Employees = new SelectList(employees.Select(x => new { x.Id, x.Name }), "Id", "Name");
}
```

This concatenates in SQL — result looks like "Иванов Иван — Qonaq Baku, Ресепшен" directly as the option text. No change needed in the `.cshtml` file.

- [ ] **Step 6: Verify build**

Run: `dotnet build src/QonaqHotels/QonaqHotels.csproj`  
Expected: `Build succeeded.`

- [ ] **Step 7: Manual verification**

- Open `/Admin/Bookings` — each row shows `#ID` in small gray text under guest name
- Open `/Admin/Employees` — each row shows `#ID` under name
- Open `/Admin/Users` — each row shows hotel name under employee name
- Open `/Admin/Users/Create` — employee dropdown shows "Иванов — Hotel, Position" format

- [ ] **Step 8: Commit**

```bash
git add src/QonaqHotels/Pages/Admin/Bookings/Index.cshtml \
        src/QonaqHotels/Pages/Admin/Employees/Index.cshtml \
        src/QonaqHotels/Pages/Admin/Users/Index.cshtml \
        src/QonaqHotels/Pages/Admin/Users/Index.cshtml.cs \
        src/QonaqHotels/Pages/Admin/Users/Create.cshtml.cs
git commit -m "feat: add ID disambiguation and hotel name to admin tables and employee dropdown"
```

---

## Task 7: Bookings Live Search & Filter

**Files:**
- Create: `src/QonaqHotels/Pages/Admin/Bookings/_BookingsRows.cshtml`
- Modify: `src/QonaqHotels/Pages/Admin/Bookings/Index.cshtml.cs`
- Modify: `src/QonaqHotels/Pages/Admin/Bookings/Index.cshtml`

**Interfaces:**
- Consumes: `FilterBarModel`, `FilterField` (Task 3); `initFilter` from `admin-filter.js` (Task 2)
- Modifies `IndexModel` to add `Hotels` select list and `OnGetSearchAsync(string? q, int? hotel, string? status, string? source)`

- [ ] **Step 1: Create `_BookingsRows.cshtml` partial**

Extract the `<tr>` content from the current `Index.cshtml` tbody into a new partial:

```razor
@* src/QonaqHotels/Pages/Admin/Bookings/_BookingsRows.cshtml *@
@using QonaqHotels.Pages.Admin.Bookings
@model IEnumerable<BookingRow>

@foreach (var b in Model)
{
    <tr>
        <td>
            @b.GuestName
            <div class="text-muted small">#@b.Id</div>
        </td>
        <td>@b.HotelName</td>
        <td>@b.CheckInPlan.ToString("dd.MM.yyyy")</td>
        <td>@b.CheckOutPlan.ToString("dd.MM.yyyy")</td>
        <td>
            @{
                var badgeClass = b.Status switch
                {
                    "новая"        => "bg-primary",
                    "подтверждена" => "bg-info text-dark",
                    "заселён"      => "bg-success",
                    "завершена"    => "bg-secondary",
                    "отменена"     => "bg-danger",
                    _              => "bg-light text-dark"
                };
            }
            <span class="badge @badgeClass">@b.Status</span>
        </td>
        <td>@(b.Source ?? "—")</td>
        <td>
            <a asp-page="Edit" asp-route-id="@b.Id" class="btn btn-outline-secondary btn-sm">Изменить</a>
            <a asp-page="Delete" asp-route-id="@b.Id" class="btn btn-outline-danger btn-sm">Удалить</a>
        </td>
    </tr>
}
```

- [ ] **Step 2: Update `Index.cshtml.cs` — add Hotels list, FilterBar, and OnGetSearchAsync**

Replace the full content of `Index.cshtml.cs` with:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Pages.Admin;

namespace QonaqHotels.Pages.Admin.Bookings;

public class BookingRow
{
    public int Id { get; set; }
    public string GuestName { get; set; } = "";
    public string HotelName { get; set; } = "";
    public DateOnly CheckInPlan { get; set; }
    public DateOnly CheckOutPlan { get; set; }
    public string Status { get; set; } = "";
    public string? Source { get; set; }
}

[Authorize(Policy = "RequireReceptionist")]
public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    public List<BookingRow> Bookings { get; set; } = [];
    public FilterBarModel FilterBar { get; set; } = null!;

    public async Task OnGetAsync()
    {
        using var conn = _db.Open();
        Bookings = await LoadBookings(conn, null, null, null, null);
        var hotels = await LoadHotelOptions(conn);
        FilterBar = new FilterBarModel
        {
            FormId = "bookings-filter",
            Fields =
            [
                new() { Type = "text",   Name = "q",      Placeholder = "Поиск по гостю…" },
                new() { Type = "select", Name = "hotel",  Placeholder = "Все отели",    Options = hotels },
                new() { Type = "select", Name = "status", Placeholder = "Все статусы",
                    Options = [("новая","новая"),("подтверждена","подтверждена"),
                               ("заселён","заселён"),("завершена","завершена"),("отменена","отменена")] },
                new() { Type = "select", Name = "source", Placeholder = "Все источники",
                    Options = [("сайт","сайт"),("стойка","стойка"),("телефон","телефон"),("агрегатор","агрегатор")] }
            ]
        };
    }

    public async Task<IActionResult> OnGetSearchAsync(string? q, int? hotel, string? status, string? source)
    {
        using var conn = _db.Open();
        var rows = await LoadBookings(conn, q, hotel, status, source);
        return Partial("_BookingsRows", rows);
    }

    private static async Task<List<BookingRow>> LoadBookings(
        Npgsql.NpgsqlConnection conn, string? q, int? hotel, string? status, string? source)
    {
        var safeQ = q is null ? null : "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        using var cmd = new NpgsqlCommand(
            "SELECT b.id, g.full_name, h.name, b.check_in_plan, b.check_out_plan, b.status, b.source " +
            "FROM bookings b " +
            "JOIN guests g ON g.id = b.guest_id " +
            "JOIN hotels h ON h.id = b.hotel_id " +
            "WHERE (@q::text IS NULL OR lower(g.full_name) LIKE lower(@q) ESCAPE '\\') " +
            "  AND (@hotel::int IS NULL OR b.hotel_id = @hotel) " +
            "  AND (@status::text IS NULL OR b.status = @status) " +
            "  AND (@source::text IS NULL OR b.source = @source) " +
            "ORDER BY b.booking_date DESC", conn);

        cmd.Parameters.AddWithValue("q",      (object?)safeQ  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hotel",  (object?)hotel  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("source", (object?)source ?? DBNull.Value);

        var rows = new List<BookingRow>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new BookingRow
            {
                Id           = r.GetInt32(0),
                GuestName    = r.GetString(1),
                HotelName    = r.GetString(2),
                CheckInPlan  = r.GetFieldValue<DateOnly>(3),
                CheckOutPlan = r.GetFieldValue<DateOnly>(4),
                Status       = r.GetString(5),
                Source       = r.IsDBNull(6) ? null : r.GetString(6)
            });
        }
        return rows;
    }

    private static async Task<List<(string Value, string Label)>> LoadHotelOptions(Npgsql.NpgsqlConnection conn)
    {
        var list = new List<(string Value, string Label)>();
        using var cmd = new NpgsqlCommand("SELECT id, name FROM hotels ORDER BY name", conn);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add((r.GetInt32(0).ToString(), r.GetString(1)));
        return list;
    }
}
```

- [ ] **Step 3: Update `Index.cshtml` — add filter bar and wire up JS**

Replace the full content of `Index.cshtml`:

```razor
@page
@model IndexModel
@{ ViewData["Title"] = "Брони"; }

<div class="d-flex justify-content-between align-items-center mb-3">
    <h4>Брони</h4>
    <a asp-page="Create" class="btn btn-primary btn-sm">+ Добавить</a>
</div>

<partial name="_FilterBar" model="Model.FilterBar" />

<table class="table table-bordered table-sm table-hover">
    <thead class="table-light">
        <tr>
            <th>Гость</th>
            <th>Отель</th>
            <th>Заезд</th>
            <th>Выезд</th>
            <th>Статус</th>
            <th>Источник</th>
            <th></th>
        </tr>
    </thead>
    <tbody id="bookings-tbody">
        <partial name="_BookingsRows" model="Model.Bookings" />
    </tbody>
</table>

<script>
    initFilter({ formId: 'bookings-filter', tbodyId: 'bookings-tbody', searchUrl: '/Admin/Bookings' });
</script>
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/QonaqHotels/QonaqHotels.csproj`  
Expected: `Build succeeded.`

- [ ] **Step 5: Manual verification**

Open `/Admin/Bookings`:
- Type a partial guest name → table updates without page reload, matching rows shown
- Select a status from dropdown → table filters immediately
- Click "Сбросить" → all rows return
- Type `%` in search box → no SQL error (ESCAPE clause handles it), no results or all results depending on data

- [ ] **Step 6: Commit**

```bash
git add src/QonaqHotels/Pages/Admin/Bookings/_BookingsRows.cshtml \
        src/QonaqHotels/Pages/Admin/Bookings/Index.cshtml \
        src/QonaqHotels/Pages/Admin/Bookings/Index.cshtml.cs
git commit -m "feat: add live search and filters to Bookings index"
```

---

## Task 8: Employees Live Search & Filter

**Files:**
- Create: `src/QonaqHotels/Pages/Admin/Employees/_EmployeesRows.cshtml`
- Modify: `src/QonaqHotels/Pages/Admin/Employees/Index.cshtml.cs`
- Modify: `src/QonaqHotels/Pages/Admin/Employees/Index.cshtml`

**Interfaces:**
- Modifies `IndexModel` to add `Hotels`, `Positions` select lists and `OnGetSearchAsync(string? q, int? hotel, int? position, string? status)`

- [ ] **Step 1: Create `_EmployeesRows.cshtml`**

```razor
@* src/QonaqHotels/Pages/Admin/Employees/_EmployeesRows.cshtml *@
@using QonaqHotels.Pages.Admin.Employees
@model IEnumerable<EmployeeRow>

@foreach (var e in Model)
{
    <tr>
        <td>
            @e.FullName
            <div class="text-muted small">#@e.Id</div>
        </td>
        <td>@e.HotelName</td>
        <td>@e.PositionName</td>
        <td>@e.HireDate.ToString("dd.MM.yyyy")</td>
        <td>
            @if (e.DismissalDate == null)
            {
                <span class="badge bg-success">Работает</span>
            }
            else
            {
                <span class="badge bg-secondary">Уволен @e.DismissalDate.Value.ToString("dd.MM.yyyy")</span>
            }
        </td>
        <td>
            <a asp-page="Edit" asp-route-id="@e.Id" class="btn btn-outline-secondary btn-sm">Изменить</a>
            @if (e.DismissalDate == null)
            {
                <a asp-page="Delete" asp-route-id="@e.Id" class="btn btn-outline-danger btn-sm">Уволить</a>
            }
            else
            {
                <form method="post" asp-page-handler="Rehire" asp-route-id="@e.Id" style="display:inline">
                    <button type="submit" class="btn btn-outline-success btn-sm">Восстановить</button>
                </form>
            }
        </td>
    </tr>
}
```

- [ ] **Step 2: Update `Index.cshtml.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Pages.Admin;

namespace QonaqHotels.Pages.Admin.Employees;

public class EmployeeRow
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string HotelName { get; set; } = "";
    public string PositionName { get; set; } = "";
    public DateOnly HireDate { get; set; }
    public DateOnly? DismissalDate { get; set; }
}

[Authorize(Policy = "RequireAdmin")]
public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    public List<EmployeeRow> Employees { get; set; } = [];
    public FilterBarModel FilterBar { get; set; } = null!;

    public async Task OnGetAsync()
    {
        using var conn = _db.Open();
        Employees = await LoadEmployees(conn, null, null, null, null);
        var hotels    = await LoadOptions(conn, "SELECT id, name FROM hotels ORDER BY name");
        var positions = await LoadOptions(conn, "SELECT id, name FROM positions ORDER BY name");
        FilterBar = new FilterBarModel
        {
            FormId = "employees-filter",
            Fields =
            [
                new() { Type = "text",   Name = "q",        Placeholder = "Поиск по имени…" },
                new() { Type = "select", Name = "hotel",    Placeholder = "Все отели",     Options = hotels },
                new() { Type = "select", Name = "position", Placeholder = "Все должности", Options = positions },
                new() { Type = "select", Name = "status",   Placeholder = "Все статусы",
                    Options = [("работает","Работает"),("уволен","Уволен")] }
            ]
        };
    }

    public async Task<IActionResult> OnGetSearchAsync(string? q, int? hotel, int? position, string? status)
    {
        using var conn = _db.Open();
        var rows = await LoadEmployees(conn, q, hotel, position, status);
        return Partial("_EmployeesRows", rows);
    }

    public async Task<IActionResult> OnPostRehireAsync(int id)
    {
        using var conn = _db.Open();
        using var emp = new NpgsqlCommand(
            "UPDATE employees SET dismissal_date = NULL WHERE id = @id", conn);
        emp.Parameters.AddWithValue("id", id);
        await emp.ExecuteNonQueryAsync();
        using var usr = new NpgsqlCommand(
            "UPDATE users SET is_active = true, locked_until = NULL, failed_attempts = 0 " +
            "WHERE employee_id = @id", conn);
        usr.Parameters.AddWithValue("id", id);
        await usr.ExecuteNonQueryAsync();
        return RedirectToPage();
    }

    private static async Task<List<EmployeeRow>> LoadEmployees(
        Npgsql.NpgsqlConnection conn, string? q, int? hotel, int? position, string? status)
    {
        var safeQ = q is null ? null : "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        using var cmd = new NpgsqlCommand(
            "SELECT e.id, e.full_name, h.name, p.name, e.hire_date, e.dismissal_date " +
            "FROM employees e " +
            "JOIN hotels h ON h.id = e.hotel_id " +
            "JOIN positions p ON p.id = e.position_id " +
            "WHERE (@q::text IS NULL OR lower(e.full_name) LIKE lower(@q) ESCAPE '\\') " +
            "  AND (@hotel::int IS NULL OR e.hotel_id = @hotel) " +
            "  AND (@position::int IS NULL OR e.position_id = @position) " +
            "  AND (@status::text IS NULL " +
            "       OR (@status = 'работает' AND e.dismissal_date IS NULL) " +
            "       OR (@status = 'уволен'   AND e.dismissal_date IS NOT NULL)) " +
            "ORDER BY e.full_name", conn);

        cmd.Parameters.AddWithValue("q",        (object?)safeQ    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hotel",    (object?)hotel    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("position", (object?)position ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status",   (object?)status   ?? DBNull.Value);

        var rows = new List<EmployeeRow>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new EmployeeRow
            {
                Id            = r.GetInt32(0),
                FullName      = r.GetString(1),
                HotelName     = r.GetString(2),
                PositionName  = r.GetString(3),
                HireDate      = r.GetFieldValue<DateOnly>(4),
                DismissalDate = r.IsDBNull(5) ? null : r.GetFieldValue<DateOnly>(5)
            });
        }
        return rows;
    }

    private static async Task<List<(string Value, string Label)>> LoadOptions(
        Npgsql.NpgsqlConnection conn, string sql)
    {
        var list = new List<(string Value, string Label)>();
        using var cmd = new NpgsqlCommand(sql, conn);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add((r.GetInt32(0).ToString(), r.GetString(1)));
        return list;
    }
}
```

- [ ] **Step 3: Update `Index.cshtml`**

```razor
@page
@model IndexModel
@{ ViewData["Title"] = "Сотрудники"; }

<div class="d-flex justify-content-between align-items-center mb-3">
    <h4>Сотрудники</h4>
    <a asp-page="Create" class="btn btn-primary btn-sm">+ Добавить</a>
</div>

<partial name="_FilterBar" model="Model.FilterBar" />

<table class="table table-bordered table-sm table-hover">
    <thead class="table-light">
        <tr>
            <th>ФИО</th>
            <th>Отель</th>
            <th>Должность</th>
            <th>Дата приёма</th>
            <th>Статус</th>
            <th></th>
        </tr>
    </thead>
    <tbody id="employees-tbody">
        <partial name="_EmployeesRows" model="Model.Employees" />
    </tbody>
</table>

<script>
    initFilter({ formId: 'employees-filter', tbodyId: 'employees-tbody', searchUrl: '/Admin/Employees' });
</script>
```

- [ ] **Step 4: Verify build and manual test**

Run: `dotnet build src/QonaqHotels/QonaqHotels.csproj`  
Expected: `Build succeeded.`

Open `/Admin/Employees`: search by name, filter by hotel, filter by status "уволен" → correct rows. "Восстановить" button still posts correctly (it's a form POST, not affected by AJAX).

- [ ] **Step 5: Commit**

```bash
git add src/QonaqHotels/Pages/Admin/Employees/_EmployeesRows.cshtml \
        src/QonaqHotels/Pages/Admin/Employees/Index.cshtml \
        src/QonaqHotels/Pages/Admin/Employees/Index.cshtml.cs
git commit -m "feat: add live search and filters to Employees index"
```

---

## Task 9: Users Live Search & Filter

**Files:**
- Create: `src/QonaqHotels/Pages/Admin/Users/_UsersRows.cshtml`
- Modify: `src/QonaqHotels/Pages/Admin/Users/Index.cshtml.cs`
- Modify: `src/QonaqHotels/Pages/Admin/Users/Index.cshtml`

**Interfaces:**
- Modifies `IndexModel` to add `OnGetSearchAsync(string? q, string? role, string? status)`
- `UserRow` already has `HotelName` from Task 6

- [ ] **Step 1: Create `_UsersRows.cshtml`**

```razor
@* src/QonaqHotels/Pages/Admin/Users/_UsersRows.cshtml *@
@using QonaqHotels.Pages.Admin.Users
@model IEnumerable<UserRow>

@foreach (var u in Model)
{
    <tr>
        <td>
            @u.EmployeeName
            <div class="text-muted small">@u.HotelName</div>
        </td>
        <td>@u.Login</td>
        <td>@u.Role</td>
        <td>@(u.LastLoginAt?.ToString("dd.MM.yyyy HH:mm") ?? "—")</td>
        <td>
            @if (!u.IsActive)
            {
                <span class="badge bg-secondary">Отключён</span>
            }
            else if (u.LockedUntil > DateTime.UtcNow)
            {
                <span class="badge bg-warning text-dark">Заблокирован</span>
            }
            else
            {
                <span class="badge bg-success">Активен</span>
            }
        </td>
        <td>
            <a asp-page="Edit" asp-route-id="@u.Id" class="btn btn-outline-secondary btn-sm">Изменить</a>
            @if (!u.IsActive)
            {
                <form method="post" asp-page-handler="Enable" asp-route-id="@u.Id" style="display:inline">
                    <button type="submit" class="btn btn-outline-success btn-sm">Включить</button>
                </form>
            }
            else
            {
                <a asp-page="Delete" asp-route-id="@u.Id" class="btn btn-outline-danger btn-sm">Отключить</a>
            }
        </td>
    </tr>
}
```

- [ ] **Step 2: Update `Index.cshtml.cs`**

Replace full content:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Pages.Admin;

namespace QonaqHotels.Pages.Admin.Users;

public class UserRow
{
    public int Id { get; set; }
    public string EmployeeName { get; set; } = "";
    public string HotelName { get; set; } = "";
    public string Login { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LockedUntil { get; set; }
}

[Authorize(Policy = "RequireAdmin")]
public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    public List<UserRow> Users { get; set; } = [];
    public FilterBarModel FilterBar { get; set; } = null!;

    public async Task<IActionResult> OnPostEnableAsync(int id)
    {
        using var conn = _db.Open();
        using var cmd = new NpgsqlCommand(
            "UPDATE users SET is_active = true, locked_until = NULL, failed_attempts = 0 WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
        return RedirectToPage();
    }

    public async Task OnGetAsync()
    {
        using var conn = _db.Open();
        Users = await LoadUsers(conn, null, null, null);
        FilterBar = new FilterBarModel
        {
            FormId = "users-filter",
            Fields =
            [
                new() { Type = "text",   Name = "q",      Placeholder = "Имя или логин…" },
                new() { Type = "select", Name = "role",   Placeholder = "Все роли",
                    Options = [("админ","Администратор"),("ресепшен","Ресепшен"),("бухгалтер","Бухгалтер")] },
                new() { Type = "select", Name = "status", Placeholder = "Все статусы",
                    Options = [("активен","Активен"),("заблокирован","Заблокирован"),("отключён","Отключён")] }
            ]
        };
    }

    public async Task<IActionResult> OnGetSearchAsync(string? q, string? role, string? status)
    {
        using var conn = _db.Open();
        var rows = await LoadUsers(conn, q, role, status);
        return Partial("_UsersRows", rows);
    }

    private static async Task<List<UserRow>> LoadUsers(
        Npgsql.NpgsqlConnection conn, string? q, string? role, string? status)
    {
        var safeQ = q is null ? null : "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        using var cmd = new NpgsqlCommand(
            "SELECT u.id, e.full_name, h.name, u.login, u.role, u.last_login_at, u.is_active, u.locked_until " +
            "FROM users u " +
            "JOIN employees e ON e.id = u.employee_id " +
            "JOIN hotels h ON h.id = e.hotel_id " +
            "WHERE (@q::text IS NULL " +
            "       OR lower(e.full_name) LIKE lower(@q) ESCAPE '\\' " +
            "       OR lower(u.login) LIKE lower(@q) ESCAPE '\\') " +
            "  AND (@role::text IS NULL OR u.role = @role) " +
            "  AND (@status::text IS NULL " +
            "       OR (@status = 'активен'      AND u.is_active = true  AND (u.locked_until IS NULL OR u.locked_until <= NOW())) " +
            "       OR (@status = 'заблокирован' AND u.is_active = true  AND u.locked_until > NOW()) " +
            "       OR (@status = 'отключён'     AND u.is_active = false)) " +
            "ORDER BY e.full_name", conn);

        cmd.Parameters.AddWithValue("q",      (object?)safeQ  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("role",   (object?)role   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);

        var rows = new List<UserRow>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new UserRow
            {
                Id           = r.GetInt32(0),
                EmployeeName = r.GetString(1),
                HotelName    = r.GetString(2),
                Login        = r.GetString(3),
                Role         = r.GetString(4),
                LastLoginAt  = r.IsDBNull(5) ? null : r.GetDateTime(5),
                IsActive     = r.GetBoolean(6),
                LockedUntil  = r.IsDBNull(7) ? null : r.GetDateTime(7)
            });
        }
        return rows;
    }
}
```

- [ ] **Step 3: Update `Index.cshtml`**

```razor
@page
@model IndexModel
@{ ViewData["Title"] = "Пользователи"; }

<div class="d-flex justify-content-between align-items-center mb-3">
    <h4>Пользователи</h4>
    <a asp-page="Create" class="btn btn-primary btn-sm">+ Добавить</a>
</div>

<partial name="_FilterBar" model="Model.FilterBar" />

<table class="table table-bordered table-sm table-hover">
    <thead class="table-light">
        <tr>
            <th>Сотрудник</th>
            <th>Логин</th>
            <th>Роль</th>
            <th>Последний вход</th>
            <th>Статус</th>
            <th></th>
        </tr>
    </thead>
    <tbody id="users-tbody">
        <partial name="_UsersRows" model="Model.Users" />
    </tbody>
</table>

<script>
    initFilter({ formId: 'users-filter', tbodyId: 'users-tbody', searchUrl: '/Admin/Users' });
</script>
```

- [ ] **Step 4: Verify build and test**

Run: `dotnet build src/QonaqHotels/QonaqHotels.csproj`  
Expected: `Build succeeded.`

Open `/Admin/Users`: search by login, filter by role "админ", filter by status "заблокирован". "Включить" POST button still works after AJAX update (POST goes to server, page reloads).

- [ ] **Step 5: Commit**

```bash
git add src/QonaqHotels/Pages/Admin/Users/_UsersRows.cshtml \
        src/QonaqHotels/Pages/Admin/Users/Index.cshtml \
        src/QonaqHotels/Pages/Admin/Users/Index.cshtml.cs
git commit -m "feat: add live search and filters to Users index"
```

---

## Task 10: Positions Live Search & Filter

**Files:**
- Create: `src/QonaqHotels/Pages/Admin/Positions/_PositionsRows.cshtml`
- Modify: `src/QonaqHotels/Pages/Admin/Positions/Index.cshtml.cs`
- Modify: `src/QonaqHotels/Pages/Admin/Positions/Index.cshtml`

**Interfaces:**
- Adds `PositionRow` class; adds `OnGetSearchAsync(string? q, string? role)` to `IndexModel`

- [ ] **Step 1: Create `_PositionsRows.cshtml`**

```razor
@* src/QonaqHotels/Pages/Admin/Positions/_PositionsRows.cshtml *@
@using QonaqHotels.Pages.Admin.Positions
@model IEnumerable<PositionRow>

@foreach (var p in Model)
{
    <tr>
        <td>@p.Name</td>
        <td>@p.Role</td>
        <td>
            <a asp-page="Edit" asp-route-id="@p.Id" class="btn btn-outline-secondary btn-sm">Изменить</a>
        </td>
    </tr>
}
```

- [ ] **Step 2: Update `Index.cshtml.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;
using QonaqHotels.Data;
using QonaqHotels.Pages.Admin;

namespace QonaqHotels.Pages.Admin.Positions;

public class PositionRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
}

[Authorize(Policy = "RequireAdmin")]
public class IndexModel : PageModel
{
    private readonly Db _db;
    public IndexModel(Db db) => _db = db;

    public List<PositionRow> Positions { get; set; } = [];
    public FilterBarModel FilterBar { get; set; } = null!;

    public void OnGet()
    {
        using var conn = _db.Open();
        Positions = LoadPositions(conn, null, null);
        FilterBar = new FilterBarModel
        {
            FormId = "positions-filter",
            Fields =
            [
                new() { Type = "text",   Name = "q",    Placeholder = "Поиск по названию…" },
                new() { Type = "select", Name = "role", Placeholder = "Все роли",
                    Options = [("админ","Администратор"),("ресепшен","Ресепшен"),("бухгалтер","Бухгалтер")] }
            ]
        };
    }

    public IActionResult OnGetSearch(string? q, string? role)
    {
        using var conn = _db.Open();
        return Partial("_PositionsRows", LoadPositions(conn, q, role));
    }

    private static List<PositionRow> LoadPositions(
        Npgsql.NpgsqlConnection conn, string? q, string? role)
    {
        var safeQ = q is null ? null : "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        using var cmd = new NpgsqlCommand(
            "SELECT id, name, role FROM positions " +
            "WHERE (@q::text IS NULL OR lower(name) LIKE lower(@q) ESCAPE '\\') " +
            "  AND (@role::text IS NULL OR role = @role) " +
            "ORDER BY name", conn);

        cmd.Parameters.AddWithValue("q",    (object?)safeQ ?? DBNull.Value);
        cmd.Parameters.AddWithValue("role", (object?)role  ?? DBNull.Value);

        var rows = new List<PositionRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new PositionRow { Id = r.GetInt32(0), Name = r.GetString(1), Role = r.GetString(2) });
        return rows;
    }
}
```

Note: `OnGet` stays synchronous (matches existing code). Search handler `OnGetSearch` (no `Async` suffix) maps correctly to `?handler=Search` in Razor Pages.

- [ ] **Step 3: Update `Index.cshtml`**

```razor
@page
@model IndexModel
@{ ViewData["Title"] = "Должности"; }

<div class="d-flex justify-content-between align-items-center mb-3">
    <h4>Должности</h4>
</div>

<partial name="_FilterBar" model="Model.FilterBar" />

<table class="table table-bordered table-sm table-hover">
    <thead class="table-light">
        <tr>
            <th>Название</th>
            <th>Роль в системе</th>
            <th></th>
        </tr>
    </thead>
    <tbody id="positions-tbody">
        <partial name="_PositionsRows" model="Model.Positions" />
    </tbody>
</table>

<script>
    initFilter({ formId: 'positions-filter', tbodyId: 'positions-tbody', searchUrl: '/Admin/Positions' });
</script>
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/QonaqHotels/QonaqHotels.csproj`  
Expected: `Build succeeded.`

Open `/Admin/Positions`: search by name, filter by role → rows update live.

- [ ] **Step 5: Commit**

```bash
git add src/QonaqHotels/Pages/Admin/Positions/_PositionsRows.cshtml \
        src/QonaqHotels/Pages/Admin/Positions/Index.cshtml \
        src/QonaqHotels/Pages/Admin/Positions/Index.cshtml.cs
git commit -m "feat: add live search and filters to Positions index"
```

---

## Final Verification

After all tasks complete:

1. **Build:** `dotnet build` — zero warnings, zero errors
2. **Employee form:** Create employee with 1-char name → Submit blocked. Invalid phone → Submit blocked. Valid data → Submit enabled. On Edit, set DismissalDate < HireDate → error shows.
3. **Booking form:** Create with CheckIn = yesterday (type manually) → on CheckOut change, error appears. Set CheckOut = CheckIn + 1 → error clears.
4. **Bookings list:** Filter by status "отменена" + partial guest name → AJAX result, URL doesn't change. Reset → all rows back.
5. **Employees list:** Filter "уволен" → only dismissed employees. Rehire button POST still works (page reloads normally after POST).
6. **Users list:** Filter by role "админ" → only admins. Enable POST button still works.
7. **User Create dropdown:** Shows "ФИО — Отель, Должность" format.
8. **Special chars:** Enter `%` in any search box → no SQL error, graceful result.
