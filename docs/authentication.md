# RFC: JWT-Based Authentication & Token Management

**Status:** Accepted  
**Audience:** Backend Engineers  
**Context:** Greenfield project, single app, self-managed users

---

## Overview

This document specifies the authentication design for stateless JWT-based auth with refresh token rotation. Both the Access Token and Refresh Token are transported exclusively via `httpOnly` cookies — no tokens are exposed to JavaScript. Each section is structured as: **Problem → Decision → Consequences** so the reasoning behind every design choice is explicit and auditable.

---

## 1. Stateless Authentication via JWT

### Problem

Session-based auth requires the server to maintain state — a session store that maps a session ID to a user. This doesn't scale horizontally without a shared session store (Redis, etc.), adding infrastructure complexity and a single point of failure.

### Decision

Issue a **JWT (JSON Web Token)** upon successful login. The server embeds trust directly into the token via a cryptographic signature, enabling stateless verification on every request.

```
POST /api/auth/login
  → bcrypt.compare(submittedPassword, storedHash)
  → if match: sign JWT with SECRET_KEY
  → return JWT via httpOnly cookie
```

On every subsequent request, the server recomputes the signature from the incoming token's header + payload and compares it against the embedded signature — entirely in memory:

```
Incoming token: header.payload.signature
                        ↓
Server recomputes: HMACSHA256(header + payload, SECRET_KEY)
                        ↓
Match ✅ → token is authentic and untampered → proceed to claim checks
No match ❌ → reject 401
```

**No database round-trip required** — the SECRET_KEY lives in server memory. Any tampering with the payload (e.g. escalating `role` from `student` to `admin`) immediately invalidates the signature.

### Consequences

✅ Horizontally scalable — any server instance can verify any token independently, as long as they share the same SECRET_KEY.  
✅ No session store infrastructure needed.  
⚠️ Stateless JWTs cannot be revoked without introducing server-side state. This design intentionally avoids maintaining Access Token state and instead relies on short-lived Access Tokens together with Refresh Token rotation.  
⚠️ Token payload is base64-encoded, not encrypted — **never embed sensitive data** (passwords, PII beyond identifiers) in the payload.

---

## 2. JWT Structure & Validation Surface

### Decision

Standard three-part structure: `Header.Payload.Signature`, all Base64URL-encoded.

**Header:**
```json
{ "typ": "JWT", "alg": "HS256" }
```

**Payload (registered + custom claims):**
```json
{
  "sub": "user-b07f85be-45da",
  "iss": "https://provider.domain.com/",
  "aud": "https://api.yourdomain.com",
  "iat": 153449083,
  "exp": 153452683,
  "jti": "a1b2c3d4-...",
  "role": "teacher",
  "tenantId": "org-xyz"
}
```

> Use `sub` as the primary user identifier — avoid duplicating it with a custom `userId` claim. `iat` (issued at) records when the token was created — useful for audit trails and anomaly detection. Add custom claims (e.g. `role`, `tenantId`) only when they are needed for request-level authorization and are not expected to change frequently.

**Signature:**
```
HMACSHA256(
  base64UrlEncode(header) + "." + base64UrlEncode(payload),
  SECRET_KEY
)
```

### Validation Rules (enforced on every request)

| Claim | Check | Failure response |
|---|---|---|
| `sig` | Recompute HMAC using SECRET_KEY → compare — single bit change = fail | `401` |
| `exp` | Must be in the future (allow ±2 min clock skew) | `401` |
| `iss` | Must match known issuer | `401` |
| `aud` | Must match this service's identifier | `401` |
| `jti` | Unique token ID — optional blacklist check on logout | `401` |

> **Clock skew:** In distributed systems, server clocks may differ slightly. Allow a small tolerance (1–2 minutes) when validating `exp` to prevent spurious authentication failures across instances.

`401 Unauthorized` = token problem (expired, tampered, wrong audience).  
`403 Forbidden` = token is valid, but user lacks the required permission.

### Secret Key Storage

The `SECRET_KEY` must never appear in source code or be committed to version control. It is injected into the server at runtime and lives only in memory. Storage options by environment:

| Option | When to use |
|---|---|
| **AWS Secrets Manager** | AWS stack (ECS/Terraform) — recommended for this project |
| **Azure Key Vault** | Azure stack |
| **GCP Secret Manager** | GCP stack |
| **HashiCorp Vault** | Self-hosted, cloud-agnostic — suitable for Stage 3 multi-service |
| **CI/CD secrets + env var** | Very small app, low budget — acceptable but not ideal |

**For this project (AWS ECS + Terraform):**

```hcl
# Terraform — create the secret
resource "aws_secretsmanager_secret" "jwt_secret" {
  name = "myapp/jwt-secret"
}

# ECS Task Definition — inject at runtime
secrets = [
  {
    name      = "JWT_SECRET"
    valueFrom = aws_secretsmanager_secret.jwt_secret.arn
  }
]
```

The ECS task IAM role is granted read access to the secret. The app reads it as a standard environment variable — no code change needed if the secret is rotated.

### Consequences

✅ Tamper-evident — any mutation to header or payload invalidates the signature.  
✅ Self-contained — all validation is in-process, no external call needed.  
✅ Secret never touches the codebase — injected at runtime via Secrets Manager.  
⚠️ **Algorithm confusion attacks:** Always enforce the expected algorithm server-side. Never trust the `alg` claim from the incoming token.  
⚠️ All instances must share the same `SECRET_KEY`. Switching to RS256 (asymmetric) at Stage 3 removes this constraint — only the Auth Server holds the private key; all services verify with the public key.

---

## 3. Two-Token Pattern (Access + Refresh)

### Problem

Short-lived tokens (needed for security) conflict with good UX (users shouldn't re-authenticate every 15 minutes). A single long-lived token is a stolen-token nightmare.

### Decision

Issue two tokens on login with asymmetric lifetimes:

| Token | Lifetime | Transport | Scope |
|---|---|---|---|
| **Access Token** | 15 minutes | `httpOnly` cookie | `Path=/` |
| **Refresh Token** | 7 days (fixed) | `httpOnly` cookie | `Path=/api/auth/refresh` |

**Refresh Token expiration model — Sliding vs Fixed:**

There are two approaches for Refresh Token expiration:

| | Sliding | Fixed |
|---|---|---|
| TTL behavior | Resets on every successful refresh | Set once at login, never resets |
| Active user | Session extends indefinitely while active | Forced re-login every 7 days |
| Stolen RT risk | Attacker can keep session alive indefinitely by refreshing | Hard cap — expires 7 days from original login |
| UX impact | Seamless for active users | Predictable session boundary |
| Best for | UX-first apps | Security-conscious apps |

**Chosen: Fixed expiration.** The Refresh Token TTL is set once at login and never resets on rotation. A user who actively refreshes every 15 minutes will still be forced to re-authenticate after 7 days from their original login. This bounds the maximum exploitation window of a stolen Refresh Token to a hard 7-day ceiling regardless of usage — a deliberate security trade-off.

The Access Token is sent automatically on every API request. When it expires, the Refresh Token cookie is sent automatically to the refresh endpoint — the browser handles all of this transparently.

```
Access Token  ──→ expires in 15 min ──→ silently replaced via Refresh Token
Refresh Token ──→ fixed 7-day TTL   ──→ expires 7 days from login, does not reset
```

### Consequences

✅ Stolen Access Token window is bounded to 15 minutes.  
✅ Closing/reopening the tab does not log the user out — cookies persist across tab sessions.  
✅ Stolen Refresh Token has a hard maximum exploitation window of 7 days from original login.  
⚠️ Active users will be forced to re-authenticate every 7 days — accepted trade-off for tighter session control.

---

## 4. Why Cookies Instead of Authorization Headers?

This is a deliberate architectural decision. The alternatives and their trade-offs:

| Approach | XSS Risk | CSRF Risk | Survives Tab Close | Recommendation |
|---|---|---|---|---|
| `localStorage` | ❌ High | ✅ None | ✅ Yes | **Avoid** |
| `sessionStorage` | ❌ High | ✅ None | ❌ No | **Avoid** |
| In-memory (JS var) | ✅ None | ✅ None | ❌ No | Acceptable for SPAs, poor UX |
| `httpOnly` Cookie | ✅ None | ⚠️ Needs `SameSite` | ✅ Yes | **Chosen approach** |

`localStorage` and `sessionStorage` are readable by any JavaScript on the page — a single XSS injection exfiltrates all tokens instantly. In-memory storage is the safest from an XSS perspective but is lost on tab close, requiring a silent refresh on every app startup.

`httpOnly` cookies give the best combination: JavaScript cannot read them, they survive tab close, and CSRF is mitigated by `SameSite=Strict`.

**Cookie flags:**

```
Set-Cookie: access_token=<jwt>;
  HttpOnly;       ← JavaScript cannot read this cookie (XSS protection)
  Secure;         ← HTTPS only
  SameSite=Strict; ← Not sent on cross-site requests (CSRF protection)
  Path=/

Set-Cookie: refresh_token=<token>;
  HttpOnly;
  Secure;
  SameSite=Strict;
  Path=/api/auth/refresh  ← Only sent to the refresh endpoint, reducing unnecessary exposure
```

> **`SameSite=Strict` caveat:** This is appropriate for a first-party application where the frontend and backend share the same site origin. Applications that rely on cross-site redirects (e.g. third-party identity providers, OAuth flows) may require `SameSite=Lax` or `SameSite=None` together with explicit CSRF token protection.

### Consequences

✅ XSS cannot exfiltrate tokens — JavaScript cannot read `httpOnly` cookies.  
✅ CSRF mitigated by `SameSite=Strict` without needing a separate CSRF token.  
✅ Tokens survive tab close/reopen — no silent refresh needed on startup.  
✅ Client authentication logic is simplified because token attachment is handled by the browser.  
⚠️ Subdomain isolation: use `__Host-` cookie prefix to prevent subdomain cookie injection if subdomains are in scope.

---

## 5. Refresh Token Design: Opaque, Not JWT

### Decision

Refresh Tokens are **opaque cryptographically secure random values**, not JWTs.

Since every refresh requires a database lookup anyway (to check `revokedAt` and `expiresAt`), a self-contained JWT provides no benefit — the database is always consulted regardless. Opaque tokens are simpler, smaller, and make revocation straightforward: just set `revokedAt`.

**Storage:** The raw Refresh Token string is **never stored**. Only its `SHA-256 hash` is persisted. A DB breach yields nothing usable.

---

## 6. Refresh Token Rotation & Reuse Detection

### Problem

A long-lived Refresh Token that is never invalidated is equivalent to a permanent credential. If stolen, the attacker has indefinite access.

### Decision

**Rotation:** Every use of a Refresh Token issues a new Refresh Token and immediately revokes the old one. The old token's `revokedAt` is set; a new row is inserted.

**Reuse detection:** If a token arrives with `revokedAt IS NOT NULL`, the entire token family is revoked. This signals that a previously-rotated token was replayed — indicative of theft.

```
Refresh flow:
  1. Browser auto-sends refresh_token cookie to POST /api/auth/refresh
  2. Server: hash(token) → lookup tokenHash in DB
  3. Check: expiresAt > NOW AND revokedAt IS NULL
  4. If reused token detected (revokedAt IS NOT NULL):
       → revoke entire token family
       → return 401, force re-login
  5. If valid:
       → SET revokedAt = NOW on old row
       → INSERT new row with same expiresAt (TTL unchanged — fixed expiration)
       → issue new cookies
```

**Refresh race condition:** Browsers can issue concurrent requests. If two tabs both trigger a refresh simultaneously using the same token, one will succeed and the other will appear as a reuse (the token was already rotated). Mitigate with a per-user refresh mutex or a short-lived idempotency window:

```
Tab A: refresh() ──→ succeeds, old token revoked
Tab B: refresh() ──→ same token → revokedAt IS NOT NULL → looks like reuse → 401
                                                                  ↑
                                          Mitigate: short grace window (~5s) before treating as attack
```

### Consequences

✅ Stolen Refresh Token becomes invalid after the next legitimate refresh cycle.  
✅ Reuse detection catches token replay attacks and forces re-authentication.  
✅ DB breach does not expose usable tokens.  
⚠️ Race condition on concurrent refreshes — mitigate with mutex or grace window.

---

## 7. User Info Endpoint

The Access Token cookie is sent automatically by the browser on every request to `Path=/`, granting access to protected API endpoints.

Identity information is intentionally separated from the Access Token — this keeps tokens small and avoids serving stale profile data if the user updates their name or avatar between token issuances. After login, the client fetches user info via a dedicated endpoint:

```
GET /api/profile/me
(browser auto-attaches access_token cookie)
→ 200 { userId, name, email, avatar, ... }
```

---

## 8. Database Schema

### User Table

| Field | Type | Notes |
|---|---|---|
| `id` | UUID | Primary key |
| `email` | VARCHAR | Unique, indexed |
| `password` | VARCHAR | bcrypt/Argon2 hash — raw password never stored |

### Refresh Token Table

| Field | Type | Notes |
|---|---|---|
| `tokenHash` | VARCHAR | SHA-256 of raw token — indexed |
| `userId` | UUID | FK → User |
| `jti` | UUID | Unique token ID — prevents double-use |
| `expiresAt` | TIMESTAMP | 7 days from original login — fixed, not reset on rotation |
| `revokedAt` | TIMESTAMP | NULL = active; non-NULL = revoked |
| `ip` | VARCHAR | Issuing client IP — anomaly detection |
| `userAgent` | VARCHAR | Device fingerprint — anomaly detection |

**Design note:** Rows are never hard-deleted on rotation — `revokedAt` is set. This preserves the audit trail and enables reuse detection.

### Implementation note — ASP.NET Core Identity store

The user and role stores are backed by **ASP.NET Core Identity** (`AddIdentityCore<AppUser>`,
`AppDbContext : IdentityDbContext<AppUser>`). This is the **authoritative physical schema** the
migrations create.

**Tables**

| Table | Role |
|---|---|
| `AspNetUsers` | user store — email, normalized email/username, `PasswordHash`, `SecurityStamp`, `ConcurrencyStamp` |
| `AspNetRoles` | role catalogue — seeded with `admin` and `user` |
| `AspNetUserRoles` | user ↔ role join — the RBAC link read by `RequireRole` and `/api/profile/me` |
| `refresh_tokens` | refresh-token store (this document) |

The template is **role-based (RBAC)**, so `OnModelCreating` keeps only the three Identity tables
above; authorization is driven entirely by role membership.

**Password hashing.** Identity hashes with **PBKDF2** (HMAC-SHA256, key-stretched), stored in
`AspNetUsers.PasswordHash`; the raw password is never persisted. Swap in a custom
`IPasswordHasher<AppUser>` if Argon2/bcrypt is required.

**Case-insensitive lookup.** `AspNetUsers` stores `NormalizedUserName` / `NormalizedEmail` —
uppercased copies that back the unique index and login lookup (`UserManager.FindByEmailAsync` matches
on the normalized column), so casing never splits or duplicates an account.

**`refresh_tokens` columns**

| Column | Type | Notes |
|---|---|---|
| `id` | UUID | Primary key |
| `user_id` | TEXT | FK → `AspNetUsers.Id` |
| `token_hash` | TEXT | SHA-256 of the raw token — indexed; raw value never stored |
| `created_at` | TIMESTAMPTZ | Issued-at |
| `expires_at` | TIMESTAMPTZ | Fixed 7-day TTL from original login — inherited on rotation, not reset |
| `revoked_at` | TIMESTAMPTZ | NULL = active; set on rotation, logout, or reuse-detection revocation |
| `replaced_by_token_hash` | TEXT | Hash of the successor token — rotation chain / reuse detection |

> **`SecurityStamp`.** Identity maintains this per-user stamp; bumping it is the cookie-based way to
> invalidate every session ("log out everywhere"). This template is **stateless JWT** — access tokens
> are validated by signature + `exp` with no per-request DB read — so session invalidation is enforced
> by **revoking refresh tokens** (`RefreshTokenService.RevokeAllAsync`), called on **logout**, on
> **reuse-detection** family revocation, and on **password change** (`POST /api/auth/change-password`,
> §9). Other devices can no longer rotate; their access tokens age out within the 15-minute TTL.
> `ConcurrencyStamp` is unrelated — it is EF Core optimistic-concurrency metadata, not a security
> control.

### Table Growth & Cleanup Strategy

Every refresh inserts a new row. With active users, this accumulates quickly:

```
1 active user    → refresh every 15 min → ~96 rows/day
1,000 users      → ~96,000 rows/day
1 month          → ~2.9 million rows
```

Most of these rows are either expired (`expiresAt < NOW`) or rotated (`revokedAt IS NOT NULL`) and no longer usable. However, **do not delete rotated rows immediately** — they are still needed for reuse detection. If a stolen token is replayed after rotation, the server must find the `revokedAt` row to detect the attack. Deleting it too early blinds this mechanism.

**Safe deletion criteria:**

```sql
DELETE FROM refresh_tokens
WHERE expiresAt < NOW
   OR (revokedAt IS NOT NULL AND revokedAt < NOW - INTERVAL '7 days');
```

- `expiresAt < NOW` — token expired naturally, safe to remove immediately.
- `revokedAt IS NOT NULL AND revokedAt < NOW - 7 days` — rotated token, kept for 7 days as reuse detection window, then safe to remove.

**Implementation options for the cleanup job:**

| Option | Notes |
|---|---|
| **ECS Scheduled Task** | Run a dedicated cleanup container on a cron schedule — fits existing AWS/ECS stack |
| **AWS EventBridge + Lambda** | Trigger a Lambda function nightly — lightweight, no container overhead |
| **Hangfire / Quartz.NET** | Background job inside the ASP.NET app itself — simplest if already using a job scheduler |

Recommended: run nightly during off-peak hours. The job is a simple DELETE query — fast even on large tables if `expiresAt` and `revokedAt` are indexed.

---

## 9. End-to-End Flow

### Login

```
POST /api/auth/login  { email, password }

Server:
  1. SELECT user WHERE email = ?
  2. bcrypt.compare(password, user.passwordHash)
  3. Generate accessToken (JWT, 15min, signed with SECRET_KEY)
  4. Generate refreshToken (cryptographically secure random bytes)
  5. INSERT refresh_tokens (hash(refreshToken), userId, jti, expiresAt, ip, userAgent)

Response:
  Set-Cookie: access_token=<jwt>; HttpOnly; Secure; SameSite=Strict; Path=/
  Set-Cookie: refresh_token=<tok>; HttpOnly; Secure; SameSite=Strict; Path=/api/auth/refresh
```

### Authenticated Request

```
GET /api/profile/me
(browser automatically attaches the appropriate cookies based on their scope and attributes)

Server middleware:
  1. Extract token from cookie header
  2. Verify signature, exp (+skew), iss, aud — all in-process
  4. Attach decoded claims to request context
  5. Handler returns data

Access Token validation is performed locally without database access.
```

### Token Refresh

```
POST /api/auth/refresh
(browser automatically attaches refresh_token cookie — scoped to this path)

Server:
  1. Read cookie → hash → lookup tokenHash in DB
  2. Assert: expiresAt > NOW
  3. Assert: revokedAt IS NULL  ← if not, revoke family, return 401
  4. SET revokedAt = NOW on old row
  5. INSERT new row with same expiresAt as old row  ← fixed TTL, not reset
  6. Generate new accessToken (signed with SECRET_KEY)

Response:
  Set-Cookie: access_token=<new_jwt>; HttpOnly; Secure; SameSite=Strict; Path=/
  Set-Cookie: refresh_token=<new_tok>; HttpOnly; Secure; SameSite=Strict; Path=/api/auth/refresh

Client:
  - Cookies replaced automatically by browser
  - Retry original request transparently
```

### Logout

```
POST /api/auth/logout

Server:
  1. SET revokedAt = NOW on active refresh token row
  2. Set-Cookie: access_token=;  Max-Age=0  ← clears cookie
     Set-Cookie: refresh_token=; Max-Age=0  ← clears cookie
```

> **Note:** Logout revokes the active Refresh Token immediately and clears both cookies. Any previously issued Access Token remains technically valid until its 15-minute expiry. This is an accepted trade-off of stateless Access Tokens — the short TTL bounds the risk window.

### Change Password

```
POST /api/auth/change-password  { currentPassword, newPassword }   (authorized)

Server:
  1. Resolve caller from the access cookie; verify currentPassword (Identity, PBKDF2)
  2. On mismatch / weak newPassword → 400 (ValidationProblem), no change
  3. UserManager.ChangePasswordAsync → updates PasswordHash (and Identity's SecurityStamp)
  4. RefreshTokenService.RevokeAllAsync(userId)  ← revokes EVERY active refresh token (all devices)
  5. Issue a NEW access + refresh pair for THIS device, set both cookies

Response:
  Set-Cookie: access_token=<new_jwt>;  …
  Set-Cookie: refresh_token=<new_tok>; …   ← fresh 7-day TTL (treated as a new login)
```

> **"Log out everywhere" without `SecurityStamp`.** Because Access Tokens are stateless (no
> per-request DB read), changing the password cannot invalidate other devices by itself. Session
> invalidation is enforced by **revoking all refresh tokens**: other devices can no longer rotate, and
> their Access Tokens age out within the 15-minute TTL. The current device is kept signed in by
> re-issuing its cookie pair. The new Refresh Token starts a **fresh 7-day window** — a password change
> is treated as a new credential event, like logging in again (so the §-Absolute-Session-Expiry clock
> resets for this device only).

### Absolute Session Expiry

Refresh Token has a **fixed 7-day TTL from login**. The clock does not reset on refresh. After 7 days, regardless of activity, the user must re-authenticate. This bounds the maximum exploitation window of a stolen Refresh Token to 7 days from the original login.

---

## 10. Full Sequence Diagram

```
CLIENT (Browser)                                            SERVER (ASP.NET API)
      |                                                              |
      | 1. POST /api/auth/login (Email & Password)                   |
      | -----------------------------------------------------------> |
      |                                                              | 2. Verify credentials in DB
      |                                                              | 3. Generate 15-min Access Token &
      |                                                              |    Refresh Token (opaque, hashed)
      | 4. Set-Cookie: access_token  (HttpOnly, Path=/)              |
      |    Set-Cookie: refresh_token (HttpOnly, Path=/api/auth/refresh)|
      | <----------------------------------------------------------- |
      |                                                              |
      |=================== DAY-TO-DAY API REQUEST ===================|
      |                                                              |
      | 5. GET /api/profile/me                                       |
      |    (browser auto-attaches access_token cookie)               |
      | -----------------------------------------------------------> |
      |                                                              | 6. Validate sig, exp, iss, aud
      | 7. Returns Protected User Data                               |
      | <----------------------------------------------------------- |
      |                                                              |
      |================= AFTER 15 MINUTES (EXPIRY) ==================|
      |                                                              |
      | 8. GET /api/profile/me                                       |
      |    (browser auto-attaches expired access_token cookie)       |
      | -----------------------------------------------------------> |
      |                                                              | 9. Token is expired
      | 10. Returns 401 Unauthorized                                 |
      | <----------------------------------------------------------- |
      |                                                              |
      |==================== THE REFRESH FLOW ========================|
      |                                                              |
      | 11. POST /api/auth/refresh                                   |
      |     (browser auto-attaches refresh_token cookie)             |
      | -----------------------------------------------------------> |
      |                                                              | 12. hash(token) → DB lookup
      |                                                              | 13. Rotation: revoke old row,
      |                                                              |     insert new row (expiresAt unchanged)
      | 14. Set-Cookie: new access_token                             |
      |     Set-Cookie: new refresh_token                            |
      | <----------------------------------------------------------- |
      |                                                              |
      | 15. Retry original GET /api/profile/me                       |
      |     (browser auto-attaches new access_token cookie)          |
      | -----------------------------------------------------------> |
      |                                                              | 16. Validate new Access Token
      | 17. Returns Protected User Data                              |
      | <----------------------------------------------------------- |
      |                                                              |
      |======================= LOGGING OUT ==========================|
      |                                                              |
      | 18. POST /api/auth/logout                                    |
      | -----------------------------------------------------------> |
      |                                                              | 19. SET revokedAt on refresh token
      |                                                              |     Clear both cookies (Max-Age=0)
      | 20. Returns 200 OK                                           |
      | <----------------------------------------------------------- |
      |     Access Token remains valid until expiry (~15 min)        |
```

---

## 11. Security Checklist

| Concern | Mitigation |
|---|---|
| XSS stealing tokens | `httpOnly` cookies — JavaScript cannot read them |
| CSRF on API requests | `SameSite=Strict` — cookie not sent on cross-site requests |
| Stolen Access Token | 15-min TTL limits blast radius; valid until expiry even after logout |
| Stolen Refresh Token | Rotation invalidates after next use; reuse detection triggers family revocation |
| DB breach exposing tokens | Only SHA-256 hashes stored — raw tokens never persisted |
| Algorithm confusion attack | Server enforces signing algorithm — never trusts `alg` claim from token |
| Replay attacks | `exp` claim + `jti` uniqueness check |
| Subdomain cookie injection | Use `__Host-` cookie prefix to scope cookies strictly to the origin |
| Signing key compromise | Rotate SECRET_KEY in Secrets Manager; redeploy to pick up new value |
| Clock skew across instances | Allow ±2 min tolerance on `exp` validation |
| Refresh endpoint abuse | Rate limiting per IP and per user |
| Brute-force login | Rate limiting + account lockout / exponential backoff |
| Password database breach | bcrypt/Argon2 hashing — raw passwords never stored |
| TLS downgrade | `Secure` cookie flag + HSTS enforced at infrastructure level |

---

## 12. Future Work: Scaling the Auth Architecture

The custom JWT pattern in this document is designed for a **single app managing its own users**. As the product grows, the auth layer will need to evolve.

```
Stage 1               Stage 2                    Stage 3
─────────────         ───────────────────        ──────────────────────
Small app             Multiple apps              Full SSO
Self-issued JWT   →   Each app owns its own  →   Extract a dedicated
                      auth logic — painful,       Auth Server,
                      duplicated, hard to         adopt OIDC
                      keep in sync
```

### Stage 1 — Current (this document)
- Backend self-issues Access Token + Refresh Token, both in `httpOnly` cookies.
- Client gets user info via `GET /api/profile/me`.
- Simple, no external dependencies, full control.

### Stage 2 — Pain points when scaling to multiple apps
- Every new app duplicates auth logic (login, refresh, revocation).
- Password policy changes must be applied in multiple places.
- Users must log in separately for each app — no shared session.

### Stage 3 — Dedicated Auth Server + OIDC
- Extract auth into a standalone service (e.g. **Keycloak**, **Auth0**, **AWS Cognito**, **FusionAuth**).
- All apps delegate authentication to this single source of truth.
- **OpenID Connect (OIDC)** is adopted — a standard built on top of OAuth 2.0 that adds an identity layer via the **ID Token**.
- Users log in once and access all apps — true **SSO**.
- The Access Token + Refresh Token mechanics stay exactly the same; only the issuer changes.
- **HS256 → RS256** — Auth Server holds `private_key` (signs tokens); all API services hold only `public_key` (verify tokens). No shared secret across services.
- **`kid` + JWKS** — each key pair is identified by a `kid`. The Auth Server exposes a standard `/.well-known/jwks.json` endpoint. Services fetch and cache the correct public key by `kid`, enabling zero-downtime key rotation without redeployment:
  ```
  GET /.well-known/jwks.json
  → { "keys": [{ "kid": "abc", "alg": "RS256", "n": "...", "e": "AQAB" }] }
  ```
- ⚠️ **`SameSite=Strict` → `SameSite=Lax`** — OIDC Authorization Code flow requires a cross-site redirect from the Identity Provider back to the app. `SameSite=Strict` blocks cookies on that inbound redirect, breaking the login callback. `SameSite=Lax` permits cookies on top-level GET navigation (redirect) while still blocking CSRF on state-mutating requests (POST/PUT/DELETE). Additional CSRF token protection should be evaluated at that point.

> The foundation built here maps directly onto OIDC. Migrating is an architectural extraction, not a rewrite.
