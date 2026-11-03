# Admin Panel: Validation, Identification, Filtering & Search

**Date:** 2026-10-12  
**Author:** Azya

---

## Context

The admin panel manages Bookings, Employees, Users, and Positions. Currently:
- Validation is minimal (only StringLength, rest is DB-level CHECK constraints)
- No phone format validation, no date boundary checks in the UI
- Employee names can collide with no disambiguating info in dropdowns
- No search or filtering on any list page

This spec covers three coordinated improvements:
1. **Client-side validation** for phones, names, and dates
2. **Identification improvements** — disambiguation in tables and dropdowns
3. **Live AJAX filtering/search** — unified pattern across all sections

---

## Part 1: Client-side Validation

### Phone (E.164)

Format: `^\+[0-9]{7,15}$`  
Applied to: Employee.Phone in Create and Edit forms.  
Implementation: `admin-validation.js` → `validatePhone(input)` shows/hides an inline error `<span>` beneath the input. Submit button disabled while errors exist.

### Names

Pattern: at least 2 non-whitespace characters; no leading/trailing spaces (trimmed before submit).  
Applied to: Employee.FullName in Create and Edit.  
Function: `validateName(input)`.

### Dates

**Booking Create:**
- CheckInPlan: `min = today` (ISO format set via JS on page load)
- CheckOutPlan: `min = checkIn + 1 day`, updated dynamically when checkIn changes

**Booking Edit:**
- No `min` on CheckInPlan (existing bookings may have past dates)
- CheckOutPlan: `min = checkIn + 1 day`

**Employee Create/Edit:**
- HireDate: no min restriction (back-dating allowed); max = today + 30 days
- DismissalDate: `min = hireDate`, `max = today` — JS updates min when hireDate changes

Function: `validateDateRange(startInput, endInput, allowPastStart)`.

### Submit gate

All forms with validation get a JS event listener that runs all validators on each `input`/`change` event and toggles `disabled` on the submit button.

---

## Part 2: Identification in UI

### Employee dropdown (User Create form)

Change query to JOIN positions + hotels, format as:  
`Иванов Иван — Qonaq Baku, Ресепшен`

### Employees table

Already shows hotel + position columns. Add `#ID` in small muted text under the name for unambiguous reference in edge cases.

### Users table

Add hotel name in small muted text under the employee name (requires JOIN on hotels via employees in the Users index query).

### Bookings table

Add `#ID` in small muted text under guest name so receptionists can reference a specific booking.

---

## Part 3: Live AJAX Filtering & Search

### Architecture

**`admin-filter.js`** — shared module, called once per Index page:
```js
initFilter({ formId, tbodyId, searchUrl })
```
- Debounce 250 ms on text inputs, immediate on select changes
- Sends GET `?handler=Search&<all filter params>` 
- On response: replaces `tbodyId.innerHTML` with returned HTML
- Shows a spinner in the tbody while loading
- On empty filters: returns all records (same as initial page load)

**`_FilterBar.cshtml`** — shared partial, accepts a model with a list of field descriptors (type: text|select, name, placeholder, options for select). Renders a Bootstrap row with inputs and a "Сбросить" button that clears all fields and re-runs the search.

**Partial row views** — one per section, contains only `<tr>` elements (no table/tbody wrapper):
- `_BookingsRows.cshtml`
- `_EmployeesRows.cshtml`  
- `_UsersRows.cshtml`
- `_PositionsRows.cshtml`

Each Index.cshtml renders its initial table body by including its rows partial. `OnGetSearchAsync()` runs filtered SQL and returns `PartialView("_XxxRows", items)`.

### Filter fields per section

| Section    | Text search        | Select filters                               |
|------------|--------------------|----------------------------------------------|
| Bookings   | Guest name         | Hotel, Status, Source                        |
| Employees  | Full name          | Hotel, Position, Status (working/dismissed)  |
| Users      | Name or login      | Role, Status (active/locked/disabled)        |
| Positions  | Name               | Role                                         |

### SQL filtering pattern

Each `OnGetSearchAsync` builds the same base query as `OnGetAsync` but adds `WHERE` clauses for non-null/non-empty filter params:
```sql
WHERE (@q IS NULL OR lower(guest_name) LIKE '%' || lower(@q) || '%')
  AND (@hotel IS NULL OR b.hotel_id = @hotel)
  AND (@status IS NULL OR b.status = @status)
```
All parameters are passed as Npgsql `NpgsqlParameter` (no string interpolation).

---

## New Files

| File | Purpose |
|------|---------|
| `wwwroot/js/admin-validation.js` | validatePhone, validateName, validateDateRange, submit gate |
| `wwwroot/js/admin-filter.js` | initFilter AJAX module |
| `Pages/Admin/Shared/_FilterBar.cshtml` | Parametric filter bar partial |
| `Pages/Admin/Bookings/_BookingsRows.cshtml` | Row partial for bookings table |
| `Pages/Admin/Employees/_EmployeesRows.cshtml` | Row partial for employees table |
| `Pages/Admin/Users/_UsersRows.cshtml` | Row partial for users table |
| `Pages/Admin/Positions/_PositionsRows.cshtml` | Row partial for positions table |

## Modified Files

| File | Changes |
|------|---------|
| `Pages/Admin/Bookings/Index.cshtml` | Add _FilterBar, reference tbody partial, add JS init |
| `Pages/Admin/Bookings/Index.cshtml.cs` | Add OnGetSearchAsync, extract filter SQL |
| `Pages/Admin/Bookings/Create.cshtml` | Add phone/date JS hooks |
| `Pages/Admin/Bookings/Edit.cshtml` | Add date JS hooks |
| `Pages/Admin/Employees/Index.cshtml` | Add _FilterBar, reference tbody partial, add JS init |
| `Pages/Admin/Employees/Index.cshtml.cs` | Add OnGetSearchAsync, extract filter SQL |
| `Pages/Admin/Employees/Create.cshtml` | Add phone/name/date JS hooks |
| `Pages/Admin/Employees/Edit.cshtml` | Add phone/name/date JS hooks |
| `Pages/Admin/Users/Index.cshtml` | Add _FilterBar, reference tbody partial, add JS init |
| `Pages/Admin/Users/Index.cshtml.cs` | Add OnGetSearchAsync, extract filter SQL |
| `Pages/Admin/Positions/Index.cshtml` | Add _FilterBar, reference tbody partial, add JS init |
| `Pages/Admin/Positions/Index.cshtml.cs` | Add OnGetSearchAsync |
| `Pages/Admin/Users/Create.cshtml.cs` | Fix employee dropdown query to include hotel + position |
| `Pages/Admin/Users/Create.cshtml` | Update option text format |

---

## Verification

1. **Validation:** Open Employee Create, type one letter in FullName → error visible, submit disabled. Enter a phone without `+` → error. Enter valid phone → error clears, submit enabled. Set CheckIn to today, CheckOut to same day → error. Advance CheckOut by 1 day → submit enabled.
2. **Identification:** Open User Create, check that employee dropdown shows "Name — Hotel, Position" format. Open Employees list, verify `#ID` under names. Open Bookings list, verify `#ID` under guest names.
3. **Filtering:** Open Bookings index, type partial guest name → table updates without page reload. Select a status → table filters. Click "Сбросить" → all records return. Open Employees, filter by hotel and status simultaneously → correct results.
4. **Edge cases:** Empty search returns all records. SQL special chars in search box (e.g. `%`, `'`) don't break queries (parameterized). Filter state preserved in URL only within the session (AJAX, not GET form).
