# Change Email — Design

> Feature #27 in [feature-status.md](feature-status.md). Status: **backend + Angular UI built and
> verified live; automated e2e + mail-off run pending** (design 2026-08-04, backend 2026-08-04, UI
> 2026-08-07). The backend implements §3–§11 (the `EmailChangeTokens` table + `AddEmailChange`
> migration, `POST`/`DELETE /api/me/email`, the anon `confirm-email/{token}` preview/confirm, the two
> email templates, and the `ProfileResponse` extension). The §12 UI is built — a Change-email card on
> `/profile` and the public `/confirm-email/:token` page. Both were verified against the Mailpit stack:
> the §14.1 mail-on journey (through the real UI) + the §14.3 edge paths, plus responsive/a11y. The
> automated **journey F** and the §14.2 mail-off live run remain. Builds directly on #26's
> `EmailVerified` invariant and its token machinery, and on #36's email seam + Cove email template.
>
> One capability: let a signed-in user change the address their account signs in with. The whole
> design turns on a single question the user raised — **is a mail provider configured?** — because
> that decides whether the new address can be *proven* before it takes effect:
>
> 1. **Mail configured → verify-before-commit.** The change is staged; a confirmation link is emailed
>    to the **new** address; the change lands (and the new address becomes verified) only when that
>    link is clicked. The old address keeps working until then.
> 2. **No mail configured → immediate, unverified.** There is no way to prove the new inbox, so the
>    change applies at once and the account's email is marked **unverified** — exactly the state a
>    direct-set (#36 §4.1) account is already in.
>
> Builds on: `User.EmailVerified` + the `SecureToken`/hash-only token pattern +
> `PasswordResetService`'s shape from [feature-26-password-reset.md](feature-26-password-reset.md);
> the `IEmailSender` factory, `EmailSettingsService.IsEnabledAsync`, and the Cove email template from
> [feature-36-account-provisioning.md](feature-36-account-provisioning.md) /
> [feature-36-email-providers.md](feature-36-email-providers.md); `EmailPolicy` +
> `CredentialValidator` from [feature-3-registration-validation.md](feature-3-registration-validation.md);
> the re-authenticate-then-change and revoke instincts from `MeController.ChangePassword` (#28).

---

## 1. The problem, and the one load-bearing constraint

Changing an email is mostly mechanical — validate the address, check it's free, write it to
`User.Email`. Every piece already exists: `EmailPolicy` validates well-formedness and blocks
disposable domains, `Email` is unique + normalized in `AppDbContext`, and `CredentialValidator`
already reuses `EmailPolicy` for registration.

The **load-bearing constraint** is the same one that blocked #26, seen from the other side:

> The account's email is both the **login identifier** and the **recovery channel**. Pointing it at
> a new address that the holder hasn't proven they control quietly breaks the #26 invariant —
> *email-based recovery is available only to a verified address* — unless the new address is
> re-verified.

There's a concrete takeover chain this must not open. Account-takeover playbooks run
**change-email → forgot-password**: switch the login address to one the attacker controls, then reset
the password to it. Keepr is *already* immune to that, and #27 must keep it that way, for free:

> A password reset link is only ever sent to an `EmailVerified` address (#26 §5.1). So even after an
> email change, a reset is impossible until the **new** address is verified — and verifying it
> requires clicking a link that only reaches the **new** inbox's real owner.

That is the whole reason the mail-on path is **verify-before-commit** and the mail-off path lands
**unverified**: in both, an unproven address can never become a working recovery channel. The change
never *lowers* the security bar — it either proves the new address or marks it unproven.

Two more invariants fall out and are enforced everywhere below:

- **Re-authenticate first.** Changing the login address is a sensitive account change, so — exactly
  like change-password (#28) — the request must carry the **current password**. A stolen *session*
  alone (no password) can't move the email, which is what closes the takeover chain at step one.
- **Uniqueness is a DB fact, not a check.** `Email` is unique; the target is validated at request
  time *and* re-checked at confirm time (the gap between them is a real, if small, window).

---

## 2. The path, mapped to what the user sees

The profile screen (`/profile`, #29) gains a **Change email** panel. What it does depends only on the
**provider-level** capability *"is mail configured at all?"* — never on per-account state.

| Situation | Change-email panel shows | What happens |
|---|---|---|
| Mail provider configured | New-email + current-password fields; on submit, *"Confirm the link we sent to `new@x`."* | **Staged.** A confirmation link goes to the new address; the change lands on click (§5.3). Old address still signs in until then. |
| No mail provider configured | Same fields, with the note *"Email delivery isn't set up, so your new address can't be verified — it'll change right away."* | **Immediate.** Email swapped, `EmailVerified = false` (§5.1). |
| A change is already pending (mail on) | A *"Pending: confirm `new@x`"* line with **Resend** and **Cancel** | Resend re-sends the link; Cancel drops the pending change (§5.4). |

The panel reads the mail-on/off flag from the existing capability endpoint
`GET /api/auth/capabilities` (#26 §7) — it already returns exactly `EmailSettingsService.IsEnabledAsync`,
which is precisely this fact and leaks nothing about any account.

---

## 3. Re-authentication

Both modes require the current password, verified the same way `MeController.ChangePassword` does:

```csharp
if (user.PasswordHash is null || !BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
    return Problem("Your current password is incorrect.", statusCode: 400);
```

A `null` hash can't reach this authenticated endpoint (an unclaimed account can't sign in), but the
guard is kept so `Verify` never sees null — identical to #28.

Unlike a password reset, an email change **does not revoke sessions**. The actual secret (the
password) is unchanged and was just re-verified, and sessions are keyed to the user id, not the
email, so they stay valid. (A password reset revokes everything because the secret rotated; here
nothing secret rotated.)

---

## 4. The pending email-change token

A new table, structurally a twin of `PasswordResetToken` (#26 §4) — same `SecureToken` construction,
same hash-only storage, same one-live-per-account invariant — with one extra column: the **target
address** it will move the account to.

New table `EmailChangeTokens`:

| Column | Type | Meaning |
|---|---|---|
| `Id` | uuid PK | |
| `UserId` | uuid FK → Users (cascade) | The account being changed. |
| `NewEmail` | text | The normalized (trimmed, lowercased) target address. The pending state *is* this row. |
| `TokenHash` | bytea | SHA-256 of the token (`SecureToken.Hash`); only the hash is stored. Unique index. |
| `ExpiresAt` | timestamptz | `Email:EmailChangeExpiryMinutes` (§5.5), **1440 (24 h)** default. |
| `UsedAt` | timestamptz null | Set when the change is confirmed; a used token is dead. |
| `CreatedAt` | timestamptz | |

```csharp
public bool IsUsable(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;
```

**One live change per account is a DB invariant** — a **partial unique index on `UserId` filtered to
`UsedAt IS NULL`**, exactly as `PasswordResetTokens` filters. A second *Change email* request
supersedes the first (delete-then-insert in a transaction); a concurrent double-submit that loses the
unique-index race is mapped to a clean response, never a 500.

The token exists **only in the mail-on path**. In the mail-off path there is nothing to confirm, so
no row is written — the change is applied inline (§5.1).

---

## 5. Self-service change

Two authenticated endpoints on `MeController` (the request + cancel), and two anonymous
token-authorized endpoints on `AuthController` (preview + confirm), mirroring the reset trio's shape.

### 5.1 Request a change

```
POST /api/me/email   { "newEmail": "...", "currentPassword": "..." }
```

Shared preamble (both modes):

1. Re-authenticate (§3). Wrong password → `400`.
2. Normalize `newEmail` (trim + lowercase). Validate with `EmailPolicy.Validate` → `400`
   `ValidationProblemDetails { email }` on a malformed or disposable address.
3. **No-op guard:** `newEmail == user.Email` → `400 { code: "email_unchanged" }`, "That's already
   your email."
4. **Uniqueness:** another account already has `newEmail` → `409 { code: "email_in_use" }`, "That
   email is already in use." *(This is an authenticated existence check; see §5.6 for why the mild
   oracle is acceptable here.)*

Then the two modes diverge on `EmailSettingsService.IsEnabledAsync`:

**Mail ON → stage it (verify-before-commit).**

5. Supersede any prior pending change for this user, mint an `EmailChangeToken { NewEmail = newEmail }`,
   **commit**.
6. Send the confirmation email to **`newEmail`** (§11) — dispatched on a background task with its own
   DI scope, like the reset send, so the request returns promptly and a transport hiccup is logged,
   not surfaced.
7. `202 Accepted { pendingEmail: "new@x" }`. `User.Email` is **unchanged**; the account still signs in
   with the old address.
   - *A send failure still returns `202`* (the user asked to change, the row exists, Resend supersedes
     it) — consistent with self-service reset. The profile screen's *Resend* is the recovery.

**Mail OFF → apply immediately (unverified).**

5. Set `user.Email = newEmail`; set **`user.EmailVerified = false`** (the new address is unproven and
   there is no channel to prove it); `SaveChanges`.
6. `200 ProfileResponse` with the new email and `emailVerified: false`.

**Rate limit.** `POST /api/me/email` triggers outbound mail (mail-on), so it carries a new
`[EnableRateLimiting(RateLimiterPolicies.ChangeEmail)]` policy — a fixed window keyed **per user**
(the endpoint is authenticated, so the account id is the natural partition, not the client IP #26
uses). 5 / 15 min is plenty for a human correcting a typo and caps mail fan-out.

### 5.2 Prime the confirmation form

```
GET /api/auth/confirm-email/{token}   →  200 { newEmail }  |  410 Gone
```

Anonymous, token-authorized (the link may be opened in any browser, signed in or not — same as the
reset/claim links). Resolves the token (exists, unused, unexpired) and returns the target address to
show on the confirm screen. Unknown / used / expired collapse to **one opaque `410 Gone`**.

`GET` is deliberately side-effect-free: an email client or link-prefetcher that fetches the URL must
**not** confirm the change. The confirmation happens only on the explicit `POST` below.

### 5.3 Confirm the change

```
POST /api/auth/confirm-email/{token}   →  200 { email }  |  410 Gone  |  409 Conflict
```

In one transaction:

1. Re-resolve the token; `410` if no longer usable.
2. **Re-check uniqueness** of `NewEmail` (another account may have taken it since step 5.1 — unlikely
   under admin-only registration, but the window is real). Taken → `409 { code: "email_in_use" }`.
3. **Single-winner consume:** `UPDATE … SET UsedAt = now WHERE Id = @id AND UsedAt IS NULL` — only the
   request that flips `UsedAt` proceeds (guards a double-submit), same guard reset/claim use.
4. Set `user.Email = row.NewEmail`; set **`user.EmailVerified = true`** — clicking a link that reached
   the new inbox *is* the proof of control (§1). `SaveChanges` + commit.
5. **Notify the old address** (mail is on): a heads-up email to the *previous* address that the account
   email was changed (§11), so the original owner can react if it wasn't them. Dispatched on the
   background task; a failure is logged, never surfaced.
6. `200 { email: newEmail }`.

**No auto-sign-in and no session changes.** Unlike a reset (which issues a fresh session because it
booted all the others), an email change touches no session — the user is typically already signed in
elsewhere. The confirm screen shows success with a link to `/files` (or `/login` if this browser has
no session).

### 5.4 Cancel / pending status

```
DELETE /api/me/email   →  204 No Content  |  404 Not Found
```

Drops the user's live pending change (deletes the row). `404` when there's nothing pending. The
profile screen offers **Cancel** and **Resend** next to a pending line; *Resend* is just `POST
/api/me/email` again with the same address (it supersedes and re-sends).

The pending state is surfaced by folding it into the existing profile read rather than a new endpoint:
`GET /api/me/profile` gains `EmailVerified` and `PendingEmail` (the live token's `NewEmail`, or null)
— see §8.

### 5.5 Config

One new tunable, startup-validated exactly like `Email:ResetExpiryMinutes`:

| Setting | Default | Meaning |
|---|---|---|
| `Email:EmailChangeExpiryMinutes` | `1440` (24 h) | Confirmation-link lifetime. Longer than the 60-min reset link — confirming a new address is less time-critical and the user may not check the new inbox immediately. **Range-validated at startup** (`Program.cs`) to `[1, 10080]` (≤ 1 week); a `0`/negative or absurd value fails boot with a message naming the env var, like the reset/SMTP checks. |

The confirmation link is `{EmailSettings.PublicBaseUrl}/confirm-email/{token}`, resolved by the same
`ResolveBaseUrl` fallback the invite/reset use.

### 5.6 On the uniqueness oracle

Unlike `forgot-password` (anonymous, deliberately neutral), `POST /api/me/email` is **authenticated**
and must give a real user real errors — "that email is taken" is necessary UX. It does leak that an
address is registered, but the caller is a signed-in account, registration is admin-only
(`ClosedRegistrationGate`, #36), and the account set is small and controlled. The mild oracle is
accepted, matching how registration itself reports a duplicate. (A neutral "we sent a link" that never
confirms would be safer but actively misleads a legitimate user who fat-fingered an address.)

---

## 6. Admin change-email (fallback — optional)

Symmetric with admin manual reset (#26 §6): an admin sets any account's email directly from the
console. It's the only recovery when mail is off *and* a user can't reach their own account, and it's
useful for fixing an admin-invented typo.

```
POST /api/admin/users/{id}/email   { "newEmail": "..." }
```

- Validate `EmailPolicy` + uniqueness; `409 email_in_use` on collision.
- Set `Email = newEmail`, `EmailVerified = false` (an admin typing an address proves nothing about the
  inbox — the exact rule as admin direct-set reset, #26 §6.1).
- Audit `EmailChanged { by: admin }` to `AdminActionLogs` (new `AdminActionType`, string column, no
  schema change — the #26/#36 pattern).
- No re-auth of the *target* (the admin is the authority); the acting admin is already `Admin`-policy
  gated.

**Recommended: defer to a follow-up.** The self-service flow (§5) is the feature the user asked for
and stands alone. Admin change-email is a small, independent add and is flagged in Q-27-5 for a
yes/no call rather than built by default.

---

## 7. `EmailVerified` interplay

The state transitions, stated once:

| Before | Action | After `Email` | After `EmailVerified` |
|---|---|---|---|
| any | Mail-on request (`POST /api/me/email`) | **unchanged** | **unchanged** (nothing committed yet) |
| any | Mail-on **confirm** | `NewEmail` | **`true`** (inbox proven) |
| verified or not | Mail-off request | `newEmail` | **`false`** (unproven, no channel) |
| any | Admin change-email (§6) | `newEmail` | **`false`** |

Within the change-email flow, the only path that yields `EmailVerified = true` is clicking a link
delivered to the new address — the same single rule #26 established. (Password reset also assigns the
flag, but only on an account that is *already* verified, so it re-asserts rather than grants it: every
path that sets `EmailVerified` first proves inbox control, and none weakens the invariant.) An email
change therefore never fabricates verification; it either proves the new address or marks it unproven,
and the account simply routes to admin reset until some later verification (its own #27 confirm, or a
future "verify my email" action, #26 Q-26-4).

---

## 8. Profile capability & response

The profile screen needs three facts it doesn't have today: whether mail is on (to pick the panel
copy), whether the current email is verified (to show a badge), and whether a change is pending.

- **Mail on/off** → reuse `GET /api/auth/capabilities` (`{ selfServiceReset }` == `IsEnabledAsync`),
  fetched by the profile screen; no new endpoint.
- **Verified + pending** → extend `ProfileResponse` (`GET /api/me/profile`):

```csharp
public record ProfileResponse(
    string Email, string? FirstName, string? LastName, string Role, bool MustChangePassword,
    bool EmailVerified, string? PendingEmail);   // ← two new fields
```

`PendingEmail` is the live `EmailChangeToken.NewEmail` for the user, or null. Both are cheap reads on
a screen that's already loaded per visit.

---

## 9. Data-model & migration summary

One migration, `AddEmailChange`:

- New table `EmailChangeTokens` (§4): FK to `Users` **cascade delete**, unique index on `TokenHash`,
  and a **partial unique index on `UserId` filtered `UsedAt IS NULL`**.
- `AdminActionType.EmailChanged` — only if §6 ships; no schema change (string column).

**No change to `Users`** — `EmailVerified` already exists (from #26's `AddPasswordReset`), and the
pending address lives in the token table. No change to `Sessions`, `MediaFile`, `Folder`, quota,
sharing, trash, `AccountInvites`, or `PasswordResetTokens`.

---

## 10. API surface (delta)

| Endpoint | Auth | New? |
|---|---|---|
| `POST /api/me/email` | user | new — request a change; `202` staged (mail on) or `200` applied (mail off); `400`/`409`/`429` (§5.1) |
| `DELETE /api/me/email` | user | new — cancel a pending change; `204`/`404` (§5.4) |
| `GET /api/auth/confirm-email/{token}` | anon (token) | new — prime the confirm form; `410` opaque (§5.2) |
| `POST /api/auth/confirm-email/{token}` | anon (token) | new — apply the change + verify; `410`/`409` (§5.3) |
| `GET /api/me/profile` | user | **changed** — `ProfileResponse` gains `EmailVerified`, `PendingEmail` (§8) |
| `POST /api/admin/users/{id}/email` | Admin | new — **optional** direct set (§6, Q-27-5) |

Each carries XML docs + `[ProducesResponseType]` for every status and problem+json errors, per the
`src/Api` conventions. Only `POST /api/me/email` carries `[EnableRateLimiting]` (§5.1); the
token-consume endpoints are guarded by the unguessable 256-bit token itself.

---

## 11. Email templates

Two additions to `EmailTemplates`, reusing the existing `Layout(...)` + Cove palette (no new styling):

- **`ConfirmEmailChange(confirmUrl, expiryMinutes)`** → sent to the **new** address.
  - Headline: **"Confirm your new email"**
  - Body: "Confirm this address to start using it to sign in to Keepr."
  - CTA: **"Confirm this email"** → `{PublicBaseUrl}/confirm-email/{token}`, plus the raw link as text.
  - Footer: "This link expires in {N} hours." + "If you didn't request this, you can ignore this
    email — nothing will change."
- **`EmailChanged(newEmailMasked)`** → sent to the **old** address on completion (mail-on, §5.3).
  - Headline: **"Your Keepr email was changed"**
  - Body: "Your account email was changed to {masked}. If this wasn't you, contact your admin right
    away." No CTA. The new address is masked (e.g. `n•••@example.com`) so the heads-up doesn't itself
    spell out the full new address in the old inbox.

Both carry a plain-text alternative, as with invite/reset, and are unit-tested like
`EmailTemplateTests` (link present in both bodies, minute/hour-accurate expiry, the reassurance line).

---

## 12. Frontend

- **Profile `/profile` (#29)** — a new **Change email** card below the name/password cards:
  - Read-only current email with a **Verified / Unverified** badge (from `ProfileResponse.EmailVerified`).
  - New-email + current-password fields (the password field reuses login's reveal-toggle +
    `aria-pressed` pattern). Submit → success copy that branches on the response: *"Confirm the link
    we sent to `new@x`"* (`202`) or *"Your email is now `new@x`"* (`200`).
  - When `PendingEmail` is set: a *"Pending: confirm `new@x`"* line with **Resend** and **Cancel**.
  - The mail-off note (*"…can't be verified — it'll change right away"*) shows when capabilities report
    mail is off.
- **`/confirm-email/:token`** — a public page, sibling of `/reset-password/:token` and `/claim/:token`
  and a near-clone of them: prime with `GET …/confirm-email/{token}` (shows the new address read-only),
  a single **Confirm** button → `POST`, `410` handled as "this link is no longer valid — start the
  change again from your profile," `409` as "that address is now in use." On success → *"Your email is
  now `new@x`"* with a link to `/files` or `/login`.

Accessibility per the **frontend-developer** skill: confirmation/errors use `role="alert"` /
`aria-live`; the whole flow is keyboard-operable and responsive; the verified badge is not
colour-only (icon + text). Matches the existing auth/profile screens.

---

## 13. Build order

Backend-first, each step independently testable:

1. **`EmailChangeTokens` + migration** — table, one-live invariant, the domain `IsUsable`. Ships the
   foundation with no user-visible change.
2. **Request + confirm endpoints** — `POST /api/me/email` (both modes + rate limit), the two anon
   token endpoints, `DELETE` cancel; the two email templates (§11).
3. **Profile response extension + the profile panel + `/confirm-email/:token`** (§8, §12).
4. **(Optional) Admin change-email** — `POST /api/admin/users/{id}/email` + audit (§6, Q-27-5).

Steps 1–3 are the whole self-service story; step 4 is independent.

---

## 14. E2E scenarios & expected output

"End-to-end" here is the whole journey across the boundaries this feature crosses: Angular → API →
Postgres → the email provider → back to the screen. The repo now has a **Playwright** suite with a
**Mailpit** overlay (`docker-compose.e2e.yml`, see [testing-strategy.md](testing-strategy.md)), so —
unlike #26 when it was written — the mail-on path is fully automatable: a **journey F** (planned, lands
with the frontend) will drive it and read the confirmation email from Mailpit. The pure functions
(`EmailChangeToken.IsUsable`, the two templates) **are** unit-tested in `tests/Api.Tests` (added with
the backend). Each scenario names its concrete expected output.

### 14.1 Happy path — mail configured (verify-before-commit)

1. **Request →** signed-in user submits `new@keepr.app` + current password at `/profile` →
   `POST /api/me/email` → **`202 { pendingEmail: "new@keepr.app" }`**. Screen shows *"Confirm the link
   we sent to new@keepr.app"* and a Pending line.
   - **Rows:** one `EmailChangeTokens` row (`UserId`, `NewEmail = new@keepr.app`,
     `TokenHash = SHA-256(raw)`, `ExpiresAt = now + 24 h`, `UsedAt = null`). `Users.Email`
     **unchanged**.
   - **Side effect:** exactly one email captured, **to `new@keepr.app`**, subject *"Confirm your new
     email"*, CTA `{PublicBaseUrl}/confirm-email/{raw}`.
2. **Prime →** `GET /api/auth/confirm-email/{raw}` → **`200 { newEmail: "new@keepr.app" }`**.
3. **Confirm →** `POST /api/auth/confirm-email/{raw}` → **`200 { email: "new@keepr.app" }`**.
   - **Rows:** `Users.Email = new@keepr.app`; `Users.EmailVerified = true`; token `UsedAt = now`.
     **Sessions untouched.**
   - **Side effect:** one heads-up email **to the old address**, subject *"Your Keepr email was
     changed"*, body naming the masked new address; **no** email to the new address.
   - **Invariant checks:** signing in now requires `new@keepr.app`; the old address no longer signs in;
     the raw token now `410`s.

### 14.2 Happy path — no mail provider (immediate, unverified)

1. **Request →** same submit, but `IsEnabledAsync` is false → `POST /api/me/email` →
   **`200 ProfileResponse { email: "new@keepr.app", emailVerified: false }`**. Screen shows *"Your
   email is now new@keepr.app"*.
   - **Rows:** `Users.Email = new@keepr.app`, `Users.EmailVerified = false`. **No** `EmailChangeTokens`
     row, **no** email.
   - **Invariant:** a subsequent `forgot-password` for `new@keepr.app` sends nothing (#26 §5.1 — the
     address is unverified), so the change-email→reset takeover stays closed.

### 14.3 Edge / error paths

| # | Scenario | Expected output |
|---|---|---|
| E1 | Wrong current password | **`400`**, "Your current password is incorrect." No row, no email, email unchanged. |
| E2 | Malformed / disposable new email | **`400`** `ValidationProblemDetails { email }` from `EmailPolicy`. No change. |
| E3 | New email **equals** current | **`400`** `{ code: "email_unchanged" }`. No change. |
| E4 | New email **already used** by another account (at request) | **`409`** `{ code: "email_in_use" }`. No token, no email. |
| E5 | Confirm with **unknown / expired / used** token | **`410 Gone`**, one opaque body. No change. |
| E6 | New email **taken between request and confirm** | Confirm → **`409`** `{ code: "email_in_use" }`; token **not** consumed (`UsedAt` stays null); email unchanged. The user can cancel and retry with another address. |
| E7 | **Double-submit** of a valid confirm | Exactly **one** wins (`200`); the loser gets **`410`** (the `UPDATE … WHERE UsedAt IS NULL` flips once). Never two changes, never a 500. |
| E8 | Repeat *Change email* while one is pending | **`202`**; the prior token is superseded (one-live partial index), the **old** link now `410`s, only the newest works. A lost race maps to a clean response, not a 500. |
| E9 | `GET` the confirm link (prefetch) then never `POST` | `GET` returns `200` and **changes nothing**; the change lands only on `POST`. |
| E10 | Cancel a pending change (`DELETE /api/me/email`) | **`204`**; row deleted; the emailed link now `410`s. `DELETE` with nothing pending → **`404`**. |
| E11 | Rate limit exceeded (>5 requests/15 min for one user) | **`429`** with an `application/problem+json` body (a `detail` the client can show); no token minted for the throttled requests. |
| E12 | Transport failure sending the confirm email (mail on) | Still **`202`** (the row exists; *Resend* supersedes/retries); logged, not surfaced. |
| E13 | **Concurrent request race for the new address** (mail-off): two accounts pass the existence check for the same address, both write | The loser's write hits the unique `Users.Email` index; each request runs in a transaction that maps the `DbUpdateException` to **`409`** `{ code: "email_in_use" }` and rolls back (email unchanged) — **never a raw 500**, matching the confirm path. |
| E14 | **Concurrent same-account requests** (mail-on): two requests both supersede then insert a token | One wins (**`202`**); the other loses the one-live-token slot (partial unique index) and its transaction rolls back — preserving the winner's live token — returning **`409`** `{ code: "email_change_pending" }`, not a 500. |

### 14.4 Invariants (must hold across all of the above)

- **Re-auth gates every self-service change.** No email moves without the current password (E1). A
  session alone can't do it — this is step one of the takeover chain, closed.
- **Email-based recovery ⇔ verified, still.** After any change, `forgot-password` for the new address
  works only once the new address is `EmailVerified` — i.e. only after the mail-on **confirm**
  (§14.1), never after a mail-off change (§14.2) or an admin set (§6). The takeover chain stays shut.
- **`EmailVerified` is set true only by proving inbox control** — here, clicking the confirm link.
  Never by a request, a mail-off change, or an admin typing an address (§7).
- **Verify-before-commit.** In mail-on, `Users.Email` never changes until confirm; a typo'd or
  abandoned request leaves the account fully working on the old address (E-abandon = E8/E10).
- **One live change per account; tokens are hash-only and single-use** (E7, E8). The raw token never
  appears in any API response — Mailpit is the only place it's readable, which is what the journey
  asserts on.
- **The old owner is told.** A completed mail-on change emails the *old* address (§14.1); a change
  never silently moves the recovery channel.

### 14.5 What gets automated vs. run by hand

- **Automated (`tests/Api.Tests`, pure functions) — done:** `EmailChangeToken.IsUsable`
  (unused/expired/spent); `ConfirmEmailChange` (the confirm link in both bodies, expiry text, the
  reassurance line); `EmailChanged` (the masked address + the "contact your admin" heads-up, no link,
  and HTML-encoding of the value); and `EmailChangeService.Mask`. Mirrors `PasswordResetToken` /
  `EmailTemplates` tests.
- **Automated (Playwright **journey F**, mail-on) — planned, lands with the frontend:** §14.1 end to end — request from `/profile`, read
  the confirm email from Mailpit, `GET` prime, `POST` confirm, then assert new-email sign-in works, old
  fails, the link `410`s, and the old-address heads-up arrived. Plus E5–E8, E10 through the API. Runs
  in the `e2e` CI job on a fresh stack (default `Provider=None` → env-SMTP → Mailpit).
- **Manual (dockerised stack):** the **mail-off** path (§14.2) — flip `EmailSettings.Provider` to
  `None` *and* remove the env-SMTP overlay so `IsEnabledAsync` is false — and the admin path (§6) if
  built. The mail-off branch can't be exercised in the standard e2e stack, which always has the Mailpit
  SMTP fallback on.

---

## 15. Open questions

| # | Question | Recommendation |
|---|---|---|
| Q-27-1 | Confirmation-link lifetime | **24 h** (`Email:EmailChangeExpiryMinutes = 1440`) — friendlier than the 60-min reset link for confirming a new inbox; still bounded. |
| Q-27-2 | Mail-off behaviour: immediate-unverified vs. block-and-route-to-admin? | **Immediate-unverified.** It's genuinely useful (fix a typo, move addresses) and safe under single-owner data — the new address is unproven so it can never receive a reset. Blocking would strand every no-mail deployment. |
| Q-27-3 | Notify the **old** address at request time too, not just on completion? | **Completion only** for v1 — one heads-up, less noise. Add a request-time "a change was requested" notice later if abuse warrants. |
| Q-27-4 | Auto-sign-in on confirm (parity with reset), or leave sessions alone? | **Leave sessions alone** — the secret didn't rotate and the user is usually already signed in; the confirm page just links onward. |
| Q-27-5 | Build **admin change-email** (§6) now or defer? | **Defer** to a small follow-up unless a no-mail deployment needs the fallback immediately. The self-service flow is the requested feature and is complete without it. |
| Q-27-6 | Should a mail-off change be allowed to *keep* a previously-verified account verified? | **No.** Verification is per-address; a new, unproven address must start unverified even if the old one was verified. |

---

## 16. What this buys, and what it doesn't

**Buys:** self-service email change that's safe with mail on (verify-before-commit, the new address
proven before it takes effect) and workable with mail off (immediate, honestly marked unverified). It
**reuses** #26's `EmailVerified` invariant to keep Keepr immune to the change-email→reset takeover for
free, and tells the old inbox when a change completes.

**Doesn't:** it doesn't add a general "verify my email" action for already-unverified accounts
(that's #26 Q-26-4, shared machinery, still deferred), and it defers admin change-email (§6, Q-27-5).
It doesn't revoke sessions — by design, since no secret rotates.
