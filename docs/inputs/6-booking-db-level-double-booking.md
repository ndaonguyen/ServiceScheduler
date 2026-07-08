# Input: Booking DB-level double-booking constraint (schema)

> **Required input** for `/plan-issue`. The GitHub issue is still the source of truth for acceptance criteria; this file adds design links, brainstorming, and constraints the issue doesn't capture.

## 1. Issue
- Primary: `#6` — https://github.com/ndaonguyen/ServiceScheduler/issues/6
- Blocked by: `#3` (merged — created the `appointments` table this migration alters, with `scheduled_start`/`scheduled_end` as `timestamp with time zone` and `status` as `text`)
- Builds on: `#5` (merged — application-level availability narrowing; in normal single-request operation #5 already prevents overlaps, so this constraint is the **concurrency backstop**, not the primary path)
- Related: `#7` (retry-on-violation — the follow-up that catches the exclusion violation this slice introduces and turns it into a graceful `409`/retry; **not** this slice)

## 2. Design / Reference Links
- [PRD: Unified Service Scheduler — Appointment Booking](../prds/appointment-booking.md) — authoritative for this slice. In particular:
  - **§6 NFR-01** — the double-booking guarantee must hold under concurrent requests across multiple API instances, **enforced at the database level, not application code**. This is the whole reason for the slice: #5's app-level check has a check→insert (TOCTOU) race that only a DB constraint can close.
  - **§7 AC-03** — the exact mechanism: PostgreSQL `EXCLUDE USING gist` constraints on `(service_bay_id, tstzrange(scheduled_start, scheduled_end))` and `(technician_id, tstzrange(...))`, both filtered `WHERE status = 'Confirmed'` **so future non-confirmed statuses (e.g. cancelled) won't require altering the constraint**. Requires the `btree_gist` extension.
  - **§4 BR-03** — half-open intervals. `tstzrange(lower, upper)` defaults to `'[)'` bounds (inclusive lower, exclusive upper), which **is** the half-open semantics: an appointment ending at T and one starting at T do **not** overlap. This must match #5's `AppointmentOverlap` predicate (`start < end2 && start2 < end`) — same rule, now enforced in the DB.
  - **§10 Sequence Diagram** — this constraint is the "final race guard = EXCLUDE constraint" on the INSERT step; the "retry once with next candidate" branch is #7.
  - **Further Notes** — "The migration that creates the `appointments` table must first run `CREATE EXTENSION IF NOT EXISTS btree_gist;` before creating the `EXCLUDE USING gist` constraints." (Extension first, then constraints, in that order.)
  - **Testing Notes** — the guarantee is **not** exercisable via handler unit tests with fake repositories; verification is deferred to a future integration-test PRD (Testcontainers / real Postgres). **No new automated test is expected from this issue.**
- [CLAUDE.md](../../CLAUDE.md) — EF migrations are schema-of-record (`source/AppointmentScheduler.Infrastructure/Migrations/`). Add with `dotnet ef migrations add <Name> --project source/AppointmentScheduler.Infrastructure --startup-project source/AppointmentScheduler.Api`; commit the generated files. **Development auto-migrates + seeds** via `DbInitializer.MigrateAndSeedAsync` (`Program.cs`, `IsDevelopment()` only); **production** runs migrations as a deliberate deploy step (`.github/workflows/deploy.yaml`), never on startup.
- **Existing code this slice builds on:**
  - `source/AppointmentScheduler.Infrastructure/Migrations/20260708074519_BookingFoundation.cs` — the migration that created `appointments` (+ plain `service_bay_id` / `technician_id` indexes). #6's migration applies **on top** of this (and on top of `20260707095241_InitialCreate`).
  - `source/AppointmentScheduler.Infrastructure/Persistence/Configurations/AppointmentConfiguration.cs` — the EF mapping (snake_case columns, `status` via `HasConversion<string>()`). **Not changed** — the constraint is not expressible in the fluent API (see brainstorming).
  - `source/AppointmentScheduler.Application/Features/Booking/RequestAppointment.cs` — the handler. **Not changed** this slice (AC).

## 3. Brainstorming

**Raw-SQL migration, not fluent config.** `EXCLUDE USING gist` constraints and `CREATE EXTENSION` have **no first-class EF Core / Npgsql fluent API** (Npgsql exposes `HasMethod("gist")` for *indexes*, but not exclusion constraints). So this cannot be driven from `AppointmentConfiguration` and the model snapshot. Approach: scaffold an **empty** migration (EF generates no operations because the model didn't change) and hand-author the SQL:
```
dotnet ef migrations add BookingNoOverlapConstraints \
  --project source/AppointmentScheduler.Infrastructure --startup-project source/AppointmentScheduler.Api
```
Then edit the generated `Up`/`Down` to use `migrationBuilder.Sql(...)`:
```sql
-- Up (order matters: extension before the constraints, per PRD Further Notes)
CREATE EXTENSION IF NOT EXISTS btree_gist;

ALTER TABLE appointments
  ADD CONSTRAINT ex_appointments_bay_no_overlap
  EXCLUDE USING gist (service_bay_id WITH =, tstzrange(scheduled_start, scheduled_end) WITH &&)
  WHERE (status = 'Confirmed');

ALTER TABLE appointments
  ADD CONSTRAINT ex_appointments_technician_no_overlap
  EXCLUDE USING gist (technician_id WITH =, tstzrange(scheduled_start, scheduled_end) WITH &&)
  WHERE (status = 'Confirmed');
```
```sql
-- Down (drop the constraints; leaving the extension installed is safer than dropping it)
ALTER TABLE appointments DROP CONSTRAINT IF EXISTS ex_appointments_technician_no_overlap;
ALTER TABLE appointments DROP CONSTRAINT IF EXISTS ex_appointments_bay_no_overlap;
```
- `btree_gist` is what lets the scalar `service_bay_id`/`technician_id` (uuid) participate in a gist exclusion with the `=` operator alongside the range's `&&` (overlap) operator.
- `tstzrange(scheduled_start, scheduled_end)` uses default `'[)'` bounds ⇒ half-open ⇒ BR-03. Columns are already `timestamp with time zone`, so `tstzrange` applies directly.
- The partial `WHERE (status = 'Confirmed')` matches the text `status` column and implements AC-03's "only confirmed rows are constrained" so cancelled/other future statuses never trip it.

**Model-snapshot divergence is expected and fine.** Because the constraint lives only in raw SQL (not the EF model), the `AppDbContextModelSnapshot` won't mention it. That's the normal pattern for DB objects EF can't model: EF won't try to create or drop it in future migrations, so there's no drift risk — just don't expect it to appear in the snapshot. The plan should note this so a reviewer doesn't flag the "missing" snapshot entry.

**No handler change; the violation surfaces raw (by design).** With #5's app-level narrowing, normal sequential requests never hit the constraint. Only a genuine concurrent double-book trips it, raising Postgres error `23P01` (`exclusion_violation`) → an unhandled `DbUpdateException`/`PostgresException` → `500`. Per the issue AC this is **explicitly acceptable for this slice** (not a regression); `#7` adds the catch-and-retry that turns it into a graceful outcome. Do **not** add exception handling here.

**Apply/verify locally.** After committing, `dotnet ef database update` (or just run the API in Development — `DbInitializer` auto-migrates) against local Postgres (`docker-compose.yml`). Sanity-check manually: two overlapping confirmed rows on the same bay/tech inserted directly via SQL should be rejected; a touching-at-T pair should be accepted (half-open). This is a manual check only — no xUnit test (per PRD).

**Production note (surface in the plan).** `CREATE EXTENSION btree_gist` requires a role permitted to create extensions (superuser, or a role with the privilege / the extension pre-allow-listed on managed Postgres). The deploy-step migration must run under such a role, or `btree_gist` must be pre-installed. Worth flagging even though it's an ops concern.

## 4. Constraints & Non-goals

- **Constraints:**
  - Schema/migration **only** — one new EF migration under `source/AppointmentScheduler.Infrastructure/Migrations/`, hand-authored raw SQL via `migrationBuilder.Sql(...)`. No entity, `AppDbContext`, `AppointmentConfiguration`, handler, or Application change.
  - `CREATE EXTENSION IF NOT EXISTS btree_gist;` runs **before** the two constraints (PRD Further Notes).
  - Both constraints use `EXCLUDE USING gist (… WITH =, tstzrange(scheduled_start, scheduled_end) WITH &&) WHERE (status = 'Confirmed')` — half-open `tstzrange` (BR-03), confirmed-only (AC-03).
  - Must apply cleanly on top of `20260708074519_BookingFoundation` (and `InitialCreate`); commit the generated migration + designer files. The model snapshot is unchanged (expected).
  - No handler behavior change — a constraint violation surfacing as an unhandled exception is **acceptable** for this slice (issue AC).
  - **No new automated test** — DB-level guarantee isn't unit-testable with fakes; verification deferred to a future Testcontainers integration PRD. Existing unit/integration tests must still pass (they should — no code change).
- **Non-goals (explicitly deferred, not missing):**
  - Catching / retrying / translating the `exclusion_violation` (23P01) into a graceful `409` or next-candidate retry → **#7**.
  - Any change to #5's application-level availability narrowing (it stays; the constraint is the backstop for the concurrent case only).
  - Integration tests (Testcontainers / real Postgres) exercising the constraint → future PRD.
  - Constraints for any status other than `Confirmed`, or on any table other than `appointments`.
  - Provisioning/ops automation for the `btree_gist` extension privilege (flagged as a note, not built here).

---
Delete or move to an `archive/` subfolder once the plan PR merges.
