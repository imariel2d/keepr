# Admin-Provisioned Accounts & Email Invites — Design

> Feature #36 in [feature-status.md](feature-status.md). Status: **backend implemented**
> (2026-07-30); Angular UI (login trim, admin create/role dialogs, `/profile`, public `/claim`) and
> live verification against the dockerised stack still pending. Backend code:
> `Features/Auth/ClosedRegistrationGate` + `CredentialValidator`, `Features/Email/*` (`IEmailSender`,
> `NoOpEmailSender`, `SmtpEmailSender`, `EmailTemplates`), `Features/Invites/*`,
> `Features/Admin/AdminController` (create/role/resend), `Features/Me/MeController`
> (profile + change-password), migration `AddAccountProvisioning`. Designed 2026-07-29.
>
> Replaces public invite-code self-registration (#3) with **admin-provisioned accounts**. The admin
> creates every account and assigns its role; a new account is made usable either by the admin
> setting a password directly (no email needed) or by sending an **email invite** the recipient
> claims to set their own password. Introduces a **provider-agnostic outbound-email seam**
> (`IEmailSender`) that is *optional* — with no provider configured, everything still works via the
> admin-set-password path. Folds in the **profile section** (#29) and the **change-password** core
> (#28), both of which this model needs.
>
> Builds on: the role model and `AdminSeeder` from [feature-34-admin-console.md](feature-34-admin-console.md),
> the swap-the-gate seam from [feature-3-registration-gate.md](feature-3-registration-gate.md) §9,
> and the credential validation (`EmailPolicy`/`PasswordPolicy`/breach check) from
> [feature-3-registration-validation.md](feature-3-registration-validation.md). Unblocks Q-A4 in
> #34 (notify a kicked user) and moves #26/#27 closer (they need the same email seam).

---

## 1. What changes, in one paragraph

Today anyone who reaches the site and holds the shared invite code can create their own account
(`POST /api/auth/register`, gated by `InviteCodeRegistrationGate`). That is being **turned off**.
From now on the **admin creates accounts** — pick an email, pick a role (User or Admin), and either
type an initial password or send an email invite. Self-service public signup no longer exists. The
invite-code gate is **disabled, not deleted**: the class stays in the tree, dormant, so nothing is
lost and the decision is reversible in one line.

Everything else about auth is unchanged: sessions are still opaque cookie tokens
([feature-3-cookie-session.md](feature-3-cookie-session.md)), login still verifies a BCrypt hash,
the `Admin` policy still guards `/api/admin`.

---

## 2. The four asks, mapped

The request had four parts. This is where each lands:

| Ask | Where in this doc | Touches |
|---|---|---|
| 1. Admin creates accounts, assigns Admin/User role | §4 (create), §5 (role) | extends #34 `AdminController` |
| 2. Profile section for the user | §7 | implements #29, part of #28 |
| 3. Optional, provider-agnostic email invites | §6 (`IEmailSender`), §8 (invite/claim), §9 (the risk) | new infra, unblocks #26/#27 |
| 4. Cove-styled email template for any provider | §10 | new |

And the load-bearing precondition that ties them together:

| Precondition | Where |
|---|---|
| 0. Turn off public self-registration (keep the code) | §3 |

---

## 3. Turning off self-registration (without deleting it)

"Disable it, do not delete it." Two things are in play — the **backend gate** and the **frontend
register form** — and "disable, keep" means something slightly different for each.

### 3.1 Backend: a closed gate, the invite gate kept dormant

The seam already exists and was built for exactly this ([feature-3-registration-gate.md](feature-3-registration-gate.md)
§9): swapping who may register is *one line in `Program.cs`*, `AuthController` untouched.

Add a `ClosedRegistrationGate : IRegistrationGate` that always denies:

```csharp
public Task<GateDecision> EvaluateAsync(RegistrationAttempt attempt, CancellationToken ct)
    => Task.FromResult(GateDecision.Deny(
        "Public sign-up is closed. Ask an admin to create an account for you."));
```

Wire it as the default in `Program.cs`:

```csharp
// Public self-registration is off (#36). InviteCodeRegistrationGate is kept, dormant, so this is
// a one-line reversal. Account creation now goes through the admin (§4) or an invite claim (§8).
builder.Services.AddScoped<IRegistrationGate, ClosedRegistrationGate>();
```

`InviteCodeRegistrationGate` **stays in the repo unchanged and unreferenced** — that is the
"do not delete it". Re-enabling public invite-code signup later is: swap that one line back. Its
unit tests stay green (they test the class directly, not the wiring).

**Why keep `POST /api/auth/register` reachable at all** rather than deleting the action? Because with
the closed gate it now does exactly the right thing — returns `403` "Public sign-up is closed" to
anyone who probes it — and deleting it would also delete the seam, the validation pipeline
(`ValidateCredentials`), and the tests. The endpoint becomes a permanently-closed door, which is
cheaper and safer than bricking up the doorway. The account-creation logic it contains is factored
out and reused by the admin-create path (§4.3) so there is one place that validates + hashes +
inserts a user.

### 3.2 Frontend: login-only, register component kept

The login screen (`features/login/`) currently toggles between "sign in" and "register" modes with
the invite-code field shown only in register mode ([feature-3-registration-gate.md](feature-3-registration-gate.md)
§6). The register mode is **removed from the UI**: no toggle, no invite field, the screen is
sign-in only, and the copy drops "invite-only" language.

The register component code is **kept in the tree** (dormant), mirroring the backend, so re-enabling
is symmetrical. It is simply not routed to.

---

## 4. Admin creates an account

A new endpoint on the existing `AdminController` (`api/admin`, already entirely
`[Authorize(Policy = "Admin")]`).

```
POST /api/admin/users
```

```jsonc
{
  "email": "person@example.com",
  "role": "User",              // "User" | "Admin"
  "sendInvite": false,         // true → email a claim link (§8); requires a configured sender
  "password": "…"              // required when sendInvite=false; omitted when sendInvite=true
}
```

Two mutually-exclusive activation modes, chosen by `sendInvite`:

### 4.1 Direct mode (`sendInvite: false`) — no email needed

This is the ask-3 fallback: *"if no email provider is set you can still make up whatever email and
password the admin sets up."*

1. Validate `email` (`EmailPolicy`) and `password` (`PasswordPolicy` + breach check) — the same
   pipeline registration used, so admin-created accounts are held to the same credential bar.
2. `role` must parse to `User`/`Admin`.
3. Reject if the email already exists (`409`).
4. Create the account **active** (password hashed) with `MustChangePassword = true` (§7.2) — the
   admin knows this password, so the user is forced to change it on first sign-in.
5. Return `201` with the new account's admin detail shape.

The account is immediately usable: the admin hands the person the email + password out-of-band, they
sign in, and are made to set their own password.

### 4.2 Invite mode (`sendInvite: true`) — email a claim link

Requires a configured `IEmailSender` (§6); if none is configured the endpoint rejects with `409`
"Email delivery is not configured — set a password instead." (fail-loud, not silently dropped).

1. Validate `email` + `role`. **No password** is supplied.
2. Create the account in a **pending / unclaimed** state: `PasswordHash = null` (see §8.1), role set.
3. Mint a claim token, store its **hash** in `AccountInvites` (§8.2), email a Cove-styled invite with
   the claim link (§10).
4. Return `201`. The account exists but **cannot be signed into** until claimed (login refuses a
   null-hash account — §8.4).

### 4.3 Shared internals

Both modes go through one internal `AccountFactory`/service method (factored out of
`AuthController.Register`, §3.1) so there is a single place that: normalizes the email, runs
`EmailPolicy`, checks uniqueness, assigns `QuotaBytes` from `QuotaOptions.DefaultBytes`, sets the
role, and inserts the row. Direct mode additionally validates+hashes the password; invite mode
additionally creates the invite + sends the email.

An audit row is written for every admin account creation (reusing `AdminAuditService`, new action
`UserCreated`, target email + role in `Details`). Creation is an admin action and the audit trail
(#34 §5) should record it alongside quota changes and kicks.

---

## 5. Assigning and changing role

Role at **creation** is the `role` field in §4. Changing an **existing** account's role is a
separate endpoint — the "assign role admin or user" ask also applies after the fact:

```
PATCH /api/admin/users/{id}/role   { "role": "Admin" | "User" }
```

Guardrails, reusing the invariants #34 §4.3 already names (they were written anticipating exactly
this endpoint):

- **No self-demote.** An admin cannot demote their own account (prevents accidental lockout).
- **Cannot demote the last admin.** Refused `409` if it would leave zero admins — the check #34 §4.3
  called "load-bearing once a demote endpoint exists". That endpoint is here now.
- Promotion `User → Admin` is unrestricted (other than the caller being an admin).

**The last-admin check must be atomic.** Counting admins and then updating the role is a
check-then-act on the admin *set*, so it has to be serialized or two concurrent demotes each observe
"one other admin remains" and both commit, leaving zero admins. The single-row `SELECT … FOR UPDATE`
the kick path uses (#34 §4.2) is **not** sufficient here: two admins demoting *each other* target
different rows and never contend on that lock. So the demote runs in a transaction that first takes a
**transaction-scoped advisory lock on a fixed "admin-set" key** (`pg_advisory_xact_lock`) — one
serialization point that any future admin-removing path shares — then counts non-pending admins other
than the target and, only if `> 0`, applies the update. The `409` and the no-self-demote guard are
unchanged; this just closes the concurrent-demote gap. (Advisory locks are already in the project's
vocabulary — see the sweeper-leasing follow-up in [feature-status.md](feature-status.md).)

Because the role rides in the session ticket (#34 §2.2), a demotion takes effect on the target's
**next** session validation. For an immediate effect, pair a demote with a session revoke — noted as
Q-P5, not built now (matches #34 Q-A3's "force sign-out is a trivial future add").

---

## 6. The outbound-email seam (`IEmailSender`)

The app has **no outbound email today** — every account-management feature that needs it (#26 reset,
#27 change-email, #34 Q-A4 kick notice) is blocked on this. So the seam is built to be **general**,
not invite-specific, and email is **optional**.

Modelled on `IRegistrationGate`: a narrow interface, a fail-safe default implementation, and swap
the concrete via one `Program.cs` line.

```csharp
public sealed record EmailMessage(
    string ToEmail, string ToName, string Subject, string HtmlBody, string TextBody);

public interface IEmailSender
{
    /// <summary>Delivers one message. Throws on transport failure; callers decide whether that is
    /// fatal (§8.3 treats a failed invite send as non-fatal — the account still exists).</summary>
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
```

Both an `HtmlBody` and a `TextBody`: a well-formed email carries a plain-text alternative, and it is
the fallback for clients that strip HTML.

### 6.1 The default is no-op — email is optional

```csharp
public sealed class NoOpEmailSender(ILogger<NoOpEmailSender> log) : IEmailSender
{
    public Task SendAsync(EmailMessage m, CancellationToken ct)
    {
        log.LogInformation(
            "Email delivery is not configured; dropping message to {To} (subject: {Subject}). "
            + "Set Email__Provider to enable outbound mail.", m.ToEmail, m.Subject);
        return Task.CompletedTask;
    }
}
```

Selected when `Email:Provider` is unset/`none`. Its presence is why invite mode (§4.2) checks for a
*real* sender and rejects rather than silently no-op'ing — you must never think an invite was sent
when it was dropped.

### 6.2 The baseline concrete sender is SMTP

**Recommendation: SMTP first, via MailKit.** Rationale:

- SMTP is the one transport **every** provider speaks — Gmail, Amazon SES, SendGrid, Mailgun,
  Postmark, Resend all expose SMTP credentials. One implementation covers "multiple providers"
  (ask 3's "make it generic") without writing a class per vendor.
- **MailKit** over the built-in `System.Net.Mail.SmtpClient`, which Microsoft's own docs flag as not
  recommended for new development (no modern STARTTLS/auth handling). MailKit is the de-facto .NET
  standard and adds one dependency.

```csharp
public sealed class SmtpEmailSender(IOptions<EmailOptions> opts) : IEmailSender { … }
```

Config (bound to a strongly-typed `EmailOptions` with a `SectionName`, registered in `Program.cs`,
fail-fast at startup if `Provider=smtp` but host/from are blank — the pattern the CodeRabbit
`src/Api` guidance and `ShareOptions` already enforce):

| Env var | Meaning |
|---|---|
| `Email__Provider` | `none` (default) or `smtp`. `none` → `NoOpEmailSender`. |
| `Email__FromAddress` | Envelope + header From, e.g. `no-reply@keepr.app`. |
| `Email__FromName` | Display name, e.g. `Keepr`. |
| `Email__Smtp__Host` / `__Port` | e.g. `smtp.resend.com` / `587`. |
| `Email__Smtp__Username` / `__Password` | SMTP credentials (SECRET in DO). |
| `Email__Smtp__UseStartTls` | Default `true`. |
| `Email__PublicBaseUrl` | Origin for links in emails (claim, later reset). Falls back to `Sharing:PublicBaseUrl` if unset — same public origin. |

Adding an HTTP-API sender later (`ResendEmailSender` calling their REST API for deliverability
features SMTP can't give — webhooks, tagging) is a new class + one `Program.cs` line, exactly like
adding a registration gate. The seam does not change.

### 6.3 Secrets

`Email__Smtp__Password` is a SECRET (DO dashboard, never committed), and per the repo's YAML review
rule it never appears inline in `.do/app.yaml` / compose. Never logged (the `src/Api` secret rule).

---

## 7. Profile section (#29)

The account now has a human behind it who was provisioned rather than self-registered, so a place to
see and edit "who am I" is overdue. This implements #29 and the change-password core of #28.

### 7.1 Data

Add to `User` (one migration):

| Column | Type | Default | Why |
|---|---|---|---|
| `FirstName` | `text` null | null | #29. `cove-avatar` already derives initials from a whitespace-split name. |
| `LastName` | `text` null | null | #29. |
| `MustChangePassword` | `bool` not null | `false` | Set true for admin-set-password accounts (§4.1) and the bootstrap admin (#34 §3 anticipated this flag). Cleared on first change. Existing rows backfill `false`. |

`PasswordHash` also becomes **nullable** — see §8.1 (a pending invited account has no password yet).

### 7.2 Endpoints (on `MeController`, currently GET-only)

| Method & route | Does |
|---|---|
| `GET /api/me/profile` | Returns `{ email, firstName, lastName, role, mustChangePassword }`. The SPA reads this to render the profile screen and to know whether to force the change-password step. |
| `PATCH /api/me/profile` | Updates `{ firstName, lastName }`. Trimmed; length-capped; both optional (an account may have no name). |
| `POST /api/me/password` | Change password: `{ currentPassword, newPassword }`. Verifies current, runs `PasswordPolicy` + breach check on the new one, re-hashes (BCrypt), **revokes the user's other sessions** (keeps the current), clears `MustChangePassword`. This is the #28 core. |

Change-**email** (#27) is deliberately **not** here: it needs verification of the new address, which
needs the email seam *plus* a verify-token flow this doc doesn't build. Noted as out of scope (Q-P4).

### 7.3 Forced change on first sign-in

When `mustChangePassword` is true, the SPA routes the user to the set-password step before anything
else and won't let them into `/files` until `POST /api/me/password` succeeds. This is why §4.1 sets
the flag: the admin knows the initial password, so it must be rotated to something only the user
knows. Invite-claimed accounts (§8) never set the flag — the user chose their own password at claim.

Server-side, `MustChangePassword` does **not** by itself block other endpoints (keeping enforcement
simple and in the SPA); if we later want it airtight, a middleware check is a small add (Q-P6).

### 7.4 Frontend

A `/profile` route (behind `authGuard`) with a Cove form: editable first/last name, read-only email
and role, the storage meter (reuse the `/api/me/usage` widget), and a change-password panel. The
forced-change step (§7.3) reuses the same change-password panel in a minimal, can't-skip layout.

---

## 8. Email invite / claim flow

Only relevant when a sender is configured (§6) and the admin chose invite mode (§4.2).

### 8.1 A pending account has no password

An invited-but-unclaimed account is a real `User` row (so the email is reserved and the role is
fixed) with `PasswordHash = null`. Null hash is the single source of truth for "cannot sign in yet",
which is why §7.1 makes the column nullable. No separate status enum: `PasswordHash IS NULL` +
an unclaimed invite row *is* the pending state, the same way `DeletionRequestedAt IS NOT NULL` *is*
the kicked state (#34). Login already can't match a null hash, but we make the refusal explicit
(§8.4) rather than relying on BCrypt's behaviour with null.

### 8.2 The invite token

New table `AccountInvites`:

| Column | Type | Meaning |
|---|---|---|
| `Id` | uuid PK | |
| `UserId` | uuid FK → Users | The pending account this claims. |
| `TokenHash` | bytea | SHA-256 of the token. **Only the hash is stored** — same rule (and column type) as `Session.TokenHash`; the raw token exists only in the emailed URL. |
| `ExpiresAt` | timestamptz | Default 7 days (`Email:InviteExpiryDays`). An expired invite can be re-sent (§8.5). |
| `ClaimedAt` | timestamptz null | Set when claimed; a claimed invite is dead. |
| `CreatedAt` | timestamptz | |

The token in the URL is a long URL-safe random string (like a share-link token). We look up by hash,
constant-time compare — consistent with how the rest of the app treats bearer secrets.

**One live invite per account is a database invariant, not a convention.** `AccountInvites` carries a
**partial unique index** on `UserId` filtered to `ClaimedAt IS NULL`, so at most one *unclaimed*
invite can exist per account (claimed rows are excluded and never conflict). Without it, "one live
invite" would rest solely on resend deleting before inserting, and two concurrent resends could each
delete-then-insert and leave two valid claim links. With it, the second insert is rejected by the
database; `ResendInvite` maps that to a `409` "try again" (§8.5) rather than a 500.

### 8.3 Sending is non-fatal to account creation

Create the account + invite row and **commit first**, then send the email. If `SendAsync` throws
(SMTP down, bad credentials), the account still exists as pending and the admin sees a "created, but
the invite email failed to send — resend?" state rather than a half-created account or a 500. This
mirrors the "purge retries next tick rather than failing in the request" instinct from the sweepers
(#34 §4.2). The alternative — send inside the transaction — makes a transient SMTP blip roll back a
valid account creation, which is worse.

### 8.4 Claiming

Public, unauthenticated, token-gated (like the `/s/:token` share viewer):

| Method & route | Does |
|---|---|
| `GET /api/invites/{token}` | Validates the token (exists, unexpired, unclaimed). Returns `{ email }` to prime the form. `410 Gone` if expired/claimed/unknown — one opaque response, no oracle for which. |
| `POST /api/invites/{token}/claim` | `{ password }`. Re-validates the token, runs `PasswordPolicy` + breach check, sets `PasswordHash`, stamps `ClaimedAt`, and issues a session (the user is now signed in). All in one transaction. |

`MustChangePassword` stays false — the user just chose the password themselves.

Frontend: a public `/claim/:token` route with a Cove set-password screen (shows the invited email
read-only, a password field with the live policy hints the register form already had). On success,
navigate to `/files`.

### 8.5 Resend / revoke

- **Resend** (`POST /api/admin/users/{id}/invite`): mints a fresh token (invalidating the old one by
  replacing the row), re-sends. Covers a lost email or an expired invite.
- A pending account can be **kicked** like any other (#34) if the admin changes their mind; the
  invite row cascades with the user.

---

## 9. The unverified-email risk (ask 3's warning)

Ask 3 spelled it out: *"if email invites is set in the future this account can be claimed by the
real email user."* This is worth stating precisely because it is a real, accepted trade-off.

**The situation.** In direct mode (§4.1) with no email provider, the admin invents an address —
say `alex@gmail.com` — and a password, and may upload files into that account. **No one has proven
they own `alex@gmail.com`.** Keepr never sent anything there.

**The risk.** If, later, email delivery is enabled *and* an email-based path that grants access to an
existing address is added — most obviously **password reset (#26)**, or an admin re-issuing an
**invite** to that address — then the *real* owner of `alex@gmail.com` could receive a link and take
over the account the admin pre-populated, including its files.

**Why it's bounded.** Keepr is single-owner-per-account with **no cross-user data** — claiming that
account exposes only *that account's own* files, not anyone else's. And the actor who created the
unverified account is a trusted admin. The blast radius is one pre-seeded account.

**Decision (Q-P1): accept it, and surface it.** The admin "create account" screen shows an inline
warning **whenever direct mode is used** (which, with no sender configured, is always):

> ⚠️ This email address is not verified. If email delivery is turned on later, the real owner of
> this address could claim this account and its files. Use invite mode once email is configured.

Once a sender is configured, invite mode (§8) is the default and preferred path precisely because the
claim link proves control of the inbox. Direct mode remains available (e.g. for a service/shared
account with a mailbox no one reads) but is the exception, with the warning attached.

A full fix — an `EmailVerified` flag, and refusing reset/re-invite to an address a *different* live
account already holds unverified — is the real remedy and lands with #26. Out of scope here; noted so
#26 inherits it (Q-P2).

---

## 10. The email template (ask 4)

A single reusable Cove-styled HTML layout, used first by the invite email and then by every future
email (reset, kick notice). "For all providers that support inserting our own UI" = we hand the
provider a full HTML document; providers that render arbitrary HTML (all the SMTP ones) show it
verbatim.

### 10.1 Constraints email imposes on Cove

Email HTML is **not** web HTML. The Cove app leans on CSS variables, flexbox/grid, external fonts,
and `@media (prefers-color-scheme)`. Mail clients (Outlook especially) support almost none of that.
So the template **re-expresses Cove's look with email-safe primitives**, it does not import the app's
CSS:

- **Table-based layout**, not flexbox/grid. A centered single-column ~600px table.
- **Inlined, literal colors** — the actual hex values from `tokens.css`, not `var(--…)` (custom
  properties don't resolve in most mail clients). Cream paper `#FBF9F6` canvas, white `#FFFFFF` card,
  terracotta `#E8703A` primary button, ink `#221D18` / warm-gray `#695D4E` text, subtle border
  `#EDE7DD`.
- **Web-safe font stack**, not the Google-Fonts Sora/Manrope import (mail clients drop `@import` and
  linked webfonts): `font-family: 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;` as a visual
  cousin. The brand still reads through color + layout + the wordmark.
- **A bulletproof button** — a padded, background-filled `<a>` inside a table cell (the pattern that
  survives Outlook), terracotta fill, white text, `border-radius: 10px` (= `--radius-md`).
- Dark mode: a `@media (prefers-color-scheme: dark)` block is included as progressive enhancement
  (Apple Mail / iOS honor it) but the light design is the baseline and must stand alone.

### 10.2 Where it lives and how it's filled

A small server-side render step — a plain C# string/`StringBuilder` templater in
`Features/Email/EmailTemplates.cs` (no Razor dependency for three fields). One layout function takes
`(preheader, headline, bodyParagraphs, ctaText, ctaUrl)` and returns the full HTML document; each
email type (invite now, reset later) is a thin function that fills those in and also produces the
plain-text alternative.

Invite email contents: Keepr wordmark, "You've been invited to Keepr", one line naming who set it up
if available, the **Set your password** button → `{Email:PublicBaseUrl}/claim/{token}`, the raw link
as text (buttons get stripped/blocked), and "this link expires in 7 days."

### 10.3 Verifying it looks right

Because mail-client rendering can't be asserted from a unit test, the template is verified by
rendering the HTML to a file and eyeballing it in a browser (and, before shipping, one real send
through the configured SMTP provider to a test inbox). The plain-text alternative is checked to be
legible on its own. This is the same "exercise it, don't assert it works" bar the engineering-basics
skill sets for user-visible output.

---

## 11. Data-model & migration summary

One migration, `AddAccountProvisioning`:

- `Users.FirstName` (text, null), `Users.LastName` (text, null) — #29.
- `Users.MustChangePassword` (bool, not null, default false; backfill false).
- `Users.PasswordHash` → **nullable** (pending invited accounts, §8.1). Existing rows all have a
  hash, so this only relaxes the constraint.
- New table `AccountInvites` (§8.2), FK to `Users` with cascade delete.
- `AdminActionLogs.Action` gains a `UserCreated` value (no schema change — it's free text, §4.3).

No change to `Sessions`, `MediaFile`, `Folder`, quota, or the share/trash machinery.

---

## 12. API surface (delta)

New / changed endpoints:

| Endpoint | Auth | New? |
|---|---|---|
| `POST /api/admin/users` | Admin | new — create account (§4) |
| `PATCH /api/admin/users/{id}/role` | Admin | new — change role (§5) |
| `POST /api/admin/users/{id}/invite` | Admin | new — resend invite (§8.5) |
| `GET /api/me/profile` | authed | new (§7.2) |
| `PATCH /api/me/profile` | authed | new (§7.2) |
| `POST /api/me/password` | authed | new — change password (§7.2, #28 core) |
| `GET /api/invites/{token}` | anon (token) | new (§8.4) |
| `POST /api/invites/{token}/claim` | anon (token) | new (§8.4) |
| `POST /api/auth/register` | anon | **closed** — now always 403 (§3.1) |

Each new controller/DTO carries XML docs + `[ProducesResponseType]` for every status it returns, and
errors are RFC7807 problem+json — per the `src/Api` conventions and the engineering-basics
Swagger rule. `SessionResponse` is unchanged (role already rides in it since #34).

---

## 13. Build order

Backend-first, each step independently testable in `tests/Api.Tests`:

1. **Close self-registration** — `ClosedRegistrationGate`, swap the `Program.cs` line, drop the
   register mode from the login UI. Smallest, highest-value, no new schema. *(Precondition, §3.)*
2. **Migration** — names, `MustChangePassword`, nullable `PasswordHash`, `AccountInvites` (§11).
3. **Profile + change-password** — `MeController` gains profile GET/PATCH and `POST /password`; the
   `/profile` screen and forced-change step (§7). Independent of email.
4. **Admin create (direct mode)** — `POST /api/admin/users` with a password, `MustChangePassword`
   set, audit; the admin "new account" dialog with the §9 warning. **Fully usable with no email.**
5. **`IEmailSender` seam** — interface, `NoOpEmailSender` default, `SmtpEmailSender` + `EmailOptions`
   + fail-fast wiring + `.env.example`/`.do/app.yaml` (§6). The email template (§10).
6. **Invite mode + claim** — `sendInvite` branch, `AccountInvites`, the invite email, the public
   `/claim/:token` flow, resend (§8).
7. **Role change** — `PATCH …/role` with the last-admin/self-demote guardrails (§5).

Steps 1–4 deliver the whole "admin makes accounts, no email required" story. Steps 5–6 add the
optional email layer on top. Step 7 is small and independent.

---

## 14. Open questions

| # | Question | Current decision |
|---|---|---|
| Q-P1 | Unverified admin-invented emails could be claimed later (§9) | **Accepted & surfaced.** Inline warning in direct mode; invite mode preferred once email is configured. Bounded by single-owner data. |
| Q-P2 | Real fix for Q-P1 (`EmailVerified` flag; block reset/re-invite to an unverified-held address) | Deferred to **#26**, which introduces the reset path that makes the risk live. |
| Q-P3 | Baseline email transport | **SMTP via MailKit** (§6.2) — one impl covers every provider that offers SMTP. HTTP-API senders (Resend/SendGrid) added later behind the same seam. *(Confirm — §16.)* |
| Q-P4 | Change-email (#27) in the profile now? | **No.** Needs new-address verification (email seam + verify-token flow) not built here. Lands with #26/#27. |
| Q-P5 | Immediate effect on demote (revoke sessions on role change) | Deferred. Role change applies on next session validation; pair with a revoke when needed (matches #34 Q-A3). |
| Q-P6 | Server-side enforcement of `MustChangePassword` (middleware), vs SPA-only | SPA-only for now (§7.3); a middleware gate is a small hardening add if we want it airtight. |
| Q-P7 | Force change on admin-set-password accounts | **Yes** (§4.1/§7.3) — the admin knows the initial password, so it must be rotated. *(Confirm — §16.)* |
| Q-P8 | Invite token lifetime | 7 days default, resendable (§8.2/§8.5). Configurable via `Email:InviteExpiryDays`. |

---

## 15. What this does and doesn't buy

**Buys:** the owner controls exactly who has an account (no shared secret to leak, unlike the invite
code — [feature-3-registration-gate.md](feature-3-registration-gate.md) §7), per-account role
assignment, a real profile, and a reusable email capability the whole account-management cluster
(#26/#27, kick notices) has been blocked on. Email stays optional, so a zero-config deploy still
onboards users.

**Doesn't:** it is not multi-tenant (no orgs/teams — that's #24), not self-service (by design), and
does not verify email ownership in direct mode (Q-P1). It doesn't add reset-password (#26) or
change-email (#27); it lays their foundation.

---

## 16. Decisions I need from you before building

Everything above is a proposal. Three forks are genuinely yours to call and reshape the build:

1. **Email transport (Q-P3):** SMTP-via-MailKit as the generic baseline (works with Gmail/SES/
   SendGrid/Mailgun/Postmark/Resend), or start with a specific HTTP provider API? *Recommend SMTP.*
2. **Force password change (Q-P7):** for admin-set-password accounts, force a change on first
   sign-in? This is why change-password (#28 core) is folded in. *Recommend yes.*
3. **Profile scope (§7):** names + change-password now, change-email deferred (Q-P4)? Or pull
   change-email in too (bigger — needs a verify flow)? *Recommend names + change-password only.*
