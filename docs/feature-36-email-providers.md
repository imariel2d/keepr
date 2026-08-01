# Admin-Managed Email Providers — Design

> Feature #36 in [feature-status.md](feature-status.md), extending
> [feature-36-account-provisioning.md](feature-36-account-provisioning.md). Status: **backend +
> Angular admin screen implemented (2026-08-01); live verification pending.**
> Backend: `Domain/EmailSettings` (+ `EmailProvider`), `Features/Email/*`
> (`EmailSettingsService`, `EmailSenderFactory`, `ResendEmailSender`/`BrevoEmailSender`/
> `MailgunEmailSender` over `HostedEmailSender`, `EmailSettingsSeeder`),
> `Features/Admin/EmailSettingsController` (GET/PUT/test), the `EmailSettingsChanged` audit action,
> Data Protection persisted to Postgres, migration `AddEmailSettings`. `InviteService` /
> `AdminController` resolve the sender per send off the DB settings. Frontend: the `/admin/email`
> screen (`features/admin/email-settings`, `adminGuard`) with a write-only key field, per-provider
> fields, and a test-send, plus `core/email-settings.service`. Unit-tested (transports, key
> encryption) and build-verified; an end-to-end run against a real provider is still to come.
> Designed 2026-07-31.
>
> Turns outbound email from a **boot-time env setting** into **runtime, admin-managed
> configuration**: an admin picks a provider and pastes its API key into an admin-only settings
> screen, and the app starts sending — no redeploy. Ships three hosted providers with real free
> tiers (**Resend, Brevo, Mailgun**) behind the existing provider-agnostic `IEmailSender` seam. The
> API keys are secrets, so they are **encrypted at rest** with ASP.NET Data Protection, the key ring
> persisted to Postgres.
>
> Builds on: the `IEmailSender` seam, `NoOpEmailSender`, `SmtpEmailSender`, `EmailTemplates`, and the
> `EmailOptions.Enabled` gate from [feature-36-account-provisioning.md](feature-36-account-provisioning.md)
> §6; the `Admin` policy + `AdminSeeder` from [feature-34-admin-console.md](feature-34-admin-console.md);
> the `AdminActionLogs` audit trail. Once this lands, the invite/claim path (#36) and later
> reset-password (#26) / change-email (#27) all get a mailer an admin can turn on without ops.

---

## 1. What changes, in one paragraph

Today, outbound email is decided **once at startup**: `Program.cs` reads `Email__*` from the
environment, and if a provider is set it registers `SmtpEmailSender`, otherwise `NoOpEmailSender`
(§6 of the provisioning doc). The SMTP password lives in env and never touches the database.
Changing anything means editing env and redeploying — not something a non-ops admin can do. This
feature moves that config **into the database**, managed from an **admin-only screen**: pick one of
three providers, paste its API key, set the From address, send a test, save. The app resolves the
current provider **per send**, so a change takes effect immediately. Env config doesn't disappear —
it becomes a **first-boot seed** so existing deployments keep working.

Everything downstream is unchanged: `InviteService` still calls `IEmailSender.SendAsync`, the Cove
email template (§10 of the provisioning doc) is untouched, and email stays **optional** — with no
provider configured, the no-op sender keeps every email-dependent feature degrading gracefully.

---

## 2. Providers — the three we ship, and why

The ask was "at least 3 providers, free plans." Free tiers were checked in July 2026 (sources at the
end). The permanent-free, API-key providers with usable allowances:

| Provider | Free tier (no expiry) | Daily cap | Credential | Why it's in |
|---|---|---|---|---|
| **Resend** | 3,000 / mo | 100 / day | one Bearer API key | cleanest modern API — a single key, JSON body |
| **Brevo** | ~9,000 / mo | 300 / day | one API key (`api-key` header) | highest free daily allowance |
| **Mailgun** | 3,000 / mo | 100 / day | API key + sending domain + region | widely used; proves the abstraction handles extra config |

Deliberately **not** shipped:

- **SendGrid** — retired its permanent free plan in May 2025; only a 60-day trial remains. A
  "provider with a free plan" that expires in two months isn't one.
- **MailerSend** (cut to 500/mo in Dec 2025), **Postmark** (100/mo) — free tiers too small to be
  useful defaults.
- **Mailjet** (6,000/mo, 200/day) — a fine alternative, but its **key + secret** pair is a second
  credential field; kept as a documented future add rather than one of the initial three.

The three chosen span **three different auth shapes** on purpose — Bearer token (Resend), custom
header (Brevo), and HTTP Basic with a per-domain endpoint (Mailgun). If the seam holds for all
three, a fourth is a new class plus one `switch` arm.

### 2.1 What each provider's send looks like

All three are a single HTTPS POST. The transport classes differ only in URL, auth header, and body
encoding:

| | Resend | Brevo | Mailgun |
|---|---|---|---|
| **Endpoint** | `POST https://api.resend.com/emails` | `POST https://api.brevo.com/v3/smtp/email` | `POST https://api.mailgun.net/v3/{domain}/messages` (US) / `https://api.eu.mailgun.net/...` (EU) |
| **Auth** | `Authorization: Bearer <key>` | `api-key: <key>` | Basic `api:<key>` |
| **Body** | JSON `{from,to,subject,html,text}` | JSON `{sender,to[],subject,htmlContent,textContent}` | `multipart/form-data` `from,to,subject,html,text` |
| **Extra config** | — | — | sending **domain** + **region** (US/EU) |

> Mailgun uses **fixed regional base URLs**, not a region subdomain on one host: US domains send via
> `api.mailgun.net`, EU domains via `api.eu.mailgun.net`. The `MailgunRegion` value (`us`/`eu`)
> selects the base URL from that fixed pair — it is never interpolated into the hostname. Sends must
> be `multipart/form-data`; generic URL-encoding is rejected.

> Exact field names are confirmed against each provider's API reference (see Sources) at
> implementation time before the transport is wired.

### 2.2 The existing SMTP sender

`SmtpEmailSender` stays in the tree as a **pure env-backed channel** — it is *not* stored in
`EmailSettings` and *not* offered in the admin picker. Rationale: it's the odd one out
(host/port/STARTTLS/username/password rather than one key), it's already covered by the provisioning
doc, and keeping the UI to "paste a key" is the whole point. Anyone who needs Gmail/SES/etc. still
configures `Email__Provider=smtp` via env; the DB picker is for the three hosted APIs.

Because it's env-only, SMTP is resolved as a **fallback**, not a stored provider: when the DB
provider is `none` **and** `Email__Provider=smtp` is set in env, the factory builds `SmtpEmailSender`
from env (see the precedence in §5.1). That's what keeps an existing SMTP deployment working after
this upgrade — the seeded `none` row doesn't shadow it, because `none` is exactly the state that
defers to the env fallback.

---

## 3. Data model

One **singleton settings row** — email config is app-wide, not per-user — in a new table
`keepr.EmailSettings`.

```
EmailSettings
  Id                 int        -- fixed singleton (always 1); a CHECK keeps it single-row
  Provider           text       -- 'none' | 'resend' | 'brevo' | 'mailgun'  (smtp only via env)
  FromAddress        text
  FromName           text
  ApiKeyCipher       bytea?     -- Data-Protection-encrypted API key; NULL when Provider='none'
  MailgunDomain      text?      -- Mailgun only
  MailgunRegion      text?      -- Mailgun only: 'us' | 'eu'
  PublicBaseUrl      text       -- origin for links in emails (was Email:PublicBaseUrl)
  InviteExpiryDays   int        -- was Email:InviteExpiryDays
  LastTestAt         timestamptz?   -- result of the most recent "send test"
  LastTestOk         bool?
  LastTestError      text?
  UpdatedAt          timestamptz
  UpdatedByUserId    uuid?      -- FK Users, SET NULL on delete (audit convenience; AdminActionLogs is authoritative)
```

Notes:
- **Secrets are the only encrypted columns** (`ApiKeyCipher`). Everything else is plain — provider
  name, From address, and Mailgun domain/region aren't secrets. This is the app's **first
  reversibly-encrypted** value; every existing secret (bcrypt password hashes, SHA-256 session and
  invite token hashes) is one-way, because those never need to be read back. An API key must be
  decrypted to use it, which is exactly why §4 exists.
- **Provider-specific columns stay few** (just Mailgun's two). If a future provider needs more, a
  small `ConfigJson` column beats a wide sprawl — but two nullable columns don't justify it yet.
- The **key ring** for Data Protection lives in its own table (§4), not here.

---

## 4. Secrets at rest — Data Protection, keys in Postgres

API keys are encrypted with ASP.NET Core's **Data Protection** API (`IDataProtector`) — the same
subsystem that already protects auth cookies and antiforgery tokens. A protector scoped to the
purpose string `"Keepr.EmailSettings.ApiKey"` seals the key with `Protect()` and opens it with
`Unprotect()`; the framework owns the AEAD cipher, key IDs, and rotation, so we hand-roll no crypto.

**The load-bearing detail is where the key ring lives.** Data Protection keeps a rotating set of
master keys and stamps each ciphertext with the key that sealed it. By default, in a container the
ring is written to an **ephemeral path wiped on every redeploy** — after which every stored API key
becomes permanently undecryptable. So the ring is **persisted to Postgres**:

```csharp
// AppDbContext implements IDataProtectionKeyContext (adds a DbSet<DataProtectionKey>).
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("Keepr");   // stable name so the ring is found across instances/restarts
```

This adds a `DataProtectionKeys` table (managed by the `Microsoft.AspNetCore.DataProtection.
EntityFrameworkCore` package) and a migration. The ring now:
- **survives restarts and redeploys** (the actual bug the default hits),
- is **shared across API instances** — instance B can open what instance A sealed,
- needs **no new infrastructure** — it reuses the Postgres we already run.

**The honest limit.** `DataProtectionKeys` sits in the *same* database as `EmailSettings.
ApiKeyCipher`, so a full DB dump contains both ciphertext and the keys to open it. This protects
against the realistic leaks — a key surfacing in **logs**, an **error body**, an **API response**,
or a casual `SELECT` by someone who can read the settings table but not the keys table. It does
**not** defend against exfiltration of the whole database. If that enters the threat model later,
`AddDataProtection().ProtectKeysWith…` wraps the ring with a certificate or an env-provided master
key, and a stolen dump alone becomes useless — **no re-encryption of existing data required**, which
is why this design is structured to allow it as a drop-in follow-up. (Chosen over hand-rolled AES,
which would throw away rotation and make us own the crypto.)

---

## 5. Runtime resolution — from boot-time DI to per-send factory

Today `Program.cs` binds one `IEmailSender` at startup. That can't reflect a DB row that changes at
runtime, so the choice moves to request time:

- **`EmailSettingsService`** (scoped) — reads the singleton row, decrypts `ApiKeyCipher`, and exposes
  `GetAsync()` and `IsEnabledAsync()` (replacing the static `EmailOptions.Enabled`). Sends are rare
  (only on an invite or a test), so it reads the row on demand — **no cache to invalidate**; one
  indexed single-row read per send is negligible.
- **`EmailSenderFactory`** (scoped) — `CreateAsync(ct)` reads settings and returns the transport for
  the current provider (`ResendEmailSender` / `BrevoEmailSender` / `MailgunEmailSender`). When
  `Provider='none'` it applies the §5.1 fallback: `SmtpEmailSender` from env if `Email__Provider=smtp`
  is set, otherwise `NoOpEmailSender`. Each hosted transport is constructed with its decrypted key +
  config and an `IHttpClientFactory` client carrying the **same short timeout** the inline SMTP send
  uses (a dead provider must not hang the admin's request).
- **`InviteService` / `AdminController`** change by one indirection: instead of an injected
  `IEmailSender`, they take the factory (or a thin `IEmailService` wrapper over it) and resolve per
  send. `EmailOptions.Enabled` checks become `await settings.IsEnabledAsync(ct)`.

`IEmailSender` and `EmailMessage` are unchanged, so the transports and the Cove template are reused
verbatim.

### 5.1 Resolution precedence — DB providers, env SMTP, then none

The migration seeds one singleton row (`Provider='none'`), so the row always exists; there is no
"empty table" special case. The factory resolves each send in a fixed order:

1. **DB hosted provider** — if `EmailSettings.Provider` is `resend` / `brevo` / `mailgun`, use it
   (admin-managed, the primary path).
2. **Env SMTP fallback** — else if `Email__Provider=smtp` is set, build `SmtpEmailSender` from env.
   This is what keeps existing SMTP deployments working; SMTP is never written to the DB (§2.2).
3. **None** — else `NoOpEmailSender`.

So the **DB is authoritative for the hosted providers**, env owns SMTP, and the two don't collide.
The boot-time fail-fast validation in `Program.cs` (reject `smtp` with a blank host, etc.) stays for
the **env-SMTP** path only. `PublicBaseUrl` and `InviteExpiryDays` are seeded into the row at first
boot by a post-migration `EmailSettingsSeeder` (alongside `AdminSeeder`), so current config carries
over; after that they're managed through the admin API (§6) and **not** re-read from env. The seed
honours the existing precedence for the origin — `Email__PublicBaseUrl`, then `Sharing:PublicBaseUrl`
— so it stores the URL that links actually resolve to rather than a blank that only works via the
downstream `InviteService` fallback.

**Provider changes and the stored key.** A blank key on `PUT` keeps the stored `ApiKeyCipher` **only
when the provider is unchanged** — an edit that just fixes the From name mustn't force re-entry of the
key. But changing the provider (e.g. Resend → Brevo) **requires a new key**: a key is
provider-specific, and carrying the old one over would send a Resend secret to Brevo. Selecting
`none` **clears `ApiKeyCipher`** (there's no provider to hold a key for), preserving the "NULL when
`Provider='none'`" invariant in §3. The old provider's key is simply overwritten — nothing orphaned;
revoking it on the provider's side is done in that provider's own dashboard.

---

## 6. Admin API

A new `EmailSettingsController`, `[Authorize(Policy="Admin")]` (401 anonymous, 403 non-admin), under
`/api/admin/email-settings`. Every mutation writes an `AdminActionLogs` entry.

| Method | Route | Purpose | Notes |
|---|---|---|---|
| `GET` | `/api/admin/email-settings` | current config for the screen | **never returns the key** — sends `hasApiKey: bool`, provider, From, Mailgun domain/region, `publicBaseUrl`, `inviteExpiryDays`, last-test result |
| `PUT` | `/api/admin/email-settings` | upsert config | key field is **write-only**: blank → keep the stored key **only if the provider is unchanged** (§5.1); a provider change requires a new key; `none` clears the key. Also writes `publicBaseUrl` (absolute http(s) URL) and `inviteExpiryDays` (≥ 1). Validates provider-specific required fields (Mailgun needs domain + region) |
| `POST` | `/api/admin/email-settings/test` | send a test email | uses the **saved** settings (no request body) — the admin must `PUT` first; the UI (§7) saves before it enables Test. Sends to the admin's own address, records `LastTest*`, returns 200 + `{ok,error?}` |

`publicBaseUrl` and `inviteExpiryDays` are stored in `EmailSettings` (§3), so they must be in the
request/response DTOs and the UI — otherwise, after the initial env seed, no one could ever change the
link origin or invite lifetime. They're seeded from env once (§5.1) and admin-managed thereafter.

Test-send **uses saved settings on purpose**: it avoids a second path where an unsaved plaintext key
travels the wire and duplicates provider validation. "Verify before you rely on it" is preserved —
the admin saves, then tests; the §7 UI disables Test while there are unsaved edits so a green result
can never describe a config that isn't the one stored.

Security posture:
- Secrets are **write-only in, masked out** — the plaintext key leaves the browser once (on save)
  and is never returned.
- **Audit payload is a secret-free allowlist.** Each change writes an `EmailSettingsChanged` action to
  `AdminActionLogs` whose `Details` carries **only** allowlisted metadata — provider, From, Mailgun
  domain/region, `publicBaseUrl`, `inviteExpiryDays`, and a `keyChanged: bool`. It must **never**
  contain the request DTO, the plaintext key, `ApiKeyCipher`, or raw provider error text (which can
  echo a key back). A unit test asserts these exclusions.
- **`LastTestError` is a fixed category, not raw provider text** — same rule as the audit payload. A
  failed test send stores one of a small set of safe messages (couldn't decrypt the key; provider
  timed out; provider rejected the request; generic failure) via a `SafeError` map; the full
  exception is *logged*, never persisted or returned, since a provider 401 body or an SMTP auth error
  can echo the submitted key. The key-decrypt path (a lost/rotated Data Protection ring) is resolved
  **inside** the try so it's reported as a failed test, not an unhandled 500.
- Provider endpoints are **fixed known hosts**; the only admin-supplied host-ish values are Mailgun's
  **domain** (path segment, validated) and **region** (a `us`/`eu` enum, not a free URL) — so no SSRF
  surface. (Admins are already the app's most-trusted role.)
- Test-send has a **best-effort, process-local** in-flight guard against a double-click. It is *not*
  cross-instance: two requests hitting different API instances could each send one test email. That's
  acceptable for a low-volume admin action; if it ever needs to be strict, a short DB-backed
  idempotency record would make it cross-instance.

### 6.1 OpenAPI

Each action gets an XML `<summary>`, `<param>` docs on the request DTO, and a
`[ProducesResponseType<T>]` for every status (200 / 400 / 401 / 403), per the repo's
generated-OpenAPI convention. The response DTO models the masked shape (`hasApiKey`, never the key).

---

## 7. Admin UI

A new **`/admin/email`** screen (Cove-styled, `adminGuard` — the same guard the rest of `/admin`
uses; 403s never reach it). It mirrors the existing admin dialogs:

- **Provider** dropdown — None / Resend / Brevo / Mailgun. Selecting one reveals only that provider's
  fields (Mailgun adds domain + region). **Changing the provider clears the key field and requires a
  new one** (§5.1), so a stale key can't ride along.
- **API key** — a write-only field. When a key is already stored (and the provider is unchanged) it
  shows `•••• configured` with a **Replace** affordance rather than a value; leaving it untouched
  keeps the stored key. Reuses the reveal-toggle pattern from the login/claim/profile inputs.
- **From address / From name.**
- **Public base URL / Invite expiry (days)** — the link origin and invite lifetime (§6), editable
  here rather than trapped in env.
- **Save** — `PUT`; surfaces field validation via the shared `problem-details` helpers.
- **Send test email** — button + inline result (green "sent" / red error), driven by `POST …/test`.
  It tests the **saved** config, so it is **disabled while there are unsaved edits** (save first);
  this guarantees a green result describes exactly what's stored, never an unsaved draft.

Nothing here is visible to non-admins: the route is `adminGuard`-protected and the API is
`Admin`-policy-gated, so this is the same two-layer gate (route + server) the console already uses.

---

## 8. Migration

One migration, `AddEmailSettings`:
- creates `keepr.EmailSettings` (§3) with the single-row CHECK and seeds the singleton `none` row
  (`PublicBaseUrl` / `InviteExpiryDays` are then filled at first boot by `EmailSettingsSeeder`, §5.1);
- creates the `DataProtectionKeys` table (§4). Because `Down()` intentionally keeps this table (see
  below), a plain EF `CreateTable` would fail on a rollback-then-reapply — Postgres rejects creating a
  table that already exists. So the migration creates it with **`CREATE TABLE IF NOT EXISTS`** (raw
  SQL matching the Npgsql-generated shape), which is what makes the re-apply a genuine no-op.

**`Down()` drops `EmailSettings` only — it deliberately leaves `DataProtectionKeys` in place.**
Dropping the key ring would be **destructive, not safe**: that table is shared with the framework's
auth-cookie and antiforgery protection, so removing it after any traffic would sign every user out
(cookies fail to validate) *and* make any stored `ApiKeyCipher` permanently undecryptable. Rolling
back this feature should discard its own table, not the shared key infrastructure it introduced;
retiring the key ring is a separate, explicitly-destructive operation (re-seed keys + force
re-authentication), never a side effect of this migration's rollback. Thanks to the `IF NOT EXISTS`
create above, a re-apply after such a rollback is a no-op on the key table.

---

## 9. What this unblocks / relationship to #36

This is the **configuration half** of the email seam #36 introduced. With it:
- #36's **email-invite / claim** path becomes usable by a self-service admin (previously it required
  ops to set env + redeploy, which is why that path is still "verification-pending" in
  feature-status).
- #26 **reset password** and #27 **change email** inherit a working, admin-toggleable mailer — they
  add flows, not infrastructure.

It does **not** change account provisioning or the invite token model — only how the sender is chosen
and how its secret is stored. It does add a **content contract** the existing email template must
meet (§10), but not the template's transport or token mechanics.

---

## 10. Required email contents

Every account-provisioning email — the invite/claim email today, and any future "your account was
created" notice — must carry the elements below. This is the content contract for the Cove template
in [feature-36-account-provisioning.md](feature-36-account-provisioning.md) §10; it's specified here
because it's the same template that every provider in §2 sends, regardless of which one is active.

**The email carries a one-time set-password link, never a password.** Email is not a safe channel
for a credential — it lingers in the mailbox, mail-server logs, and backups — so instead of shipping
the secret we ship a single-use, expiring link and let the recipient choose their own password (the
existing `claim` flow). "Login credentials" in the email therefore means *the address to sign in
with* plus *the link to set the secret*, never the secret itself.

Required:

1. **Cove branding** — the mark + "Keepr", so the message reads as legitimate and not phishing.
2. **Who invited the account** — context for a message the recipient didn't ask for. When the inviter
   is known, "`{invitedByEmail}` invited you to Keepr"; `invitedByEmail` is **nullable**
   (`EmailTemplates.Invite`), so the template **must** fall back to a generic line with no dangling
   placeholder — "You've been invited to Keepr." — and both cases are asserted in `EmailTemplateTests`.
3. **The sign-in identity** — the email address the account signs in with (`{toEmail}`).
4. **A one-time set-password link + primary call-to-action** — a "Set your password" button to
   `{PublicBaseUrl}/claim/{token}`. This is the only credential-bearing element, and it's a
   single-use, expiring token (provisioning doc §8.1), not a password.
5. **A prompt to set the password for security** — framed as "choose your own password to finish."
   The recipient sets their password *during* the one-time claim, which clears the account's pending
   state; there is no forced post-login change in invite mode (the claim controller sets
   `MustChangePassword = false`). `MustChangePassword` applies only to the separate direct-password
   flow, where the admin set a temporary password.
6. **Link expiry** — "This link expires in `{InviteExpiryDays}` days," and note that an admin can
   re-send it once expired (provisioning doc §8.5).
7. **A "did you not expect this?" line** — "If you weren't expecting this, you can ignore this email
   or contact your administrator."
8. **Where to sign in afterwards** — the app URL (`{PublicBaseUrl}`), so a returning user can find
   the login page.
9. **A plain-text alternative** — the message always ships `text` + `html` (the `EmailMessage`
   contract already requires both).

Must **not** contain: a password or any reusable secret, another account's data, or tracking pixels.
Stating "we will never ask for your password by email" explicitly is a cheap anti-phishing cue and
worth including.

> Design consequence: because the email carries a set-password link and never a password, "notify
> the new user by email" *is* the existing **invite mode** — there is no separate "email the
> admin-chosen password" path (which would be the anti-pattern this decision rejects). Direct-password
> mode stays the out-of-band option: the admin sets a password and conveys it themselves. Choosing to
> email the user means choosing the claim flow.

---

## 11. Testing

- **Unit** — `EmailSettingsService` decrypt round-trip; `EmailSenderFactory` returns the right
  transport per provider and `NoOp` for `none`; `PUT` blank-key-keeps / present-key-replaces logic;
  Mailgun-requires-domain validation.
- **Transport** — each hosted sender builds the correct request (URL, auth header, body) against a
  stubbed `HttpMessageHandler`; a non-2xx provider response throws (so callers treat it as they treat
  a failed SMTP send).
- **Authorization** — `GET`/`PUT`/`test` are 401 anonymous, 403 for a non-admin, 200 for an admin
  (the pattern already covering `AdminController`).
- **Email contents** — the rendered provisioning email meets the full §10 contract, asserted against
  **both** the HTML and plain-text bodies: Cove branding, the who-created-it line **with its
  null-inviter fallback** (both cases), the sign-in identity, the set-password link + CTA text, the
  expiry, the "did you not expect this?" line, the post-login app URL, and — the security invariant —
  **no** password or reusable secret anywhere.
- **Live** — against the dockerised stack: save a real free-tier key, send a test, confirm receipt;
  then run the #36 invite/claim path end-to-end (the piece still pending live verification).

---

## Sources (free tiers, checked July 2026)

- Resend — [account quotas & limits](https://resend.com/docs/knowledge-base/account-quotas-and-limits),
  [send email API](https://resend.com/docs/api-reference/emails/send-email)
- Brevo — [email API / free plan](https://www.brevo.com/features/email-api/),
  [send transactional email](https://developers.brevo.com/reference/sendtransacemail)
- Mailgun — [free plan overview](https://www.mailgun.com/blog/email/best-free-email-plans/),
  [messages API](https://documentation.mailgun.com/docs/mailgun/api-reference/send/mailgun/messages/post-v3--domain-name--messages)
- SendGrid free-plan retirement — [Twilio changelog](https://www.twilio.com/en-us/changelog/sendgrid-free-plan)
- ASP.NET Data Protection — [overview](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/introduction),
  [key storage providers](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers),
  [key encryption at rest](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-encryption-at-rest)
