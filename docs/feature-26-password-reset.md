# Forgot / Reset Password — Design

> Feature #26 in [feature-status.md](feature-status.md). Status: **design only** (proposed
> 2026-08-03). Builds directly on #36's email seam, invite/claim token machinery, and admin console.
>
> Two ways to recover a password, exactly as asked:
>
> 1. **Self-service by email** — the user clicks *Forgot password?*, we email a one-time reset link,
>    they choose a new password. Available **only when a mail provider is configured** *and* the
>    account's email is **verified** (§4).
> 2. **Admin manual reset** — an admin resets any account's password from the console, either by
>    setting a new one directly (no email needed) or by emailing the user a reset link. This is the
>    fallback the "*Contact your admin to reset your password*" copy points at.
>
> Builds on: the `IEmailSender` seam + Cove email template + `SecureToken` + the `AccountInvite`
> claim flow from [feature-36-account-provisioning.md](feature-36-account-provisioning.md); the
> role model, `AdminController`, and `AdminAuditService` from
> [feature-34-admin-console.md](feature-34-admin-console.md); the `PasswordPolicy` + breach check
> from [feature-3-registration-validation.md](feature-3-registration-validation.md); the
> revoke-on-credential-change instinct from [feature-3-cookie-session.md](feature-3-cookie-session.md)
> (Q-C3). **Closes** the security debt #36 §9 deferred here: the `EmailVerified` flag (Q-P2 / Q-V6).

---

## 1. The problem, and the one non-obvious constraint

Password reset is mostly mechanical — mint a single-use token, email it, let the holder set a new
password — and every piece already exists from #36 (`SecureToken`, `AccountInvite`, the email
template, the claim page). The self-service half is essentially the claim flow with a shorter-lived
token and an existing account.

The **non-obvious constraint** is why this feature was blocked, and it is the whole reason #26 is
more than a copy-paste of #36 §8:

> A reset link is a bearer key to an account. Emailing it to an address **proves nothing about who
> owns that inbox unless we already know the account holder controls it.**

#36 §9 spelled out the concrete hazard. In direct mode, an admin can invent `alex@gmail.com`, set a
password, and upload files — **no one proved they own that inbox**. If we now let anyone type
`alex@gmail.com` into *Forgot password?* and mail a reset link there, the **real** owner of
`alex@gmail.com` receives a working key to the pre-seeded account and its files. #36 accepted the
unverified-account risk as *bounded* (single-owner data, trusted admin, one account) **and explicitly
deferred the real fix to #26** (Q-P2). This is that fix.

The fix is a single invariant, stated once and enforced everywhere email is involved:

> **Email-based reset is available only to an account whose email is verified.**
> Everything else recovers through an admin.

That one rule is what routes unverified / admin-invented accounts to the "contact your admin" path
automatically, and it's what makes the self-service link safe to send.

---

## 2. The two paths, mapped to what the user sees

| Situation | *Forgot password?* on the login screen shows | Recovery |
|---|---|---|
| Mail provider configured **and** account verified | An actionable link → `/forgot-password` (email form) | Self-service email link (§5) |
| No mail provider configured | Static copy: *"Forgot your password? Contact your admin to reset it."* | Admin manual reset (§6) |
| Mail provider configured but the account is **unverified** | Same actionable link is shown (we can't reveal per-account state on a public page), but a *Forgot password?* request for that address quietly sends nothing — the user is told to contact their admin on the confirmation screen's fine print | Admin manual reset (§6) |

The login page can't branch per-account (it has no idea who's typing), so the **provider-level**
capability (`is email configured at all?`) decides link-vs-copy, and the **per-account** verified
check happens server-side inside the always-neutral `forgot-password` response (§5.1). See §7 for the
capability endpoint that drives the login copy without leaking anything.

---

## 3. Email verification — the load-bearing addition

### 3.1 One new column

Add to `User` (in the same migration as the reset table, §9):

| Column | Type | Default | Meaning |
|---|---|---|---|
| `EmailVerified` | `bool` not null | `false` | The account holder has **proven control of this inbox**. Gates every email-based reset (self-service *and* admin-emailed link). |

### 3.2 What sets it true

Verification means exactly one thing — *someone clicked a link we sent to this address and completed
an action* — so it is set true in precisely the places that prove that:

- **Invite claim** (#36 §8.4): completing `POST /api/invites/{token}/claim` sets `EmailVerified =
  true`. The recipient followed a link that only reached the real inbox. (One line added to the
  existing claim transaction.)
- **Completing an email-based reset** (§5.3): sets `EmailVerified = true` as well. It's a no-op the
  first time (you had to be verified to get the link), but it keeps the invariant total and
  self-healing.
- **The bootstrap admin** (`AdminSeeder`): seeded/ensured with `EmailVerified = true`. The operator
  who sets `Admin__Email` controls the deployment and is expected to control that mailbox, so the
  first admin can self-recover by email once mail is configured — closing the sole-admin lockout
  (Q-26-7). This is a new idempotent step in `AdminSeeder` (it already ensures the account exists and
  is `Admin`; it now also ensures `EmailVerified`), so it self-heals on the next startup after an
  upgrade rather than depending on the migration knowing the env value.

Nothing else sets it. Notably, **admin direct-set-password does not** (§6.1) — the admin typing a
password proves nothing about the inbox.

### 3.3 The backfill decision

Existing rows need a value. The principled backfill: **`true` only where the account has a *claimed*
`AccountInvite`** (that user provably followed an emailed link); **`false` for everyone else** —
direct-set accounts, the bootstrap admin, and any legacy self-registered (#3) account whose address
was never verified. Conservative and correct: unverified accounts simply route to admin reset until
they're verified some other way. (See Q-26-3 for the alternative of trusting legacy #3 self-registered
addresses.)

```sql
-- in the migration's Up(), after the column is added with default false
UPDATE "keepr"."Users" u
SET "EmailVerified" = true
WHERE EXISTS (SELECT 1 FROM "keepr"."AccountInvites" i
              WHERE i."UserId" = u."Id" AND i."ClaimedAt" IS NOT NULL);
```

### 3.4 How an unverified account *becomes* verified

In v1, only invite-claim verifies (§3.2). A direct-set account therefore stays on the admin-reset
path indefinitely — which is fine and safe. A self-service *"verify my email"* action for signed-in
direct-set users (send a link, click it, flip the flag) is a small, natural follow-up but is **out of
scope here** (Q-26-4) — it's really the same machinery as change-email (#27) and belongs with it.

---

## 4. The reset token

A new table, structurally a twin of `AccountInvite` (#36 §8.2) — same `SecureToken` construction,
same hash-only storage, same one-live-per-account database invariant — differing only in a **much
shorter lifetime** and a `UsedAt` marker.

New table `PasswordResetTokens`:

| Column | Type | Meaning |
|---|---|---|
| `Id` | uuid PK | |
| `UserId` | uuid FK → Users (cascade) | The account this resets. |
| `TokenHash` | bytea | SHA-256 of the token (`SecureToken.Hash`). Only the hash is stored; the raw token lives only in the emailed URL. |
| `ExpiresAt` | timestamptz | **1 hour** default (`Email:ResetExpiryMinutes`, §5.4). Reset links are more sensitive and more time-bounded than 7-day invites. |
| `UsedAt` | timestamptz null | Set when the reset completes; a used token is dead. |
| `CreatedAt` | timestamptz | |

```csharp
public bool IsUsable(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;
```

**One live reset per account is a DB invariant, not a convention** — a **partial unique index on
`UserId` filtered to `UsedAt IS NULL`**, exactly as `AccountInvites` filters on `ClaimedAt IS NULL`.
A repeat *Forgot password?* while a link is still live doesn't mint a second key: the request
supersedes the old row (delete-then-insert in a transaction, or reuse), and a concurrent double-submit
that loses the unique-index race is mapped to the neutral 202, never a 500. This also caps token
proliferation for free.

A `SecureToken`-based single-use link doesn't strictly need its own table — it could share a generic
one with invites — but keeping `PasswordResetTokens` separate mirrors the existing `AccountInvites`
split (different lifetime, different semantics, independent cleanup) and keeps each flow legible.

---

## 5. Self-service reset (email)

Three anonymous endpoints on `AuthController`, mirroring the invite trio's shape and its opacity rules.

### 5.1 Request a link — always neutral

```
POST /api/auth/forgot-password   { "email": "..." }   →  202 Accepted (always)
```

The response is **always** `202` with one fixed body — *"If an account with that email can be reset
by email, we've sent a link."* — regardless of whether the address exists, is verified, or mail is
configured. The endpoint must never become an oracle for account existence or verification state.

Internally, a link is minted and sent **only if all** hold:

1. A user with that normalized email exists, **and**
2. `PasswordHash is not null` (the account is active/claimed — a pending unclaimed account has no
   password to reset; it uses resend-invite), **and**
3. `EmailVerified == true` (§3), **and**
4. `EmailSettingsService.IsEnabledAsync(ct)` (a real sender is configured).

If any fails, the endpoint still returns the identical `202` and sends nothing. No branch is
observable from outside.

**Abuse controls** (this is the one public, unauthenticated, email-triggering endpoint):

- **Rate-limit it.** There is no rate limiter in the app today, so this feature adds one:
  ASP.NET's built-in `AddRateLimiter` with a fixed-window partition **per client IP** and
  **per submitted email** (e.g. a few requests / 15 min), applied via `RequireRateLimiting` on this
  action (and reused for the token-consume endpoints). This throttles enumeration attempts and
  outbound-mail abuse without affecting normal use.
- **The one-live-token index** (§4) means hammering *Forgot password?* can't fan out into many valid
  links; each request supersedes the last.
- Timing is kept roughly uniform (do the user lookup regardless); we don't add artificial delay, but
  we don't skip the lookup on the "no user" branch in a way that would make it measurably faster.

Minting + send reuse the #36 pattern: build the row, **commit**, then send the email (a transport
failure is logged, not surfaced — the neutral 202 already went out).

### 5.2 Prime the reset form

```
GET /api/auth/reset-password/{token}   →  200 { email }  |  410 Gone
```

Validates the token (exists, unused, unexpired) and returns the account email to show read-only on
the form. Unknown / used / expired all collapse to **one opaque `410 Gone`** — no oracle for which,
exactly like the invite `Preview`.

### 5.3 Complete the reset

```
POST /api/auth/reset-password/{token}   { "password": "..." }   →  200 SessionResponse | 400 | 410
```

In one transaction:

1. Re-resolve the token; `410` if no longer usable.
2. Run `PasswordPolicy` + breach check on the new password (`CredentialValidator.ValidatePasswordAsync`).
3. **Single-winner** consume: `UPDATE … SET UsedAt = now WHERE Id = @id AND UsedAt IS NULL` — only
   the request that actually flips `UsedAt` proceeds (guards a double-submit), same guard the claim
   flow uses.
4. Set the new BCrypt hash, clear `MustChangePassword`, set `EmailVerified = true` (§3.2).
5. **Revoke every session the user has** — reset means "I may have lost control; sign everyone out"
   (status doc line 41; cookie-session Q-C3). Unlike change-password (§7.2 of #36), which keeps the
   *current* session, a reset has no trusted current session to keep.
6. **Issue one fresh session** for this browser and set the cookie, so the person who just proved
   inbox control and set the password lands signed in — net effect: they're in, everyone else is out.
   *(Alternative in Q-26-1: issue nothing and bounce to `/login` with a success toast — the more
   conservative "you've been signed out everywhere, sign in again" story. Recommended default is
   auto-sign-in for parity with claim; flagged for your call.)*

Returns `SessionResponse` (auto-sign-in) or `204` + redirect (conservative variant).

### 5.4 Config

Reuses the existing email settings — no new provider wiring. One new tunable:

| Setting | Default | Meaning |
|---|---|---|
| `Email:ResetExpiryMinutes` | `60` | Reset-link lifetime. Short by design. Lives alongside `Email:InviteExpiryDays` on the admin email settings (or as a plain config value — Q-26-5). |

The reset link is `{EmailSettings.PublicBaseUrl}/reset-password/{token}`, resolved by the same
`ResolveBaseUrl` fallback the invite uses.

---

## 6. Admin manual reset

A new endpoint on `AdminController`, the fallback the *"Contact your admin"* copy points at and the
**only** recovery for unverified accounts and no-email deployments.

```
POST /api/admin/users/{id}/reset-password
```

```jsonc
{
  "sendLink": false,     // false → set a password directly; true → email a reset link
  "password": "…"        // required when sendLink=false; omitted when sendLink=true
}
```

Preconditions for both modes: target exists, not pending deletion, and **`PasswordHash is not null`**
(an unclaimed account has no password — the admin resends its *invite* instead; `409` with a clear
message otherwise). No last-admin/self guards are needed — a reset changes a credential, it doesn't
remove admin access — though an admin resetting **their own** account is allowed and simply skips the
forced-change flag.

### 6.1 Direct mode (`sendLink: false`) — no email needed

The zero-config path the user described ("*otherwise… admin can manually reset it*").

1. Validate `password` (`PasswordPolicy` + breach check).
2. Set the new BCrypt hash; set **`MustChangePassword = true`** (the admin knows this password, so
   the user must rotate it on next sign-in — same reasoning as admin-create direct mode, #36 §4.1).
   *(Skip the flag when an admin resets their own account.)*
3. **Revoke all the target's sessions** (a reset boots the old credential everywhere).
4. Audit `PasswordReset { method: "direct" }` (§8), committed in the same `SaveChanges`.
5. `200` with the account detail. The admin hands the new password to the user out-of-band.

`EmailVerified` is **not** touched — a direct reset proves nothing about the inbox, so an
admin-invented address stays unverified and keeps routing to admin reset.

### 6.2 Link mode (`sendLink: true`) — email the user a reset link

Same machinery as self-service, initiated by an admin. Requires **`IsEnabledAsync`** *and*
**`EmailVerified == true`** — the same one invariant as §5, because emailing a reset link to an
*unverified* address is precisely §9's takeover hazard. So:

- No sender configured → `409 { code: "email_not_configured" }` (reuse the create-path code so the
  client shows the right message).
- Sender configured but account **unverified** → `409 { code: "email_unverified" }`, "This account's
  email isn't verified — set a password directly instead." This is the admin-facing surface of the
  §9 fix.
- Otherwise → mint a `PasswordResetToken`, send the reset email, `202`. A send failure returns
  `502` (the admin explicitly asked to send, so unlike self-service this *is* surfaced), matching
  `ResendInvite`.

Audited `PasswordReset { method: "link" }`.

---

## 7. Login-page capability (driving the copy, leaking nothing)

The login screen needs to know whether to show an actionable *Forgot password?* link or the static
*contact your admin* copy — a **global** fact (is mail configured?), never a per-account one.

```
GET /api/auth/capabilities   (anon)   →  { "selfServiceReset": true|false }
```

`selfServiceReset` = `EmailSettingsService.IsEnabledAsync`. This reveals only that the deployment can
send mail — no account information — so it's safe to serve anonymously. The login component fetches it
on load:

- `true`  → render *Forgot password?* as a link to `/forgot-password`.
- `false` → render the static line: **"Forgot your password? Contact your admin to reset it."**

(The per-account verified check stays server-side and invisible, §5.1.)

---

## 8. Audit

One new action, appended to the existing enum (free-text column, no schema change — #36 pattern):

```csharp
public enum AdminActionType { …, PasswordReset = 5 }
```

`AdminAuditService.RecordPasswordReset(actorId, actorEmail, target, string method)` writes an
`AdminActionLog` with `Details = {"method":"direct"|"link"}`. **Only admin-initiated resets are
audited** — self-service resets aren't admin actions and don't belong in the admin log (a future
per-user security log is Q-26-6). The revoke-all-sessions effect is implied by the action, as with a
kick.

---

## 9. Data-model & migration summary

One migration, `AddPasswordReset`:

- `Users.EmailVerified` (bool, not null, default false), **backfilled true where a claimed
  `AccountInvite` exists** (§3.3).
- New table `PasswordResetTokens` (§4): FK to `Users` **cascade delete**, unique index on
  `TokenHash`, and a **partial unique index on `UserId` filtered `UsedAt IS NULL`**.
- `AdminActionType.PasswordReset` — no schema change (string column).

No change to `Sessions`, `MediaFile`, `Folder`, quota, sharing, trash, or `AccountInvites`.

---

## 10. API surface (delta)

| Endpoint | Auth | New? |
|---|---|---|
| `POST /api/auth/forgot-password` | anon | new — request a link, always `202` (§5.1) |
| `GET /api/auth/reset-password/{token}` | anon (token) | new — prime the form, `410` opaque (§5.2) |
| `POST /api/auth/reset-password/{token}` | anon (token) | new — set new password + revoke all + sign in (§5.3) |
| `GET /api/auth/capabilities` | anon | new — `{ selfServiceReset }` for login copy (§7) |
| `POST /api/admin/users/{id}/reset-password` | Admin | new — direct or emailed reset (§6) |

Each carries XML docs + `[ProducesResponseType]` for every status, problem+json errors, per the
`src/Api` conventions. `forgot-password` and both token endpoints get `RequireRateLimiting` (§5.1).

---

## 11. Email template

Add `EmailTemplates.PasswordReset(resetUrl, expiryMinutes)` — reuses the existing `Layout(...)`
templater and Cove palette, no new styling:

- Headline: **"Reset your Keepr password"**
- Body: one line, "We received a request to reset your password. Choose a new one below."
- CTA: **"Choose a new password"** → `{PublicBaseUrl}/reset-password/{token}`, plus the raw link as
  text.
- Footer: "This link expires in {N} minutes." and a security line — **"If you didn't request this,
  you can safely ignore this email; your password won't change."**
- A plain-text alternative, as with the invite.

Verified the same way (§10 of #36): render to a file, eyeball in a browser, one real send to a test
inbox before shipping.

---

## 12. Frontend

Two public routes (siblings of the existing `claim/:token` and `s/:token`), plus one admin action:

- **`/forgot-password`** — a Cove card with a single email field. Submitting always shows the same
  neutral confirmation ("If an account with that email can be reset by email, check your inbox for a
  link"), with fine print pointing unverified/no-email users at their admin. Never reveals whether
  the address exists.
- **`/reset-password/:token`** — a set-password screen, a near-clone of `claim.ts` (read-only email
  primed from `GET …/reset-password/{token}`, a password field with the live policy hints, `410`
  handled as "this link is no longer valid — request a new one"). On success → `/files`
  (auto-sign-in) or `/login` with a toast (Q-26-1).
- **Login page** — a *Forgot password?* affordance under the password field, rendered as a link or as
  the "contact your admin" line per `GET /api/auth/capabilities` (§7).
- **Admin account detail** (`/admin/accounts`) — a **Reset password** action opening a dialog that
  mirrors the create dialog: *Set a password* (direct) or *Email a reset link* (enabled only when
  mail is configured and the account is verified; disabled with the reason otherwise). The direct tab
  carries the §9-style inline note when the address is unverified.

Accessibility per the frontend skill: the confirmation and any error use `role="alert"` /
`aria-live`, the password field has the same reveal-toggle + `aria-pressed` pattern as login, and the
whole flow is keyboard-operable — matching the existing auth screens.

---

## 13. Build order

Backend-first, each step testable in `tests/Api.Tests`:

1. **`EmailVerified` + migration** — column, backfill, set it on invite-claim (§3). Independent,
   ships the security foundation with no user-visible change.
2. **`PasswordResetTokens` + self-service endpoints** — table, `forgot-password` (neutral + rate
   limit), the two token endpoints, revoke-all-then-sign-in (§4–5). The email template (§11).
3. **Capability endpoint + login copy + the two public pages** (§7, §12).
4. **Admin manual reset** — `POST …/reset-password` (direct + link), the audit action, the admin
   dialog (§6, §8).

Steps 1–2 are the whole self-service story; step 4 is the admin fallback and is independent. Either
half is shippable alone (step 4 needs only step 1's `EmailVerified` for its link mode's gate).

---

## 14. E2E scenarios & expected output

There is no browser e2e framework here, so "end-to-end" means the whole journey across the real
boundaries this feature crosses: Angular → API → Postgres → the email provider → back to the screen.
The automated slice lands as **xUnit integration tests in `tests/Api.Tests`** driving the real
endpoints against the test DB with a **fake `IEmailSender`** (captures the message + the raw token in
the link, asserting on it — the token never appears in any response, so the capture is the only way
to follow the link). The manual slice is the click-path run against the dockerised stack with a real
provider (or Mailpit) before shipping. Each scenario names its concrete expected output — status,
body, rows changed, side effect, and what the user sees.

### 14.1 Happy path — self-service reset (verified account, mail configured)

The primary journey, end to end.

1. **Capability →** `GET /api/auth/capabilities` → `200 { "selfServiceReset": true }`. Login screen
   renders *Forgot password?* as a link to `/forgot-password`.
2. **Request →** user submits `alex@keepr.app` at `/forgot-password` →
   `POST /api/auth/forgot-password { email }` → **`202`**, body *"If an account with that email can be
   reset by email, we've sent a link."* Screen shows that neutral confirmation.
   - **Rows:** one `PasswordResetTokens` row inserted (`UserId` = alex, `TokenHash` = SHA-256 of the
     raw token, `ExpiresAt` = now + 60 min, `UsedAt` = null).
   - **Side effect:** exactly one email captured — subject *"Reset your Keepr password"*, HTML +
     text bodies, CTA URL `{PublicBaseUrl}/reset-password/{rawToken}`, footer *"expires in 60
     minutes"* + *"If you didn't request this…"*.
3. **Prime →** `GET /api/auth/reset-password/{rawToken}` → **`200 { "email": "alex@keepr.app" }`**.
   `/reset-password/:token` renders the email read-only + a password field with live policy hints.
4. **Complete →** `POST /api/auth/reset-password/{rawToken} { password: "<new, strong, unbreached>" }`
   → **`200`** `SessionResponse { email: "alex@keepr.app", role: "User" }`, `Set-Cookie` with a new
   session. SPA lands on `/files` signed in.
   - **Rows:** alex's `PasswordHash` = new BCrypt hash; `MustChangePassword` = false;
     `EmailVerified` = true (already was); the token row's `UsedAt` = now; **all of alex's prior
     `Sessions` `RevokedAt` = now**, and **one new** live session exists (the reset browser).
   - **Invariant checks:** old password no longer authenticates; a previously-live session on another
     device returns `401` on its next call; the raw token now returns `410` (used).

### 14.2 Edge / error paths

| # | Scenario | Expected output |
|---|---|---|
| E1 | `forgot-password` for a **non-existent** email | **`202`**, identical neutral body. **No** token row, **no** email sent. (No oracle: indistinguishable from the happy path's step 2 from outside.) |
| E2 | `forgot-password` for an **unverified** account (`EmailVerified = false`) | **`202`**, identical body. **No** token, **no** email. |
| E3 | `forgot-password` for a **pending/unclaimed** account (`PasswordHash IS NULL`) | **`202`**, identical body. **No** token, **no** email (nothing to reset — that account uses resend-invite). |
| E4 | `forgot-password` when **no mail provider** is configured | **`202`**, identical body. **No** token, **no** email. And `GET /api/auth/capabilities` → `{ selfServiceReset: false }`, so the login screen shows *"Contact your admin to reset it."* instead of a link. |
| E5 | `forgot-password` **rate limit** exceeded (many requests from one IP/email) | **`429`** once the fixed window is exhausted; no token minted for the throttled requests. |
| E6 | Prime/complete with an **unknown, expired, or already-used** token | **`410 Gone`**, one opaque body — the three cases are indistinguishable. No row change. |
| E7 | Complete with a **weak or breached** password | **`400`** `ValidationProblemDetails` from `PasswordPolicy`/breach check. Token **not** consumed (`UsedAt` stays null), no session issued — the user can retry the same link. |
| E8 | **Double-submit** of a valid completion (two concurrent POSTs, same token) | Exactly **one** wins (`200` + session); the loser gets **`410`** (the `UPDATE … WHERE UsedAt IS NULL` flips for only one). Never two sessions, never a 500. |
| E9 | Repeat `forgot-password` while a link is still live | **`202`**; the prior token row is superseded (one live token per account, partial unique index), so the **old** link now `410`s and only the newest works. A lost race maps to the neutral `202`, not a 500. |
| E10 | Transport failure when sending (SMTP/provider down) | Self-service: still **`202`** (the neutral response already committed); the failure is logged, the token row exists (a retry supersedes it). |

### 14.3 Admin manual reset

1. **Direct mode (no email needed).** Admin opens alex in `/admin/accounts` → *Reset password* →
   *Set a password* → `POST /api/admin/users/{id}/reset-password { sendLink: false, password }` →
   **`200`** account detail.
   - **Rows:** alex's `PasswordHash` = new hash; **`MustChangePassword` = true**; `EmailVerified`
     **unchanged**; **all alex's sessions revoked**; one `AdminActionLog` row `PasswordReset {
     method: "direct" }` (actor = admin, target = alex), committed in the same `SaveChanges`.
   - **Next login:** alex signs in with the admin-given password and is forced through the can't-skip
     change-password step (the #36 forced-change guard) before reaching `/files`.
2. **Link mode (verified account, mail configured).** `{ sendLink: true }` → **`202`**; one
   `PasswordResetTokens` row + one reset email captured (same shape as §14.1). Admin log
   `PasswordReset { method: "link" }`.

**Admin error paths:**

| # | Scenario | Expected output |
|---|---|---|
| A1 | Non-admin calls the endpoint | **`403`** (the `Admin` policy). |
| A2 | Target is **pending/unclaimed** (`PasswordHash IS NULL`) | **`409`**, "This account hasn't been claimed — resend its invite instead." No change. |
| A3 | `sendLink: true` but **no mail provider** configured | **`409`** `{ code: "email_not_configured" }`, "Email delivery is not configured — set a password instead." |
| A4 | `sendLink: true` but the account is **unverified** | **`409`** `{ code: "email_unverified" }`, "This account's email isn't verified — set a password directly instead." (The §9 fix, admin-facing.) |
| A5 | `sendLink: true`, everything valid, but the **send fails** | **`502`** (the admin explicitly asked to send, so unlike self-service this *is* surfaced), matching `ResendInvite`. Token row exists; admin can retry. |
| A6 | Direct mode with a **weak/breached** password | **`400`** `ValidationProblemDetails`; no change, no audit row. |
| A7 | Admin resets **their own** account (direct) | **`200`**; `MustChangePassword` stays **false** for self (no pointless forced rotation); sessions still revoked (issue a fresh login normally). |

### 14.4 Invariants (must hold across all of the above)

- **No oracle.** `forgot-password` returns the identical `202` body whether the account exists, is
  verified, is pending, or mail is off (E1–E4). The prime/complete endpoints collapse
  unknown/expired/used into one `410` (E6).
- **Email-based reset ⇔ verified.** No reset email is ever sent to an `EmailVerified = false`
  account, by *any* path — self-service silently drops (E2), admin link mode refuses `409`
  `email_unverified` (A4). The only way to reset an unverified account is admin **direct** mode.
- **A completed reset boots every session** and consumes the token (single-use, E8). A verified
  account stays verified; a reset never *lowers* verification.
- **Tokens are hash-only and single-live.** The raw token never appears in any API response; at most
  one usable `PasswordResetTokens` row exists per account (E9).
- **`EmailVerified` is only ever set true by proving inbox control** — invite-claim, a completed
  email reset, or the bootstrap-admin seed — never by an admin typing a password.

### 14.5 What gets automated vs. run by hand

- **Automated (`tests/Api.Tests`, fake sender):** §14.1 steps 2–4, E1–E3, E6–E9, §14.3 direct mode +
  A2/A4/A6/A7, and every §14.4 invariant. These are pure API + DB assertions and the bulk of the
  value.
- **Manual (dockerised stack + real/Mailpit provider):** the actual email *rendering* and delivery
  (§14.1 step 2's captured HTML eyeballed in a browser, §11), the rate-limit window (E5), the two
  **frontend** click-paths (`/forgot-password`, `/reset-password/:token`) and the login-page
  link-vs-copy branch (E4) — the same "exercise the user-visible output, don't just assert it" bar
  the other features were verified against.

---

## 15. Open questions

| # | Question | Resolution |
|---|---|---|
| Q-26-1 | After a self-service reset: auto-sign-in (issue a fresh session), or bounce to `/login`? | **Decided: auto-sign-in** (issue a fresh session for the reset browser after revoking all others). |
| Q-26-2 | Reset-token lifetime | **Decided: 60 minutes** (`Email:ResetExpiryMinutes`). |
| Q-26-3 | Backfill `EmailVerified` for legacy self-registered (#3) accounts too? | **Decided: no** — backfill true only for claimed-invite accounts (§3.3); the bootstrap admin is verified separately via the seeder (§3.2). Legacy accounts fall to admin reset until re-verified. |
| Q-26-4 | Self-service *"verify my email"* for direct-set accounts now? | **Defer** — same machinery as change-email (#27); v1 verifies only via invite-claim. |
| Q-26-5 | Where does `ResetExpiryMinutes` live — admin email settings row, or plain config? | Recommend a plain config value first (it's a policy knob, not a per-provider secret); promote to the admin screen if operators want to tune it live. |
| Q-26-6 | Audit self-service resets anywhere? | Admin log is for admin actions; a per-user security/activity log is a separate future feature. |
| Q-26-7 | **Sole-admin lockout.** `AdminSeeder` never resets an existing account's password (#34 Q-A1), so re-setting `Admin__Password` is **not** a break-glass. | **Decided:** the bootstrap admin is seeded `EmailVerified = true` (§3.2), so once mail is configured it can self-recover by email; and the docs state **keep ≥2 admins**. A single admin with *no* mail configured still relies on a second admin or a DB-level intervention — accepted, and the reason the ≥2-admins guidance is explicit. |

---

## 16. What this buys, and what it doesn't

**Buys:** self-service recovery when email is configured; an always-available admin fallback for
everything else; and — the reason it was blocked — it **closes #36 §9 / Q-P2** by making
`EmailVerified` the single gate on every email-based reset, so an admin-invented address can never be
handed to a stranger via a reset link. A successful reset boots every existing session, matching the
"I lost control" intent.

**Doesn't:** it doesn't add a general "verify my email" action for direct-set accounts (Q-26-4), a
per-user security log (Q-26-6), or change-email (#27) — though it lays #27's verification groundwork.
It doesn't solve sole-unverified-admin lockout beyond "keep two admins" (Q-26-7).

---

## 17. Decisions — locked (2026-08-03)

The three forks are settled; the design above reflects them:

1. **Post-reset behavior (Q-26-1):** **auto-sign-in** — after a self-service reset, revoke every
   session and issue one fresh session for the reset browser.
2. **Bootstrap admin verification (Q-26-7):** the env-seeded admin is created `EmailVerified = true`
   via `AdminSeeder`, and the docs state **keep ≥2 admins**.
3. **Legacy backfill (Q-26-3):** verify only claimed-invite accounts; legacy #3 self-registered
   accounts backfill `false` and use admin reset until verified.
