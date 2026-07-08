# ADR-0005: PostgreSQL over document databases for the primary datastore

- **Status**: Accepted
- **Date**: 2026-07-08
- **Deciders**: nguyen.nguyendao
- **Related**: [ADR-0001](0001-modular-monolith.md), [ADR-0003](0003-appointment-as-scheduling-source-of-truth.md), [PRD — Appointment Booking](../prds/appointment-booking.md)

## Context

The system needs a persistent datastore. The dominant workload — the first slice and every module planned in [`../roadmap.md`](../roadmap.md) — has three properties that heavily constrain the choice:

1. **Scheduling correctness under concurrency is a hard requirement.** [PRD AC-03 / NFR-01](../prds/appointment-booking.md) mandates that no two confirmed appointments can occupy the same technician or bay for overlapping time ranges, even when multiple API instances process requests simultaneously. This is not an "eventual consistency is fine" domain — a double-booked technician is a business error, not a UX inconvenience. See also [ADR-0003](0003-appointment-as-scheduling-source-of-truth.md): reservation is a derived fact from `Appointment`, and the atomic guarantee lives at the storage layer.

2. **The domain is relational-shaped.** Every aggregate references others by ID and is queried as a set:
   - `Appointment` → `Vehicle`, `Dealership`, `Technician`, `ServiceBay`, `ServiceType` (five references).
   - `TechnicianQualification` is a textbook many-to-many join between `Technician` and `ServiceType`.
   - Availability queries are joins and set operations ("technicians at dealership X qualified for service Y with no overlapping appointment in window Z"), not document traversals.
   - No aggregate has a natural "nested document" shape that would benefit from being stored as a single JSON blob.

3. **No schemaless requirement.** The domain is stable — every entity has a well-known shape defined by [ADR-0001](0001-modular-monolith.md) module ownership. There is no scenario where fields on a `Technician` or an `Appointment` vary per row. Anything we would put in a "flexible schema" would live better as a proper column with a proper type.

Additional context that narrows the choice further:

- The auth stack (per `CLAUDE.md`) is **ASP.NET Core Identity**, whose EF Core provider is first-party and the only production-grade path. The MongoDB Identity provider is community-maintained and lags releases; using it would move a load-bearing dependency onto informal maintenance.
- EF Core + Npgsql, `dotnet ef` migrations, and `AppDbContext` are already wired. Postgres runs locally via `docker-compose.yml`. Migrations are the schema-change mechanism (see `CLAUDE.md`).
- .NET talent pool is comfortable with EF Core + relational databases; document-DB expertise is a smaller subset.
- Hosting Postgres is cheap and universally available (RDS, Cloud SQL, Azure Database, Supabase, self-hosted). No lock-in premium.

## Decision

We will use **PostgreSQL** as the primary datastore for the modular monolith, accessed via **EF Core (Npgsql provider)** with schema managed by `dotnet ef` migrations.

The concurrency guarantee for scheduling (no overlapping confirmed appointments on the same resource) is enforced by Postgres's `EXCLUDE USING gist` constraint on the `appointments` table, combined with the `tstzrange` type and the `btree_gist` extension. This is not incidental — it is a load-bearing feature of the chosen database, and one of the reasons Postgres was chosen over other relational options.

When (and only when) module extraction occurs per [ADR-0001](0001-modular-monolith.md), each extracted service moves with its tables to its own Postgres instance. This ADR does not commit us to Postgres forever for every module — a future service with a genuinely different workload (e.g. Reporting/Analytics per [`../roadmap.md`](../roadmap.md)) may pick a different store. But every module owning transactional business data uses Postgres unless it writes its own superseding ADR.

## Alternatives Considered

- **MongoDB (or any document database — CosmosDB, DynamoDB, Couchbase, etc.).** Rejected for four independent reasons, any one of which is sufficient:
  1. **No native equivalent to `EXCLUDE USING gist`.** To prevent overlapping confirmed appointments, we would have to (a) read the busy set in application code, (b) confirm no overlap, then (c) insert. To make that safe under concurrent inserts across multiple API instances, we would need either a serializable transaction (MongoDB has no true serializable isolation across documents), a multi-document ACID transaction on a replica set (available since 4.0, but with real performance overhead and only within a session), a unique-index trick over a bucketed time key (fragile, coarse, and forces artificial time discretization), or a pessimistic lock document per resource (reinventing a row lock, badly). Every option is strictly weaker or more complex than a single `EXCLUDE` constraint. The load-bearing correctness guarantee of the system would migrate from "declared and enforced by the database" to "written by us in application code" — which is exactly the wrong direction.
  2. **The domain has no document-shaped aggregates.** Nothing that gets read as a nested blob. Every entity is a small flat record with foreign keys to other small flat records, and every business query is a join. Storing this in a document store means either (a) denormalizing and losing referential integrity, or (b) doing joins in application code — reimplementing what a relational database already does.
  3. **ASP.NET Core Identity's document-DB support is community-maintained.** The user store is critical infrastructure. Depending on a community package for a feature the first-party stack ships with is added risk with no upside.
  4. **We are not exploiting document-DB strengths.** Document databases excel at: schemaless / rapidly-evolving schemas, aggregates read as one nested unit, write throughput beyond a single node, or geographically distributed reads. None of these describe the current or foreseeable workload. Choosing MongoDB here means paying the operational and modelling cost of a document store without collecting any of the benefits.

- **MySQL.** Rejected. Capable general-purpose relational DB, but no equivalent to `EXCLUDE USING gist` for range overlap constraints. Application-level overlap checks would carry the same downsides as MongoDB (see above), just with a relational schema. Postgres's advantage in this specific domain is decisive.

- **Microsoft SQL Server.** Rejected. Capable of similar constraints (via `CHECK` + application logic, or triggers), but no native GiST-style range exclusion — every equivalent is bespoke. Also introduces licensing cost (Express is limited; Standard is expensive) and pulls the stack toward the Microsoft ecosystem in a way that offers no benefit here.

- **SQLite.** Rejected. Excellent for embedded and single-process apps; single-writer model makes it wrong for a service designed to run behind a load balancer with multiple instances. Useful as a testing seam in specific cases — but not as the production store.

- **Event store (EventStoreDB / homegrown event sourcing on Postgres).** Not rejected but deferred. Event sourcing is a heavier commitment than the domain currently justifies — every read becomes a projection, every schema change is a rebuild. Revisit only if the roadmap's Audit or Reporting modules materially benefit from an event-sourced source of truth, and even then, most likely as a *complement* to Postgres (a materialized view or projection store), not a replacement.

## Consequences

- **Positive:**
  - The scheduling concurrency guarantee is declared once, at the schema layer, and enforced by the database engine itself. Application code cannot forget or circumvent it. This is the strongest correctness posture available for the requirement.
  - EF Core + Npgsql are mature, first-party for .NET, and align with the existing Identity + Auth setup (`CLAUDE.md`) — zero rework needed.
  - Migrations, seeding (`DbInitializer`), and testing seams are already wired against this stack. No migration cost.
  - Universally available in hosted form (RDS, Cloud SQL, Azure Database, Supabase, self-hosted on any Linux). No vendor lock-in.
  - Postgres has strong evolutionary headroom for adjacent needs: JSONB columns if a specific field genuinely needs schemaless behavior later, logical replication for future read replicas or CDC, extensions (`pg_trgm` for search, `PostGIS` for geo, `btree_gist` for the current constraint), and mature partitioning if a table outgrows a single row space.

- **Negative:**
  - Postgres is single-primary. Write scaling beyond one node requires either logical partitioning (Citus, application-level sharding) or splitting into more services, each with its own DB. This is a real ceiling — but it lives above any realistic near-term throughput for this domain (see [`../roadmap.md`](../roadmap.md); nothing there is a write-throughput monster).
  - The `EXCLUDE USING gist` constraint requires the `btree_gist` extension. The migration that creates the `appointments` table must run `CREATE EXTENSION IF NOT EXISTS btree_gist;` first. This is Postgres-specific and effectively vendor-locks the schema — moving off Postgres later means rewriting the concurrency guarantee, not just changing a connection string. See PRD Further Notes.
  - Some team members may not have Postgres-specific expertise (range types, `tstzrange`, gist indexes, EXCLUDE constraints). Mitigated by documentation in the PRD and this ADR; not a real blocker.

- **Follow-ups:**
  - The initial `appointments` migration must include `CREATE EXTENSION IF NOT EXISTS btree_gist;` as its first statement, then the `EXCLUDE USING gist` constraint (see PRD AC-03). This is a Postgres-specific migration step and worth calling out in the migration's PR description so reviewers know what to check.
  - Verify the `EXCLUDE` constraint's behavior against a real Postgres instance before the slice ships. Handler unit tests against fakes cannot exercise the schema constraint (see PRD Testing Notes) — verification is deferred to a future integration-test seam per [`../prds/appointment-booking.md#future-work`](../prds/appointment-booking.md).
  - Revisit this ADR only if: (a) a specific module surfaces a genuine document-DB workload (rapidly-evolving schema, nested aggregates read as one unit, or write-throughput requirements a single Postgres primary cannot meet after read-replica scaling), or (b) a hosting environment mandates a specific store (e.g. Cosmos DB in an Azure-only shop). Neither applies today.
  - Document-DB reconsideration for a *specific module* (not the primary store) is legitimate when the roadmap's Reporting or Audit modules land — read-heavy, denormalized workloads sometimes fit different stores. Each such decision would get its own scoped ADR that supplements, not supersedes, this one.
