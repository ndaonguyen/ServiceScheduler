# Input: Booking request validation & failure responses

> **Required input** for `/plan-issue`. The GitHub issue is still the source of truth for acceptance criteria; this file adds design links, brainstorming, and constraints the issue doesn't capture.

## 1. Issue
- Primary: `#4` — https://github.com/ndaonguyen/ServiceScheduler/issues/4
- Blocked by: `#3` (merged — happy-path foundation that built and *calls* the four query ports but does not yet branch on their failure results)
- Related (optional): `#5`, `#6`, `#7` (later slices of the same PRD; the `409` availability responses are theirs, not this one's)

## 2. Design / Reference Links
- [PRD: Unified Service Scheduler — Appointment Booking](../prds/appointment-booking.md) — authoritative for this slice. In particular:
  - **§5 Validation Rules** — VR-02 (`VEHICLE_NOT_FOUND`), VR-03 (`VEHICLE_NOT_OWNED_BY_CALLER`), VR-04 (`DEALERSHIP_NOT_FOUND`), VR-05 (`SERVICE_TYPE_NOT_FOUND`), VR-06 (`REQUESTED_START_IN_PAST`) with their HTTP statuses. **Authoritative for the status↔code mapping.**
  - **§8 API Contract → Error responses** — the exact `{ "code": "<STABLE_CODE>", "message": "<human-readable>" }` body shape, and the HTTP↔code table. These codes are a **stable machine-readable contract** — they must match §8 character-for-character.
  - **§10 Sequence Diagram** — this issue implements the guard steps the `#3` skeleton stubbed out: "Reject if requestedStart in the past (400)", the `404 SERVICE_TYPE_NOT_FOUND` / `404 DEALERSHIP_NOT_FOUND` branches, and the `403 NOT_OWNED` / `404 NOT_FOUND` ownership branch (diagram steps 8, 9, 11, 13). It does **not** touch the busy-set / narrow-to-free steps (16–18).
  - **§11 Acceptance Criteria** — AT-02 through AT-06 map 1:1 to the new handler unit tests this slice adds.
- [ADR-0001: Modular monolith](../adrs/0001-modular-monolith.md) — the guards branch only on results returned by the existing Booking-owned ports; no new cross-module reference is introduced.
- [CLAUDE.md](../../CLAUDE.md) — layer conventions; endpoints in `Endpoints/BookingEndpoints.cs`, handler in `Features/Booking/RequestAppointment.cs`, unit tests in `AppointmentScheduler.Application.Tests`.
- **Existing code the guards attach to (built by `#3`, already wired for this issue):**
  - `source/AppointmentScheduler.Application/Features/Booking/RequestAppointment.cs` — `RequestAppointmentHandler` (note: PRD/issue prose calls it "CreateAppointment"; the actual type is `RequestAppointment`/`RequestAppointmentHandler`). Today it null-forgives (`serviceType!`, `dealership!`), ignores the ownership result (`_ = await vehicleOwnership.CheckAsync(...)`), and returns `RequestAppointmentResponse` directly on the happy path.
  - `source/AppointmentScheduler.Api/Endpoints/BookingEndpoints.cs` — maps the handler result to `Results.Created(...)`; currently has no failure branch.
  - The four ports **already return the failure signals** this slice consumes — see their XML docs, which explicitly name `#4`:
    - `IServiceTypeLookup.GetAsync` → `ServiceTypeInfo?` (`null` = not found → 404 `SERVICE_TYPE_NOT_FOUND`)
    - `IServiceBayLookup.ListByDealershipAsync` → `DealershipBays?` (`null` = dealership not found → 404 `DEALERSHIP_NOT_FOUND`)
    - `IVehicleOwnershipQuery.CheckAsync` → `enum VehicleOwnership { Owned, NotOwned, NotFound }` (→ proceed / 403 / 404)

## 3. Brainstorming

**This slice is small and additive by design.** `#3` deliberately fetched every value these guards need and left the branches out, so this issue is: (a) branch on results the handler already has in hand, and (b) give the handler a way to report a typed failure that the endpoint turns into the PRD §8 error body. No new ports, no new entities, no schema change, no seed change.

**The one real design decision: how the handler signals a typed failure to the endpoint.** Today `Handle` returns `RequestAppointmentResponse` directly and the endpoint wraps it in `Results.Created`. Guards need to surface an HTTP status + a stable `code`. Candidate approaches (the plan should pick one and justify it):
- **Result/discriminated union** — change the handler's response to something like `Result<RequestAppointmentResponse, BookingError>` (or a small sealed hierarchy), and have the endpoint `switch` on it to produce `Results.Created` vs `Results.Json(new { code, message }, statusCode)`. Keeps the mediator pipeline exception-free; failures are ordinary return values. Most aligned with "guard clause returning error codes" language in the issue.
- **Typed domain exception** (e.g. `BookingValidationException(code, status, message)`) thrown by the handler and translated by a single mapping point (endpoint filter or exception handler). Less plumbing in the response type, but uses exceptions for expected control flow.
- Recommendation to explore first: the **Result** shape — it makes AT-02..AT-06 assertable as return values in `Application.Tests` without catching exceptions, and matches the guard-clause framing. The plan should still confirm this reads cleanly against the existing `ISender`/`IRequestHandler<,>` mediator (which today assumes a plain `TResponse`).

**Error body + codes.** One small helper owns the `{ code, message }` shape and the status↔code table from §8 so the strings live in exactly one place (a stable contract). Human-readable `message` text is not asserted by the ACs — only `code` + HTTP status are — but should be sensible.

**Guard order matters for the AT tests.** Follow the §10 sequence so each AT is reachable in isolation: service-type lookup (VR-05) → past-start check (VR-06) → dealership/bays lookup (VR-04) → ownership check (VR-03/VR-02). "Strictly in the future" (VR-06) compares against the injected `TimeProvider` (`clock.GetUtcNow()`) already in the handler — do **not** use `DateTimeOffset.UtcNow` directly, so tests can pin now. Confirm precedence when a request violates two rules at once (e.g. unknown service type *and* past start) is acceptable per the ACs (each AT fixes only one bad input, so any consistent order passes, but the plan should state the chosen order).

**Ownership vs existence.** `VehicleOwnership.NotFound` → 404 `VEHICLE_NOT_FOUND` (VR-02/AT-02); `VehicleOwnership.NotOwned` → 403 `VEHICLE_NOT_OWNED_BY_CALLER` (VR-03/AT-03); `Owned` → proceed. The single `CheckAsync` call already distinguishes all three.

**Testing (unit only).** Extend `AppointmentScheduler.Application.Tests` with one test per AT-02..AT-06 against the existing fake query ports + fake `IAppointmentRepository`. Each asserts the returned error/status carries the exact PRD §8 `code`, and asserts **nothing was persisted** through the fake repository on a failure path. The existing happy-path test (AT-01/AT-13) must still pass unchanged — if the response type changes to a Result, update that test's assertion to unwrap the success case.

## 4. Constraints & Non-goals

- **Constraints:**
  - The five `code` strings and their HTTP statuses must match **PRD §8** exactly — they are a stable client-facing contract. `VEHICLE_NOT_FOUND`/`DEALERSHIP_NOT_FOUND`/`SERVICE_TYPE_NOT_FOUND` → 404, `VEHICLE_NOT_OWNED_BY_CALLER` → 403, `REQUESTED_START_IN_PAST` → 400.
  - Error body shape is exactly `{ "code": ..., "message": ... }` (PRD §8).
  - "In the future" compares against the handler's injected `TimeProvider`, not wall-clock, so AT-06 is deterministic.
  - Must not introduce a cross-module type reference (ADR-0001): guards consume only the existing Booking-owned ports.
  - **No EF migration** — this slice is handler/endpoint logic only; no entity or schema change (the ports and tables landed in `#3`'s `BookingFoundation` migration).
  - Test seam is **unit tests only** in `AppointmentScheduler.Application.Tests` (PRD AC-06). No new `Api.Tests` integration test. VR-01/AT-07 (401) is already satisfied structurally by `.RequireAuthorization()` on the endpoint and is **not** re-tested here.
- **Non-goals (explicitly deferred, not missing):**
  - The `409` availability responses — `NO_QUALIFIED_TECHNICIAN` (AT-08) and `NO_BAY_AVAILABLE` (AT-09) — require real availability/overlap computation and belong to **`#5`**. This issue's ACs stop at the 400/403/404 guards (AT-02..AT-06). Do **not** add 409 branches here (the handler still naively takes `Bays[0]`/`technicians[0]`).
  - Conflict/overlap detection and half-open-interval logic (AT-10/AT-11) → `#5`.
  - `EXCLUDE USING gist` DB constraint (AC-03) → `#6`; retry-on-violation → `#7`.
  - Re-verifying 401 (AT-07) with a new test — covered by the existing auth pipeline.
  - Any request-shape/format validation beyond the five VRs (e.g. malformed GUIDs, missing fields) — model-binding/framework concern, not in the ACs.

---
Delete or move to an `archive/` subfolder once the plan PR merges.
