# Auth Implementation — Gap Register

Tracks divergence between the **code** and the design spec in [authentication.md](authentication.md).
Section refs (§) point at that spec. Status as of the cookie-transport rework + login hardening.

> **Legend** — ✅ aligned · 🔴 open security gap · 🟠 open behavioral/operational gap · 🟡 open
> schema gap · ⚪ by-design / declined / infra.

---

## ✅ Aligned (spec implemented)

| Spec | Where |
|------|-------|
| §1 Stateless JWT, HMAC verify in-process | `Program.cs` `AddJwtBearer` |
| §2 Header `HS256`; claims `sub`/`iss`/`aud`/`iat`/`exp`/`jti`/`role` | `TokenService` |
| §2 Validate sig/exp/iss/aud | `Program.cs` `TokenValidationParameters` |
| §2 **Algorithm pinned** (`ValidAlgorithms=[HS256]`, `alg` not trusted) | `Program.cs` |
| §2 **Clock skew ±2 min** | `Program.cs` |
| §3 Access 15 min, refresh 7-day **fixed** TTL (not reset on rotation) | `JwtOptions`, `RefreshTokenService.RotateAsync` |
| §4 `httpOnly` + `Secure` + `SameSite=Strict` cookies; access `Path=/`, refresh `Path=/api/auth/refresh` | `AuthCookies` |
| §4 Access token read from cookie server-side | `Program.cs` `JwtBearerEvents.OnMessageReceived` |
| §5 Refresh token opaque, only `SHA-256` hash stored, raw never persisted | `RefreshTokenService` |
| §6 Rotation: old token revoked, new issued | `RefreshTokenService.RotateAsync` |
| §6 Reuse detection → revoke chain → 401 | `RefreshTokenService.RotateAsync` |
| §7 Identity split into dedicated endpoint `/api/profile/me` | `ProfileEndpoints` |
| §9 Logout revokes refresh token + clears cookies; access valid until expiry | `AuthEndpoints` |
| §11 Brute-force **account lockout** (5 fails → 5-min lock) | `Program.cs`, `AuthEndpoints` login |
| §11 DB breach → only hashes exposed | `RefreshTokenService` |

---

## 🔴 Open — security (none)

All three flagged code-level security gaps (algorithm pin, clock skew, brute-force lockout) are
**closed**. Remaining items below are behavioral, schema, or infra.

---

## 🟠 Open — behavioral / operational

| # | Spec | Gap | Impact | Suggested fix |
|---|------|-----|--------|---------------|
| 4 | §6 refresh race / grace window (~5s or mutex) | No server-side grace window. Concurrent refresh from two tabs/devices → 2nd looks like reuse → **whole chain revoked → forced re-login**. React has a single-tab in-flight mutex only. | UX failure under concurrent tabs; spurious logouts | Per-user refresh mutex, or accept a just-rotated token within a short grace window |
| 5 | §8 cleanup job (nightly DELETE expired/rotated rows) | None. `refresh_tokens` grows unbounded (~96 rows/active user/day). | Table bloat, slow lookups over time | ECS Scheduled Task / EventBridge+Lambda / Hangfire running the §8 `DELETE` (keep rotated rows 7 days for reuse detection) |
| 6 | §11 TLS downgrade — `Secure` + **HSTS** | No `UseHsts()`. `Secure` flag is conditional on `request.IsHttps` (off over plain HTTP in dev/test), not unconditional. | No HSTS header; downgrade window | `app.UseHsts()` in non-dev, or enforce HSTS at the edge/ingress |
| 7 | §6/§9 "token **family**" revocation | Reuse / logout revokes **all** the user's active tokens (per-user), not a per-device lineage. | Reuse or logout on one device kills sessions on all devices | Track a `familyId` per login; revoke by family instead of by user |

---

## 🟡 Open — schema (declined: "anomaly columns")

Spec §8 lists these `refresh_tokens` fields for anomaly detection; not added (out of agreed scope).

| # | §8 field | Current state |
|---|----------|---------------|
| 8 | `ip` (issuing client IP) | missing |
| 9 | `userAgent` (device fingerprint) | missing |
| 10 | `jti` on the refresh row | missing — GUID `Id` PK + unique `TokenHash` cover uniqueness / double-use |

Adding any of these requires a new EF migration (`dotnet ef migrations add Add_RefreshToken_Anomaly_Cols`).

---

## ⚪ By-design / declined / infra

| Spec | Decision |
|------|----------|
| §11 Rate limiting on login + refresh | Not done (declined). Lockout (#2) covers per-account brute force; rate limiting would add per-IP/per-endpoint throttling — `AddRateLimiter`. |
| §4 `__Host-` cookie prefix | Not done. Incompatible with the refresh cookie's `Path=/api/auth/refresh` (the prefix requires `Path=/`). Only relevant if subdomains come into scope. |
| §2 Secret storage (AWS Secrets Manager recommended) | Uses `Jwt__SigningKey` env var / secret — the spec's "acceptable but not ideal" tier. Infra concern, not code. |
| §11 Password hashing (spec says bcrypt/Argon2) | ASP.NET Core Identity default = **PBKDF2**. Strong; acceptable substitute. |
| §2/§11 access-token `jti` uniqueness / blacklist | Not enforced — would require server-side state and contradicts the stateless access-token decision (§1, §9 explicitly accept that a logged-out access token stays valid until its short expiry). |
| §2 `tenantId` claim | Absent — multi-tenancy not in scope. |
| §12 OIDC / RS256 / JWKS / SSO | Explicitly future work; not a gap against the Stage-1 spec. |

---

## Suggested next step

**#5 (cleanup job)** is the highest-value remaining item for production — unbounded table growth is
the only open gap that degrades a running system over time. **#4 (refresh grace window)** is next for
multi-tab UX correctness.
