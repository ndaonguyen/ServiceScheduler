# CLAUDE.md

Guidance for Claude Code working in this repo. Keep current as the project grows.

## What this is

A **backend .NET service** for scheduling vehicle service appointments across dealerships,
technicians, and service bays. **Clean Architecture** (Domain / Application / Infrastructure /
Api) with **vertical-slice CQRS** over a lightweight in-process mediator
(`AppointmentScheduler.Application/Messaging`, no MediatR). No frontend — the API is the product;
the OpenAPI document at `/openapi/v1.json` is the client contract.

## Architecture principles

Two decisions constrain how new code is organised. Read the ADRs before designing anything
that crosses a module boundary.

- **Modular monolith** — [ADR-0001](docs/adrs/0001-modular-monolith.md). One deployable
  process, split into feature modules (Booking, Fleet, Workforce, Catalog, …). Each module
  owns its aggregates, handlers, EF configurations, endpoint group, and tables. Modules are
  designed to be **liftable into their own service** later without a rewrite.
  - **No cross-module type references.** A handler in module A must not `using` module B's
    Domain or Infrastructure types.
  - Cross-module reads go through a **query port** in `Application/Abstractions/`; the owning
    module implements it in its own Infrastructure.
  - Cross-module writes and side effects go through **events** (below), never direct calls.
- **Events for inter-module communication** — [ADR-0002](docs/adrs/0002-events-for-inter-module-communication.md).
  Modules publish domain events (past tense, `AppointmentConfirmed`); interested modules react.
  Publishing uses the `IEventPublisher` port (Application) + a **post-commit outbox dispatcher**
  in Infrastructure. When a module is extracted, the in-process dispatcher is swapped for a
  message bus — event records remain the contract. Synchronous cross-module calls are only
  allowed for read-only queries where the caller must have the answer to continue, and never
  chained more than one deep.

> **Persistence:** EF Core + **PostgreSQL** (`Npgsql`). `AppDbContext` lives in
> `AppointmentScheduler.Infrastructure/Persistence`; slices persist through it via repositories
> whose ports live in Application (`Abstractions/I*Repository.cs`) and are implemented in
> Infrastructure. Connection string key `ConnectionStrings:AppDb` (appsettings + env). Local
> Postgres via `docker-compose.yml`.
>
> **Schema migrations:** owned by **EF Core migrations** (`dotnet-ef` in the tool manifest).
> Migration code lives in `source/AppointmentScheduler.Infrastructure/Migrations/`; the design-time
> context is built by `AppDbContextFactory` (override the connection with the
> `AppDb__ConnectionString` env var). The initial migration covers the **ASP.NET Core Identity**
> tables (Identity is EF-migration-native). Add a migration with
> `dotnet ef migrations add <Name> --project source/AppointmentScheduler.Infrastructure
> --startup-project source/AppointmentScheduler.Api`; commit the generated files. Apply with
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
> `RefreshTokenService` (`AppointmentScheduler.Infrastructure/Security`) issues/rotates/revokes.
> Config = `Jwt` section (`SigningKey` dev value in `appsettings.json`; override via
> `Jwt__SigningKey` secret in prod). `DbInitializer` seeds roles (`admin`, `user`) + optional dev
> admin (`Seed:Admin:*`). Endpoints opt in with `.RequireAuthorization()` /
> `.RequireAuthorization(p => p.RequireRole("admin"))`. Handlers read the caller via the
> `ICurrentUser` port (Application), implemented by `CurrentUser` (Api) over `HttpContext.User`.
> Integration tests bypass auth with a header-driven `TestAuthHandler`
> (`TestWebAppFactory.UseTestAuthentication`); `AuthEndpointsTests` exercises the real cookie/JWT
> pipeline end-to-end.

## Layers within a module

Each module (feature slice) is structured the same way across the 4 projects:

- **Domain** — entities, value objects, domain rules, domain events. No framework or
  persistence references. `Domain/<Module>/`.
- **Application** — CQRS request/handler pairs (`Features/<Module>/<Verb>.cs`), event handlers
  (`Features/<Module>/Events/`), ports (`Abstractions/I*.cs`) that Infrastructure implements,
  and the in-process mediator (`Messaging/Mediator.cs`, `ISender`,
  `IRequestHandler<TRequest,TResponse>`).
- **Infrastructure** — EF Core `AppDbContext`, `IEntityTypeConfiguration<T>` per aggregate
  (`Persistence/Configurations/`), repository implementations (`<Module>/`), Identity wiring,
  `RefreshTokenService`, `DbInitializer`, event dispatcher.
- **Api** — minimal-API endpoint groups (`Endpoints/<Module>Endpoints.cs`, one file per
  module), security wiring (`Security/*`), `Program.cs` composition. Endpoints call
  `ISender.Send(...)`; no business logic inline.

## Testing

- **Unit tests** — `tests/AppointmentScheduler.Application.Tests` cover handlers in isolation
  using xUnit + AwesomeAssertions.
- **Integration tests** — `tests/AppointmentScheduler.Api.Tests` boot the API via
  `TestWebAppFactory` (WebApplicationFactory over `Program`), talk over HTTP, and default to an
  in-memory / test database. Auth is bypassed via a `TestAuthHandler` header
  (`X-Test-User`, `X-Test-Roles`) unless a test exercises the real pipeline (see
  `AuthEndpointsTests`).

## Conventions

- **Namespaces mirror folders**; one type per file where practical.
- **Endpoints** live in `Endpoints/<Module>Endpoints.cs` as an extension method
  `MapXxxEndpoints(this IEndpointRouteBuilder app)`; register in `Program.cs`.
- **Handler naming**: `Features/<Module>/<Verb><Aggregate>.cs` (e.g. `RequestAppointment.cs`),
  with the request record, response record, and handler in the same file.
- **Event naming**: past tense, one record per event, in `Features/<Module>/Events/`
  (e.g. `AppointmentConfirmed.cs`). Event handlers live in the **consuming** module under
  `Features/<ConsumerModule>/Events/On<Event>.cs`.
- **EF configurations** in `Infrastructure/Persistence/Configurations/<Aggregate>Configuration.cs`;
  column names snake_case.
- **Nothing in Application references Infrastructure or Api**; enforce dependency direction by
  the csproj `ProjectReference` graph.
- **Nothing in module A references module B's Domain/Infrastructure types** — see
  [ADR-0001](docs/adrs/0001-modular-monolith.md).
