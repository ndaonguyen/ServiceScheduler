# Plan: Booking — DB-level double-booking constraint (schema) (#6)

> **Issue**: https://github.com/ndaonguyen/ServiceScheduler/issues/6
> **Standalone**: this plan is executable without reading any other file.

## Goal
Add one EF Core migration that enforces the no-double-booking guarantee **in PostgreSQL**, independent
of application logic, so it holds even under concurrent requests across multiple API instances
(NFR-01). The migration enables `btree_gist` and adds two `EXCLUDE USING gist` constraints on the
`appointments` table — one per resource (bay, technician) — over the half-open confirmed-appointment
time range. **No application/handler change and no new automated test** (per the issue AC and PRD
Testing Notes); a raw constraint violation surfacing as an exception is explicitly acceptable until
#7 adds retry.

## Scope & Non-goals

**In scope (this slice):**
- One new hand-authored migration under `source/AppointmentScheduler.Infrastructure/Migrations/` that,
  in `Up()`:
  1. `CREATE EXTENSION IF NOT EXISTS btree_gist;` (first — PRD Further Notes);
  2. `EXCLUDE USING gist (service_bay_id WITH =, tstzrange(scheduled_start, scheduled_end) WITH &&) WHERE (status = 'Confirmed')`;
  3. the same for `technician_id`.
  And in `Down()`, drops the two constraints.
- Applies cleanly on top of `20260708074519_BookingFoundation` (and `InitialCreate`); generated
  migration + designer files committed.

**Out of scope (deferred — not missing):**
- Catching / translating / retrying the `exclusion_violation` (Postgres `23P01`) into a graceful
  `409` or next-candidate retry → **#7**. This slice lets it surface as an unhandled exception (`500`)
  — explicitly **not** a regression per the issue AC.
- Any change to #5's application-level availability narrowing (it stays; this constraint is the
  concurrency backstop for the check→insert race only).
- Any handler, endpoint, entity, `AppDbContext`, or `AppointmentConfiguration` change.
- Automated tests exercising the constraint (Testcontainers / real Postgres) → future integration-test
  PRD. No xUnit test is added here.
- Constraints for any status other than `Confirmed`, or on any table other than `appointments`.
- Provisioning the `btree_gist` extension privilege in production (flagged as Risk R1, not built here).

## Design Decisions (resolved during planning)

- **D1 — Hand-authored raw SQL on an otherwise-empty migration.** `EXCLUDE USING gist` and
  `CREATE EXTENSION` have **no EF Core / Npgsql fluent API** (Npgsql's `HasMethod("gist")` covers
  *indexes*, not exclusion constraints). So `AppointmentConfiguration` and the model **do not change**;
  `dotnet ef migrations add` produces an empty `Up`/`Down`, which is then hand-edited with
  `migrationBuilder.Sql(...)`. This is the standard pattern for DB objects EF can't model.
- **D2 — Model snapshot stays unchanged, and that is correct.** Because the constraint exists only in
  raw SQL, `AppDbContextModelSnapshot` won't reference it. EF therefore never tries to re-create or
  drop it in future migrations → no drift. Reviewers should expect the snapshot to be untouched (Risk
  R2).
- **D3 — Half-open, confirmed-only semantics match #5.** `tstzrange(scheduled_start, scheduled_end)`
  uses default `'[)'` bounds (inclusive lower, exclusive upper) ⇒ BR-03: an appointment ending at T
  does not conflict with one starting at T — identical to #5's `AppointmentOverlap` predicate. `WHERE
  (status = 'Confirmed')` (text column) implements AC-03 so non-confirmed statuses never trip it and a
  future `Cancelled` status needs no constraint change. `btree_gist` supplies the gist `=` operator
  class for the `uuid` resource columns.
- **D4 — No exception handling here.** With #5's narrowing, sequential requests never hit the
  constraint; only a genuine concurrent double-book raises `23P01`, surfacing as an unhandled
  `DbUpdateException` → `500`. Accepted for this slice (issue AC); #7 adds the catch/retry. Do **not**
  add try/catch or a `409` mapping now.
- **D5 — Manual verification only.** The guarantee isn't reachable through handler unit tests with
  fake repositories (PRD Testing Notes). Verification is `dotnet ef database update` against local
  Postgres + a manual SQL check; the future Testcontainers PRD automates it.

## Requirement Traceability
| Issue acceptance criterion | Plan section | Verification |
|---|---|---|
| Migration runs `CREATE EXTENSION IF NOT EXISTS btree_gist;` | Changes → Migration `Up()` (first statement) | Read generated `Up()`; `dotnet ef database update` succeeds |
| `EXCLUDE USING gist` on `(service_bay_id, tstzrange(...))` filtered `WHERE status = 'Confirmed'` | Changes → Migration `Up()` | `\d appointments` shows `ex_appointments_bay_no_overlap`; manual overlap insert rejected |
| `EXCLUDE USING gist` on `(technician_id, tstzrange(...))` filtered `WHERE status = 'Confirmed'` | Changes → Migration `Up()` | `\d appointments` shows `ex_appointments_technician_no_overlap` |
| Applies cleanly on top of #3's initial migration | Changes → Migration ordering | `dotnet ef database update` from a DB at `BookingFoundation` applies without error |
| No handler behavior change; violation may surface as unhandled exception | Scope → Out of scope; D4 | `git diff` touches only `Migrations/`; `dotnet test` still green |
| No new automated test expected | D5 | No test files added; existing suites pass unchanged |

## Changes

### Infrastructure (`source/AppointmentScheduler.Infrastructure/`)

**New migration** — scaffold empty, then hand-author the SQL.
> **Pattern to mimic**: existing `Migrations/20260708074519_BookingFoundation.cs` for file/namespace
> shape and the `migrationBuilder` usage; but unlike that one, this migration's body is
> `migrationBuilder.Sql(...)` calls (no `CreateTable`/`AddColumn`), because the change is a raw-SQL
> constraint the EF model doesn't express (D1).

Scaffold (produces empty `Up`/`Down` — the model didn't change):
```
dotnet ef migrations add BookingNoOverlapConstraints \
  --project source/AppointmentScheduler.Infrastructure --startup-project source/AppointmentScheduler.Api
```
Then edit the generated `…_BookingNoOverlapConstraints.cs`:
```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Extension first (PRD Further Notes) — supplies the gist operator class for uuid `=`.
    migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

    // BR-01/BR-03 + AC-03: no two *confirmed* appointments may overlap on the same bay.
    // tstzrange defaults to '[)' (half-open): touching at T does not conflict.
    migrationBuilder.Sql("""
        ALTER TABLE appointments
        ADD CONSTRAINT ex_appointments_bay_no_overlap
        EXCLUDE USING gist (service_bay_id WITH =, tstzrange(scheduled_start, scheduled_end) WITH &&)
        WHERE (status = 'Confirmed');
        """);

    // BR-02/BR-03 + AC-03: same for technicians.
    migrationBuilder.Sql("""
        ALTER TABLE appointments
        ADD CONSTRAINT ex_appointments_technician_no_overlap
        EXCLUDE USING gist (technician_id WITH =, tstzrange(scheduled_start, scheduled_end) WITH &&)
        WHERE (status = 'Confirmed');
        """);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    // Drop the constraints; leave btree_gist installed (dropping it could affect other objects).
    migrationBuilder.Sql("ALTER TABLE appointments DROP CONSTRAINT IF EXISTS ex_appointments_technician_no_overlap;");
    migrationBuilder.Sql("ALTER TABLE appointments DROP CONSTRAINT IF EXISTS ex_appointments_bay_no_overlap;");
}
```
- Commit **both** generated files (`…_BookingNoOverlapConstraints.cs` and its `.Designer.cs`). The
  `.Designer.cs` snapshot content is identical to `BookingFoundation`'s model (no model change) —
  that is expected (D2).
- **Do not** edit `AppDbContextModelSnapshot.cs` by hand; it legitimately gains no entry for the
  constraint.

**No other changes.** No entity, `AppDbContext`, `AppointmentConfiguration`, Application, or Api edit.

## Key Files
- `source/AppointmentScheduler.Infrastructure/Migrations/<timestamp>_BookingNoOverlapConstraints.cs` — new; the raw-SQL `Up`/`Down`.
- `source/AppointmentScheduler.Infrastructure/Migrations/<timestamp>_BookingNoOverlapConstraints.Designer.cs` — new; generated, committed as-is.
- (`AppDbContextModelSnapshot.cs` — **unchanged**, by design.)

## Testing & Verification
- `dotnet build -c Release` passes.
- `dotnet test -c Release` passes **unchanged** (no code change; the constraint isn't exercised by
  the unit/integration suites).
- **Migration authored + reviewed**: after scaffolding and hand-editing, read `Up()` — three
  `migrationBuilder.Sql` statements in the order extension → bay constraint → technician constraint;
  `Down()` drops both constraints. Confirm the migration's `.Designer.cs` / snapshot show **no** new
  model entries.
- **Apply against local Postgres** (`docker compose up` db, then either `dotnet ef database update
  --project source/AppointmentScheduler.Infrastructure --startup-project source/AppointmentScheduler.Api`
  or run the API in Development so `DbInitializer` auto-migrates). Then a manual SQL sanity check:
  - two overlapping `Confirmed` rows on the same `service_bay_id` → second INSERT rejected with
    `23P01 exclusion_violation`;
  - a pair where one ends exactly when the next starts (touching at T) → both accepted (half-open);
  - same checks for `technician_id`.
  (Manual only — no xUnit test, per PRD.)

## Branch & PR
- **This plan PR**: branch `plan/6`, title `docs: plan for booking db-level double-booking constraint (#6)`.
- **Implementation branch** (later, for `/implement-issue`): `feat/6-booking-db-level-double-booking`.
  (A `feat/db-level-double-booking` branch already exists locally; reuse it or rename to the standard
  `feat/6-…` form.)
- Implementation PR title: `feat: db-level double-booking constraint (#6)`, body closes the issue
  (`Closes #6`) and notes the follow-up retry slice (#7).

## Notes / Risks surfaced
- **R1 — `CREATE EXTENSION btree_gist` needs privilege in production.** It requires a role allowed to
  create extensions (superuser, or a role with the privilege / the extension pre-allow-listed on
  managed Postgres). Local `docker-compose` Postgres runs as superuser, so Dev auto-migrate is fine;
  the production deploy-step role must be able to create it (or `btree_gist` pre-installed). Flag in
  the PR for whoever owns the prod DB.
- **R2 — Empty EF scaffold / unchanged snapshot is expected.** `dotnet ef migrations add` emits an
  empty `Up`/`Down` here (no model delta); the body is hand-written. The model snapshot gains nothing.
  Call this out so review doesn't treat the empty scaffold or "missing" snapshot entry as a mistake.
- **R3 — Applying over pre-existing overlapping data would fail.** Adding an `EXCLUDE` constraint
  validates existing rows; if a dev database already holds overlapping `Confirmed` appointments the
  `ALTER TABLE` errors. #5's app-level narrowing and the small seed set make this unlikely, but a dev
  with hand-created overlaps may need to clear them (or drop/recreate the dev DB) before the migration
  applies. Not a concern for fresh databases or production.
- **R4 — This intentionally leaves a `500` on concurrent double-book.** Between this slice and #7, a
  genuine race returns an unhandled error rather than a clean `409`. Accepted by the issue AC;
  reviewers should not "fix" it here — that is #7's scope.
```
