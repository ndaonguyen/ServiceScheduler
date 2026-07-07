# Database & Auth

PostgreSQL persistence via **EF Core + Npgsql**. EF Core owns both runtime queries and the schema
(through **migrations**). The schema includes the **ASP.NET Core Identity** tables (users/roles)
plus the app's own aggregates.

| Concern | Owner |
|---------|-------|
| Runtime queries | EF Core + Npgsql (`AppDbContext`) |
| Schema (create/alter tables) | EF Core **migrations** (`source/AppointmentScheduler.Infrastructure/Migrations`) |
| Users / roles / login | ASP.NET Core Identity (tables in the same `AppDbContext`) |

The API auto-applies migrations + seeds roles **in Development only** (`Program.cs` →
`DbInitializer.MigrateAndSeedAsync`). In other environments run `dotnet ef database update` as a
deliberate deploy step (see CI/CD).

---

## Components

```
source/
├── AppointmentScheduler.Application/Abstractions/
│   └── ICurrentUser.cs             # authenticated-caller port
│                                   # (add I<Aggregate>Repository.cs per slice)
├── AppointmentScheduler.Infrastructure/
│   ├── Persistence/
│   │   ├── AppDbContext.cs         # IdentityDbContext<AppUser> + app DbSets
│   │   ├── AppUser.cs              # IdentityUser
│   │   ├── AppDbContextFactory.cs  # design-time factory for `dotnet ef`
│   │   ├── DbInitializer.cs        # Migrate + seed roles/admin (dev)
│   │   └── Configurations/         # one IEntityTypeConfiguration<T> per aggregate
│   ├── Migrations/                 # EF Core migrations (generated)
│   └── DependencyInjection.cs      # AddDbContext(UseNpgsql)
└── AppointmentScheduler.Api/
    ├── Security/CurrentUser.cs     # ICurrentUser over HttpContext.User
    ├── Security/TokenService.cs    # ITokenService — issues HS256 JWTs
    └── Program.cs                  # Identity user store (AddIdentityCore) + JWT bearer scheme

docker-compose.yml                  # local Postgres
.config/dotnet-tools.json           # dotnet-ef tool
.github/workflows/deploy.yaml       # build → migrate (dotnet ef) → roll out
```

**Connection string** — key `ConnectionStrings:AppDb` (`appsettings.json` / env
`ConnectionStrings__AppDb`). The design-time factory reads `AppDb__ConnectionString` for tooling.

---

## Authentication & authorization

**JWT** authentication, **transported as `httpOnly` cookies**, with **role-based** (RBAC)
authorization. Full design spec: **[authentication.md](authentication.md)**. ASP.NET Core Identity
(`AddIdentityCore<AppUser>()` — `UserManager` / `RoleManager` / PBKDF2 hashing over `AppDbContext`)
is the **user store**; the JWT scheme owns authentication. A short-lived access token (15 min,
`Path=/`) and an opaque, rotated, fixed-7-day refresh token (`Path=/api/auth/refresh`) are both
delivered as `HttpOnly; Secure; SameSite=Strict` cookies — never in the body, never readable by JS.

**Endpoints** (`AuthEndpoints` / `ProfileEndpoints`, anonymous unless noted):

| Endpoint | Purpose |
|----------|---------|
| `POST /api/auth/register` | Create a user (assigned the `user` role); returns 200 or validation errors. |
| `POST /api/auth/login` | Verify credentials → sets `access_token` + `refresh_token` cookies. 401 on bad credentials. |
| `POST /api/auth/refresh` | Rotate the refresh cookie for a fresh pair; reuse of a rotated token revokes the whole chain → 401. |
| `POST /api/auth/logout` | **Authorized.** Revoke all the caller's refresh tokens + clear both cookies. |
| `GET /api/profile/me` | **Authorized.** Caller's name + roles from the validated JWT. |

**Using a token:** nothing to do — the browser attaches the `httpOnly` cookies automatically. The
access cookie is read server-side by `JwtBearerEvents.OnMessageReceived`; cookie flags/scoping live
in `AuthCookies` (`AppointmentScheduler.Api/Security`).

- **Issuing** — `ITokenService` / `TokenService` (`AppointmentScheduler.Api/Security`) signs an HS256 JWT with
  claims `sub` + `NameIdentifier` (user id), `name`/`email`, and one `role` claim per role. Expiry
  is `Jwt:AccessTokenMinutes` from now (via `TimeProvider`).
- **Validating** — `Program.cs` registers `AddJwtBearer` with issuer/audience/lifetime/signing-key
  validation, `ValidAlgorithms = [HS256]` (algorithm-confusion defense — the token's own `alg` is
  never trusted), `ClockSkew = 2 min`, and `MapInboundClaims = false` (keeps `sub` verbatim). Claims
  use standard `ClaimTypes` URIs so `RequireRole`, `ICurrentUser`, and `/api/profile/me` work with no
  extra mapping.
- **Brute-force lockout** — `/api/auth/login` enforces account lockout via `UserManager`: 5 failed
  attempts → locked 5 min (policy in `Program.cs` `AddIdentityCore` lockout options).
- **Config** — the `Jwt` section (`Issuer`, `Audience`, `SigningKey`, `AccessTokenMinutes`). The
  dev `SigningKey` lives in `appsettings.json`; **override it per environment** via the
  `Jwt__SigningKey` secret/env var (HS256 needs ≥ 32 bytes).
- **Roles (RBAC):** `DbInitializer` seeds `admin` + `user`. Endpoints opt in:
  ```csharp
  group.RequireAuthorization();                                       // any authenticated caller
  group.MapPost("", ...).RequireAuthorization(p => p.RequireRole("admin"));
  ```
- **`ICurrentUser`** (Application port) exposes `UserId` / `IsInRole` to handlers without a
  dependency on `HttpContext`.

**Dev admin** — set in `appsettings.Development.json` (`Seed:Admin:Email` / `Password`);
`DbInitializer` creates the user and assigns the `admin` role on startup.

> Why JWT in cookies? A stateless access token keeps the API horizontally scalable with no
> server-side session, while `httpOnly` cookies keep the token out of JavaScript's reach (XSS-safe)
> and let the browser attach it automatically. Revocation — JWT's classic weak spot — is handled by
> the short access TTL plus a server-side refresh-token chain (rotation + reuse detection). See
> [authentication.md](authentication.md) for the full rationale and the path to OIDC/SSO.

---

## Local development

```bash
docker compose up -d                       # Postgres on :5432
dotnet run --project source/AppointmentScheduler.Api     # Development → auto-migrates + seeds roles/admin
```

Default local connection (`appsettings.json`):
```
Host=localhost;Port=5432;Database=appointmentscheduler;Username=appointmentscheduler;Password=appointmentscheduler
```

Reset local DB:
```bash
docker compose down -v && docker compose up -d   # -v drops the volume; next run re-migrates
```

Inspect:
```bash
docker exec appointmentscheduler-postgres psql -U appointmentscheduler -d appointmentscheduler -c "\dt"   # AspNet* + refresh_tokens
```

---

## Adding a migration

After changing the model (a new entity, a new `IEntityTypeConfiguration<T>`, an `AppUser` field):

```bash
dotnet ef migrations add <Name> --project source/AppointmentScheduler.Infrastructure --startup-project source/AppointmentScheduler.Api
dotnet ef database update       --project source/AppointmentScheduler.Infrastructure --startup-project source/AppointmentScheduler.Api
```
`dotnet-ef` is in the tool manifest (`dotnet tool restore`). The design-time factory supplies the
connection; override with `AppDb__ConnectionString` if needed.

---

## CI / CD

- **`deploy.yaml`** (`workflow_dispatch`) — build + push image to GHCR → **migrate**
  (`dotnet ef database update`, connection from the env-scoped secret `DB_CONNECTION_STRING`) →
  roll out (placeholder; wire in ECS / k8s / App Runner / Cloud Run). Migrations run **before**
  rollout.

Set `DB_CONNECTION_STRING` per environment (GitHub → Settings → Environments → secrets).

---

## Switching provider

1. `AppointmentScheduler.Infrastructure` — swap `Npgsql.EntityFrameworkCore.PostgreSQL` for the matching EF
   provider; change `UseNpgsql` → `UseSqlServer` / etc. in `DependencyInjection.cs` and
   `AppDbContextFactory.cs`.
2. Delete and regenerate the migrations against the new provider.
