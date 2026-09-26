# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

A university project: a PostgreSQL database schema for a hotel chain ("гостиница"). It holds hotel operations plus commercial space rental. There is no application code yet. The whole project is [db/schema.sql](db/schema.sql). `gosti.session.sql` is an empty SQLTools scratch file.

Identifiers are English. Comments and all enum-like CHECK values are **Russian** (e.g. `'свободен'`, `'подтверждена'`, `'аренда'`). Keep new status values in Russian so they match the existing ones.

## Database / commands

The local connection (from `.vscode/settings.json`, used by the SQLTools extension) is PostgreSQL at `localhost:5432`, database `gosti`, user `postgres`.

```bash
# (re)create the schema
psql -h localhost -U postgres -d gosti -f db/schema.sql

# start from a clean database
psql -h localhost -U postgres -c 'DROP DATABASE IF EXISTS gosti' -c 'CREATE DATABASE gosti'
```

There is no migration tool, test suite or linter. To check a change, apply the schema to a fresh database.

**Known ordering bug:** `employees` references `positions(id)`, but `positions` is created after it. Running the file top to bottom fails until `positions` is moved above `employees`.

## Schema architecture

The file is split into domain sections by Russian comment headers: номера, сотрудники, гости, услуги, аренда, финансы, смены/зарплата, доступы.

Conventions used throughout:
- PKs are `int GENERATED ALWAYS AS IDENTITY` (`bigint` for `audit_log`). The exception is `contract_units`, which uses the composite PK `(contract_id, unit_id)`.
- Parent-child FKs use `ON DELETE CASCADE`. References to lookup and master data use `ON DELETE RESTRICT`.
- Statuses are `varchar` + `CHECK (... IN (...))`, not Postgres enums.

Cross-table design points that aren't obvious from a single table:
- **Double-booking prevention uses `btree_gist` exclusion constraints** (the extension is created at the top of the file):
  - `booking_rooms`: one room can't be in two active bookings over overlapping `[)` date ranges (`WHERE (is_active)`).
  - `contract_units`: the same rule for commercial units, over inclusive `[]` ranges.
  - `seasons`: seasons can't overlap. As a result, a date maps to at most one season, and so to one `rates.price_per_night` per room type.
  - `shifts`: one employee's shifts can't overlap. `hours_worked` is a generated stored column.
- **Booking flow:** `bookings` (planned dates, per hotel) → `booking_rooms` (which rooms, for which dates; soft-deactivated with `is_active`) → `stays` (actual check-in/out, tied to an employee). `service_orders` hang off `bookings` and copy `price_at_order`.
- **Rental flow:** `tenants` → `lease_contracts` → `contract_units` → `commercial_units` → `utility_meters` → `meter_readings`. `utility_tariffs` are priced by `meter_type` and `valid_from`.
- **Invoices are polymorphic:** each invoice has exactly one of `booking_id` or `contract_id`, enforced by a CHECK. The type is marked by `invoice_type` (`'проживание'` or `'аренда'`). `penalties` are added to overdue invoices.
- `users` is 1:1 with `employees` and holds the role used for access control. `audit_log` records INSERT/UPDATE/DELETE per table and record. No triggers fill it yet.
