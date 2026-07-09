# CLAUDE.md

Guidance for Claude Code working in this repo. Keep current as the project grows.

## What this is

A **backend .NET service** for scheduling vehicle service appointments across dealerships,
technicians, and service bays. **Clean Architecture** (Domain / Application / Infrastructure / Api
layers, as folders **inside each module project**) with **vertical-slice CQRS** over a lightweight
in-process mediator (`AppointmentScheduler.BuildingBlocks/Messaging`, no MediatR). No frontend — the
API is the product; the OpenAPI document at `/openapi/v1.json` is the client contract.

**Repository layout** ([ADR-0006](docs/adrs/0006-project-per-module-physical-structure.md)) —
project-per-module; module boundaries are **compiler-enforced** by the `ProjectReference` graph and
a NetArchTest suite:

```
src/
├─ Host/AppointmentScheduler.Api/                      # the only executable (Program.cs, Endpoints/, Security/, DbInitializer)
├─ BuildingBlocks/
│  ├─ AppointmentScheduler.BuildingBlocks/             # mediator + cross-cutting ports (ICurrentUser)
│  └─ AppointmentScheduler.BuildingBlocks.Persistence/ # shared AppDbContext, Identity, refresh tokens, Migrations/
└─ Modules/<Module>/
   ├─ AppointmentScheduler.<Module>/                   # Domain/ Application/ Infrastructure/ folders + <Module>Module.cs (DI)
   └─ AppointmentScheduler.<Module>.Contracts/         # the module's public surface (cross-module query ports; future events)
tests/{Application.Tests, Api.Tests, ArchitectureTests}
```

A module references its own `Contracts`, the `BuildingBlocks`, and **other modules' `Contracts`
only** — never another module's implementation. Namespaces are preserved from the pre-restructure
layout (`AppointmentScheduler.<Layer>.<Module>`), so they do **not** strictly mirror the new
project/folder — enforcement is by the reference graph + arch tests, not by namespace.

## Architecture principles

Two decisions constrain how new code is organised. Read the ADRs before designing anything
that crosses a module boundary.

- **Modular monolith** — [ADR-0001](docs/adrs/0001-modular-monolith.md), physical structure per
  [ADR-0006](docs/adrs/0006-project-per-module-physical-structure.md). One deployable process, split
  into feature modules (Booking, Fleet, Workforce, Catalog, …), **one class-library project each**.
  Each module owns its aggregates, handlers, EF configurations, its own Postgres schema
  (`booking`/`fleet`/`workforce`/`catalog`), endpoint wiring, and tables. Modules are designed to be
  **liftable into their own service** later without a rewrite.
  - **No cross-module type references** — now a **compile error**, not just convention: a module
    project does not reference another module's project (only its `Contracts`). Backed by
    `tests/AppointmentScheduler.ArchitectureTests` (NetArchTest), which fails the build on a violation.
  - Cross-module reads go through a **query port owned by the provider**, in that module's
    `Contracts` project (e.g. `IServiceBayLookup` in `Fleet.Contracts`); the owning module
    implements it in its own Infrastructure, and consumers reference the `Contracts` project only. A
    module-internal port (e.g. Booking's `IAppointmentRepository`) stays inside its module.
  - Cross-module writes and side effects go through **events** (below), never direct calls.
- **Events for inter-module communication** — [ADR-0002](docs/adrs/0002-events-for-inter-module-communication.md).
  Modules publish domain events (past tense, `AppointmentConfirmed`); interested modules react.
  Publishing uses the `IEventPublisher` port (Application) + a **post-commit outbox dispatcher**
  in Infrastructure. When a module is extracted, the in-process dispatcher is swapped for a
  message bus — event records remain the contract. Synchronous cross-module calls are only
  allowed for read-only queries where the caller must have the answer to continue, and never
  chained more than one deep.

> **Persistence:** EF Core + **PostgreSQL** (`Npgsql`). The single shared `AppDbContext` lives in
> `AppointmentScheduler.BuildingBlocks.Persistence` and references **no module** — module aggregates
> are reached via `Set<T>()`, and each module contributes its `IEntityTypeConfiguration<>` mappings
> (each setting the module's schema via `ToTable(name, "<schema>")`), discovered by scanning the
> assemblies in the host-supplied `ModuleConfigurations`. Booking's own persistence port
> (`IAppointmentRepository`) lives inside the Booking module. Connection string key
> `ConnectionStrings:AppDb` (appsettings + env). Local Postgres via `docker-compose.yml`.
>
> **Schema migrations:** owned by **EF Core migrations** (`dotnet-ef` in the tool manifest).
> Migration code lives in
> `src/BuildingBlocks/AppointmentScheduler.BuildingBlocks.Persistence/Migrations/`; the design-time
> context is built by `AppDbContextFactory` (in the **Host**, since it enumerates the module
> assemblies; override the connection with the `AppDb__ConnectionString` env var). The initial
> migration covers the **ASP.NET Core Identity** tables (Identity is EF-migration-native). Add a
> migration with `dotnet ef migrations add <Name> --project
> src/BuildingBlocks/AppointmentScheduler.BuildingBlocks.Persistence --startup-project
> src/Host/AppointmentScheduler.Api`; commit the generated files. Apply with
> `dotnet ef database update` (same project flags). **In Development the API auto-migrates and
> seeds** via `DbInitializer.MigrateAndSeedAsync` (`Program.cs`, guarded to `IsDevelopment()`); in
> production migrations run as a deliberate deploy step (see `.github/workflows/deploy.yaml`),
> never on startup. EF column mappings (snake_case) define the schema — there is no separate
> hand-written SQL to keep in sync.

> **Auth:** **JWT, cookie-transported**, **role-based** (RBAC). Design spec: `docs/authentication.md`.
> Both tokens travel **only as `httpOnly` cookies** — never in the response body or readable by JS
> (XSS-safe), with `Secure` (tracks request scheme), `SameSite=Strict` (CSRF-safe, no separate
> token). The **access token** (HS256 JWT, 15 min) is scoped `Path=/`; the **refresh token**
> (opaque, SHA-256-hashed in `refresh_tokens`, **fixed 7-day TTL — not reset on rotation**) is
> scoped `Path=/api/auth/refresh`. Cookie read/write/clear is centralized in `AuthCookies`
> (`AppointmentScheduler.Api/Security`); `AuthCookies` flags + `JwtBearerEvents.OnMessageReceived`
> (in `Program.cs`, pulls the access token from the cookie) are the only cookie-specific wiring.
> ASP.NET Core Identity is the **user store** (`AddIdentityCore<AppUser>` —
> `UserManager`/`RoleManager`/PBKDF2 hashing), `AppUser : IdentityUser` +
> `AppDbContext : IdentityDbContext<AppUser>`. `AuthEndpoints` exposes
> `POST /api/auth/register`, `POST /api/auth/login` (sets both cookies), `POST /api/auth/refresh`
> (rotates the refresh cookie; reuse → revoke whole chain → 401), and `POST /api/auth/logout`
> (authorized; revokes all the caller's refresh tokens + clears cookies). Identity is separate:
> `GET /api/profile/me` (`ProfileEndpoints`). `TokenService` signs HS256 tokens;
> `RefreshTokenService` (`AppointmentScheduler.BuildingBlocks.Persistence/Security`) issues/rotates/revokes.
> Config = `Jwt` section (`SigningKey` dev value in `appsettings.json`; override via
> `Jwt__SigningKey` secret in prod). `DbInitializer` seeds roles (`admin`, `user`) + optional dev
> admin (`Seed:Admin:*`). Endpoints opt in with `.RequireAuthorization()` /
> `.RequireAuthorization(p => p.RequireRole("admin"))`. Handlers read the caller via the
> `ICurrentUser` port (Application), implemented by `CurrentUser` (Api) over `HttpContext.User`.
> Integration tests bypass auth with a header-driven `TestAuthHandler`
> (`TestWebAppFactory.UseTestAuthentication`); `AuthEndpointsTests` exercises the real cookie/JWT
> pipeline end-to-end.

## Layers within a module

Each module is **one project** (`AppointmentScheduler.<Module>`) with the Clean-Architecture layers
as folders inside it, plus a sibling `AppointmentScheduler.<Module>.Contracts` project:

- **Domain** — entities, value objects, domain rules, domain events. No framework or
  persistence references. `Domain/<Module>/`.
- **Application** — CQRS request/handler pairs (`Application/Features/<Module>/<Verb>.cs`), event
  handlers (`Application/Features/<Module>/Events/`), and any module-internal port. The shared
  mediator (`Messaging/Mediator.cs`, `ISender`, `IRequestHandler<TRequest,TResponse>`) lives in
  `AppointmentScheduler.BuildingBlocks`, not per-module.
- **Infrastructure** — `IEntityTypeConfiguration<T>` per aggregate
  (`Infrastructure/Persistence/Configurations/`, each setting the module schema), repository / query-
  port implementations (`Infrastructure/<Module>/`) over the shared `AppDbContext`.
- **Contracts** (`AppointmentScheduler.<Module>.Contracts`) — the module's public surface for other
  modules: the cross-module query ports + DTOs it provides (and, later, the events it publishes).
- **Composition** — `<Module>Module.cs` exposes `Add<Module>Module(this IServiceCollection)` (scans
  its own handlers, registers its port implementations). The **Host**
  (`src/Host/AppointmentScheduler.Api`) owns `Program.cs`, endpoint groups
  (`Endpoints/<Module>Endpoints.cs`, calling `ISender.Send(...)`), security wiring (`Security/*`),
  and `DbInitializer`; it calls each `Add<Module>Module()`.

## Testing

- **Unit tests** — `tests/AppointmentScheduler.Application.Tests` cover handlers in isolation
  using xUnit + AwesomeAssertions (references the module projects + provider `Contracts` under test).
- **Integration tests** — `tests/AppointmentScheduler.Api.Tests` boot the API via
  `TestWebAppFactory` (WebApplicationFactory over `Program`), talk over HTTP, and default to an
  in-memory / test database. Auth is bypassed via a `TestAuthHandler` header
  (`X-Test-User`, `X-Test-Roles`) unless a test exercises the real pipeline (see
  `AuthEndpointsTests`).
- **Architecture tests** — `tests/AppointmentScheduler.ArchitectureTests` (NetArchTest) fail the
  build if a module depends on another module's Domain/Infrastructure, or if a module's Domain takes
  a persistence/web dependency. This is the runtime backstop for the compiler-enforced boundaries.

## Conventions

- **Namespaces are preserved** from the pre-restructure layout
  (`AppointmentScheduler.<Layer>.<Module>`) and do **not** mirror the new project/folder — new code
  should follow the namespace of the folder it sits beside, not invent a project-based one. One type
  per file where practical.
- **Endpoints** live in `Endpoints/<Module>Endpoints.cs` (in the Host) as an extension method
  `MapXxxEndpoints(this IEndpointRouteBuilder app)`; register in `Program.cs`.
- **Handler naming**: `Features/<Module>/<Verb><Aggregate>.cs` (e.g. `RequestAppointment.cs`),
  with the request record, response record, and handler in the same file.
- **Event naming**: past tense, one record per event, in `Features/<Module>/Events/`
  (e.g. `AppointmentConfirmed.cs`). Event handlers live in the **consuming** module under
  `Features/<ConsumerModule>/Events/On<Event>.cs`.
- **EF configurations** in the owning module's
  `Infrastructure/Persistence/Configurations/<Aggregate>Configuration.cs`, each setting the module
  schema via `ToTable("<table>", "<schema>")`; column names snake_case.
- **Nothing references Infrastructure/Api implementation across a layer or module boundary**; the
  dependency direction is enforced by the csproj `ProjectReference` graph
  (`BuildingBlocks*` ← `*.Contracts` ← `<Module>` ← `Host`).
- **Nothing in module A references module B's Domain/Infrastructure types** — a compile error, since
  A's project references only B's `Contracts`. See
  [ADR-0001](docs/adrs/0001-modular-monolith.md) and
  [ADR-0006](docs/adrs/0006-project-per-module-physical-structure.md).
