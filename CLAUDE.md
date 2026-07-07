# CLAUDE.md

Guidance for Claude Code working in this repo. Keep current as the project grows.

## What this is

A **Clean-Architecture .NET service** template, packaged as a `dotnet new` template
(`.template.config/template.json`, `sourceName: AppointmentScheduler`). Vertical-slice CQRS over a
**lightweight in-process mediator** (`AppointmentScheduler.Application/Messaging`, no MediatR), an optional
**React + Vite SPA** (`source/AppointmentScheduler.Api/ClientApp`), a Widgets appointmentscheduler, and `/health`. Ships
with GitHub Actions CI and Claude skills.

> **Persistence:** EF Core + **PostgreSQL** (`Npgsql`) for runtime queries only. `AppDbContext`
> lives in `AppointmentScheduler.Infrastructure/Persistence`; the Widgets slice persists through it via
> `EfWidgetRepository` (port = `IWidgetRepository` in Application). Connection string key
> `ConnectionStrings:AppDb` (appsettings + env). Local Postgres via `docker-compose.yml`.
>
> **Schema migrations:** owned by **EF Core migrations** (`dotnet-ef` in the tool manifest).
> Migration code lives in `source/AppointmentScheduler.Infrastructure/Migrations/`; the design-time context is
> built by `AppDbContextFactory` (override the connection with the `AppDb__ConnectionString` env
> var). Covers the Widgets slice **and ASP.NET Core Identity** tables (auth added Identity, which
> is EF-migration-native). Add a migration with `dotnet ef migrations add <Name> --project
> source/AppointmentScheduler.Infrastructure --startup-project source/AppointmentScheduler.Api`; commit the generated files.
> Apply with `dotnet ef database update` (same project flags). **In Development the API
> auto-migrates and seeds** via `DbInitializer.MigrateAndSeedAsync` (`Program.cs`, guarded to
> `IsDevelopment()`); in production migrations run as a deliberate deploy step (see
> `deploy.yaml`), never on startup. EF column mappings (`WidgetConfiguration`, snake_case) define
> the schema — there is no separate hand-written SQL to keep in sync.

> **Auth:** **JWT, cookie-transported**, **role-based** (RBAC). Design spec: `docs/authentication.md`.
> Both tokens travel **only as `httpOnly` cookies** — never in the response body or readable by JS
> (XSS-safe), with `Secure` (tracks request scheme), `SameSite=Strict` (CSRF-safe, no separate
> token). The **access token** (HS256 JWT, 15 min) is scoped `Path=/`; the **refresh token**
> (opaque, SHA-256-hashed in `refresh_tokens`, **fixed 7-day TTL — not reset on rotation**) is
> scoped `Path=/api/auth/refresh`. Cookie read/write/clear is centralized in `AuthCookies`
> (`AppointmentScheduler.Api/Security`); `AuthCookies` flags + `JwtBearerEvents.OnMessageReceived` (in `Program.cs`,
> pulls the access token from the cookie) are the only cookie-specific wiring. ASP.NET Core Identity
> is the **user store** (`AddIdentityCore<AppUser>` — `UserManager`/`RoleManager`/PBKDF2 hashing),
> `AppUser : IdentityUser` + `AppDbContext : IdentityDbContext<AppUser>`. `AuthEndpoints` exposes
> `POST /api/auth/register`, `POST /api/auth/login` (sets both cookies), `POST /api/auth/refresh`
> (rotates the refresh cookie; reuse → revoke whole chain → 401), and `POST /api/auth/logout`
> (authorized; revokes all the caller's refresh tokens + clears cookies). Identity is separate:
> `GET /api/profile/me` (`ProfileEndpoints`). `TokenService` signs HS256 tokens; `RefreshTokenService`
> (`AppointmentScheduler.Infrastructure/Security`) issues/rotates/revokes. Config = `Jwt` section (`SigningKey`
> dev value in `appsettings.json`; override via `Jwt__SigningKey` secret in prod). `DbInitializer`
> seeds roles (`admin`, `user`) + optional dev admin (`Seed:Admin:*`). Endpoints opt in with
> `.RequireAuthorization()` / `.RequireAuthorization(p => p.RequireRole("admin"))`. Handlers read
> the caller via the `ICurrentUser` port (Application), implemented by `CurrentUser` (Api) over
> `HttpContext.User`. Integration tests bypass auth with a header-driven `TestAuthHandler`
> (`TestWebAppFactory.UseTestAuthentication`); `AuthEndpointsTests` exercises the real cookie/JWT
> pipeline end-to-end.

> **Client framework:** template param `--client-framework` (`-cf`) = `react` (default) or
> `none` (API only). Driven by `ClientFramework` symbol → computed `UseReact` / `UseApiOnly`,
