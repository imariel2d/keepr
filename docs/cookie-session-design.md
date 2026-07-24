# Cookie Sessions — Design

> Status: **designed, being built**.
>
> Decided by Ariel, 2026-07-23: keep the user's session in an HTTP cookie instead of a JWT in
> `localStorage`.
>
> This replaces the token half of Q1 in [my-decisions.md](my-decisions.md). Q1 chose *own auth, no
> external IdP*, and that still holds — what changes is how the authenticated session is carried and
> where it lives.

---

## 1. What changes, and what it actually buys

Today: `POST /api/auth/login` returns a 60-minute JWT, the client stores it in `localStorage`, and
an interceptor attaches it as `Authorization: Bearer`.

| | Today (JWT in localStorage) | After (opaque session cookie) |
|---|---|---|
| Readable by JavaScript | **Yes** — any XSS exfiltrates a valid token | No — `HttpOnly` |
| Revocable | **No** — valid until it expires | Yes, immediately |
| Survives 60 min | **No** — dies, with no refresh | Yes — sliding, 30 days |
| CSRF exposure | None (bearer header) | **Introduced** — mitigated in §5 |
| Server state | None | One row per session |

The honest trade: this fixes XSS token theft and gains revocation, and it takes on CSRF as a new
concern. §5 argues that trade is strongly favourable *in this architecture specifically*.

### 1.1 The session silently dies today

Worth stating separately, because it is a bug and not a design choice: `AccessTokenMinutes` is 60,
nothing refreshes the token, and no interceptor handles `401`. After an hour the app keeps rendering
and every API call fails. Sliding expiry (§4) and 401 handling (§7) fix that as a side effect.

---

## 2. Same-origin is what makes this simple

Cookie auth for an SPA is usually painful because the API and the app are on different origins,
which forces `SameSite=None`, a credentialed CORS policy, and exposure to third-party-cookie
blocking. **None of that applies here:**

| Environment | How the browser sees it |
|---|---|
| Development | Angular dev server proxies `/api` and `/health` → `localhost:5080` (`proxy.conf.json`) |
| Production | The API serves the built SPA from `wwwroot` (`UseDefaultFiles`/`MapFallbackToFile`) |

Same origin in both. No CORS policy exists in `Program.cs` and none is added. That means the cookie
is never sent cross-site by anything, and `SameSite=Lax` is a real boundary rather than a formality.

**This is load-bearing.** If Keepr ever splits the API onto its own hostname, §5 must be revisited
before that ships, not after.

---

## 3. Opaque session IDs, not a JWT in a cookie

Moving the existing JWT into a cookie would have been a smaller diff and was rejected: it wins the
XSS property and nothing else. The reason to hold server state is **revocation** — a JWT is valid
until it expires no matter what the server thinks, so "sign out" cannot actually end a session, and
a leaked token cannot be cut off.

### 3.1 The token, and why the database stores a hash

The cookie carries 32 bytes from `RandomNumberGenerator`, base64url-encoded. The database stores
only `SHA-256(token)`, never the token itself.

This is the same reasoning as password hashing, applied to a credential that *is* a password
equivalent: read access to the `Sessions` table — a backup, a log, a SQL-injection read — would
otherwise hand over live sessions for every logged-in user. Storing the digest makes that dump
inert.

SHA-256 rather than BCrypt is deliberate and is **not** the same trade-off as passwords. A 256-bit
random token has no guessable structure, so there is nothing for an offline attacker to brute-force
and no need for a slow KDF. It also has to be verified on every authenticated request, where BCrypt's
cost would be a per-request tax paid for no benefit.

Lookup is by hash — an indexed equality probe, so no timing-safe comparison is needed on top:
the database finds the row or it does not.

### 3.2 Shape

```csharp
public class Session
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public byte[] TokenHash { get; set; }        // SHA-256 of the cookie value; unique index
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? UserAgent { get; set; }       // for a future "active sessions" screen
    public string? CreatedIp { get; set; }
}
```

`RevokedAt` is a nullable timestamp rather than a bool: *when* a session was killed is the
interesting part of an incident, and it costs nothing over a flag.

---

## 4. Lifetime: sliding, 30 days

`ExpiresAt = LastSeenAt + 30 days`. An active user is never signed out; an idle one expires 30 days
after their last request. For a personal media store this is right — re-authenticating to look at
your own photos is friction with no security payoff at this threat level.

### 4.1 Renewal is throttled, or every request becomes a write

Naively updating `LastSeenAt` on each authenticated request turns every `GET` into a write,
against a row that every request already reads. On a file browser that fires several calls per
navigation, that is a lot of write amplification for a timestamp nobody reads in real time.

**The session is only renewed when more than one day has passed since `LastSeenAt`.** The window is
30 days, so a day of drift is immaterial to when the session actually expires, and it caps writes at
one per user per day. Reads stay on the hot path; writes become rare.

### 4.2 Expired rows

Expired and revoked sessions are deleted for that user on their next successful login, rather than
by a background sweeper. Session rows accrue at roughly one per login, so there is no volume here
worth a hosted service — and unlike `TrashPurgeService`, nothing is charged to a quota while they
sit. See Q-C4 if that assumption stops holding.

---

## 5. CSRF: `SameSite=Lax`, and why that is enough here

Bearer tokens are immune to CSRF because the browser never attaches them automatically. Cookies are
attached automatically, so a cross-site form post or `fetch` would ride the user's session. That is
the new risk, and it is answered by the cookie attributes:

| Attribute | Value | Reason |
|---|---|---|
| `HttpOnly` | `true` | The entire point: XSS cannot read the session. |
| `SameSite` | `Lax` | Blocks the cookie on all cross-site `fetch`/XHR and on cross-site `POST`/`PATCH`/`DELETE` navigations. |
| `Secure` | `true` outside Development | Never send over plaintext. Left off for local `http://` so the dev proxy works everywhere. |
| `Path` | `/` | One session for the whole app. |

**Why `Lax` is sufficient rather than merely convenient.** `Lax` sends the cookie on exactly one
cross-site case: a *top-level GET navigation*. Every state-changing endpoint in Keepr is `POST`,
`PATCH`, or `DELETE`, and every one is called via `fetch`/XHR, which `Lax` excludes unconditionally.
So the one hole `Lax` leaves is a shape no endpoint here has. Combined with the absence of any CORS
policy — a cross-origin script cannot read a response even if it could send the request — the
cross-site attack surface is empty.

`Strict` was rejected for a concrete UX cost: it also withholds the cookie on top-level navigation
from an external link, so opening Keepr from an email or a chat message would load the SPA, get
`401`, and bounce the user to the login screen despite a perfectly valid session, until they
reloaded. That is a real, recurring annoyance in exchange for closing a hole that does not exist.

Anti-forgery tokens were rejected for now on the same basis — plumbing on both sides for a threat
`Lax` already covers here. The condition that would change that is §2's: an API on a separate
origin, or any CORS policy with `AllowCredentials`. Recorded as Q-C2.

### 5.1 Presigned uploads are unaffected, and slightly safer

`UploadService` PUTs file parts to R2 with raw `fetch()`. That is a different origin, and `fetch`
defaults to `credentials: 'same-origin'`, so the session cookie is never attached. This is strictly
better than the bearer setup, where `auth.interceptor.ts` carries a comment explaining that the
`Authorization` header must be kept off those requests or it breaks the S3 signature. The cookie
approach makes that failure mode structurally impossible instead of a rule to remember.

---

## 6. Server surface

`AuthResponse(accessToken)` is gone — nothing is handed to JavaScript any more.

| Endpoint | Change |
|---|---|
| `POST /api/auth/register` | Creates the session, sets the cookie, returns `{ email }` |
| `POST /api/auth/login` | Same |
| `POST /api/auth/logout` | **New.** Revokes the session and clears the cookie |
| `GET /api/auth/session` | **New.** `200 { email }` if the cookie is valid, else `401` |

`GET /api/auth/session` exists because `HttpOnly` means the client cannot inspect its own auth state.
It is how the app answers "am I logged in?" on load (§7).

`logout` must be a server endpoint for the same reason: JavaScript cannot delete an `HttpOnly`
cookie, so today's client-only `logout()` would leave the session fully alive.

### 6.1 Authentication scheme

`AddJwtBearer` is replaced by a custom `AuthenticationHandler` on a `Session` scheme that reads the
cookie, hashes it, and loads the row. It emits the same `sub` claim the JWT did, so
`ClaimsPrincipalExtensions.UserId()` and every `[Authorize]` controller are untouched.

`JwtTokenService`, `JwtOptions`, and the `Jwt__*` configuration in `appsettings*.json`,
`docker-compose.api.yml`, and `.do/app.yaml` are removed rather than left dangling — including the
`Jwt__SigningKey` secret in the DigitalOcean app spec, which becomes dead. Nothing else in the
codebase issues or reads a JWT; presigned storage URLs are S3-signed and unrelated.

**Deployment note:** every existing session is invalidated by this change. With one real user that
is a re-login, not a migration.

---

## 7. Client surface

The client no longer holds a credential — it holds a *belief* about whether it has one.

- `AuthService` drops `localStorage` and the token signal. Auth state comes from `GET
  /api/auth/session`, resolved once at startup and cached.
- `authGuard` becomes async: it awaits that probe rather than reading a synchronous signal. Without
  this, a reload would race the probe and bounce a logged-in user to `/login`.
- `auth.interceptor.ts` no longer attaches a header. It gains the job the app has been missing: on
  `401`, clear the cached auth state and redirect to `/login` — which is what makes an expired or
  revoked session behave correctly instead of leaving a dead UI on screen.
- `logout()` calls `POST /api/auth/logout` before clearing local state.

---

## 8. Testing

The session rules that can be wrong in a subtle way — when a session is dead, and how often its row
is rewritten — are pure functions of the clock. They live on `Session` as `IsActive` and
`NeedsRenewal`, and are covered by `SessionTests` with no database involved.

**Why the persistence layer is not unit-tested.** The obvious approach, SQLite in-memory, was tried
and abandoned: SQLite cannot translate the `DateTimeOffset` comparison in the cleanup
`ExecuteDelete`, so `IssueAsync` fails outright there. That is a SQLite limitation rather than a
defect in the query — but it also exposes how poor a stand-in it is for this model, which leans on
Postgres for `NULLS NOT DISTINCT`, partial index filters, and a non-default schema. Testing against
SQLite would have meant asserting against a database the app never runs on.

A real Postgres via Testcontainers would be faithful, and was declined deliberately: it would make
Docker a requirement for a suite that currently runs in 40 ms with no infrastructure. The gap is
covered end-to-end instead (§8.1). If sessions grow more logic — per-device revocation, an absolute
cap — that trade is worth revisiting.

### 8.1 End-to-end

Exercised against the local dockerised stack, 2026-07-23:

| Check | Result |
|---|---|
| Register / login | `200`, `Set-Cookie: keepr_session=…; path=/; samesite=lax; httponly`, 30-day expiry |
| `Secure` absent in Development | Confirmed — set outside Development (§5) |
| Authorized call with cookie only | `200` — no `Authorization` header anywhere |
| Session probe without a cookie | `401` |
| Old bearer token in a header | `401` — the JWT path is genuinely gone, not merely unused |
| Wrong invite code | `403` — the registration gate still runs first |
| Logout | `204`, cookie cleared with matching attributes |
| **Replay of a revoked token** | **`401`** — the property a JWT cannot provide |
| Repeat logout | `204` — idempotent |
| Login after logout | `200`, new token, revoked row cleaned up (§4.2) |
| Login with a wrong password | `401` |

**The hashed-storage property, checked directly.** The value in `Sessions.TokenHash` equalled
`sha256(cookie value)` exactly, and a query for the raw token as text matched zero rows — §3.1
holds in the running system, not just in the code.

In the browser: registering navigated into the app, `document.cookie` came back **empty** and
`localStorage` held **no keys** (the old `media.jwt` is gone), a full page reload kept the user
signed in, and logging out revoked the row server-side. No console errors.

---

## 9. Open questions

### ⏳ Q-C1 — `__Host-` cookie prefix
`__Host-keepr_session` would let the browser enforce `Secure` + `Path=/` + no `Domain`, making a
subdomain unable to set a session cookie for the parent. Not adopted because the prefix *requires*
`Secure`, which the local `http://` dev setup does not use — so it would need to be conditional, and
a cookie name that differs between environments is its own small trap.

### ⏳ Q-C2 — Anti-forgery tokens
Deferred per §5. **Revisit if** the API ever moves to a separate origin, or any CORS policy with
`AllowCredentials` is added. Worth a comment on the day someone reaches for `AddCors`.

### ⏳ Q-C3 — "Sign out everywhere" and an active-sessions screen
The table makes both nearly free — revoke all rows for a user, or list them by `UserAgent` and
`LastSeenAt`. Not built because there is no UI asking for it yet. The columns are there so it does
not need a migration later.

### ⏳ Q-C4 — Session cleanup at volume
§4.2 cleans up on login. If sessions ever accrue faster than logins (API clients, many devices), a
sweeper alongside `TrashPurgeService` is the fallback — and would need the same advisory-lock
treatment flagged in [feature-status.md](feature-status.md).

### ⏳ Q-C5 — Rate limiting still absent
Unchanged from [registration-gate-design.md](registration-gate-design.md) Q-R1. Cookies do not
affect it, but a session-issuing endpoint with no throttle is the same open door it was before.

### ⏳ Q-C6 — Idle vs absolute lifetime
§4 implements a purely sliding 30-day window, so a continuously active session never expires. An
absolute cap (say 90 days) would force periodic re-authentication. Not added: with revocation
available, the cap buys little for a single-user deployment.
