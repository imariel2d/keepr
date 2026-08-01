# Admin-Managed Email Providers — Design

> Feature #36 in [feature-status.md](feature-status.md), extending
> [feature-36-account-provisioning.md](feature-36-account-provisioning.md). Status: **designed
> (2026-07-31), not built.**
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
| **Endpoint** | `POST https://api.resend.com/emails` | `POST https://api.brevo.com/v3/smtp/email` | `POST https://api.{region}.mailgun.net/v3/{domain}/messages` |
| **Auth** | `Authorization: Bearer <key>` | `api-key: <key>` | Basic `api:<key>` |
| **Body** | JSON `{from,to,subject,html,text}` | JSON `{sender,to[],subject,htmlContent,textContent}` | form-encoded `from,to,subject,html,text` |
| **Extra config** | — | — | sending **domain** + **region** (US/EU) |

> Exact field names are confirmed against each provider's API reference (see Sources) at
> implementation time before the transport is wired.

### 2.2 The existing SMTP sender

`SmtpEmailSender` stays in the tree and stays wired to the env-seed path, but is **not** offered in
the admin picker. Rationale: it's the odd one out (host/port/STARTTLS/username/password rather than
one key), it's already covered by the provisioning doc, and keeping the UI to "paste a key" is the
whole point. Anyone who needs Gmail/SES/etc. can still configure `Email__Provider=smtp` via env; the
DB picker is for the three hosted APIs.

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
  the current provider (`ResendEmailSender` / `BrevoEmailSender` / `MailgunEmailSender`), or
  `NoOpEmailSender` when `Provider='none'`. Each hosted transport is constructed with its decrypted
  key + config and an `IHttpClientFactory` client carrying the **same short timeout** the inline SMTP
  send uses (a dead provider must not hang the admin's request).
- **`InviteService` / `AdminController`** change by one indirection: instead of an injected
  `IEmailSender`, they take the factory (or a thin `IEmailService` wrapper over it) and resolve per
  send. `EmailOptions.Enabled` checks become `await settings.IsEnabledAsync(ct)`.

`IEmailSender` and `EmailMessage` are unchanged, so the transports and the Cove template are reused
verbatim.

### 5.1 Env becomes a seed, not the source of truth

On first boot, if `EmailSettings` is empty, seed the row from the existing `Email__*` env values
(mirrors `AdminSeeder`). After that the **DB is authoritative** and env is ignored for the managed
fields. This keeps current deployments working with zero change, lets ops set an initial provider via
env if they want, and hands ongoing control to the admin screen. The boot-time fail-fast validation
in `Program.cs` (reject `smtp` with a blank host, etc.) stays for the **env-seed** path only.

---

## 6. Admin API

A new `EmailSettingsController`, `[Authorize(Policy="Admin")]` (401 anonymous, 403 non-admin), under
`/api/admin/email-settings`. Every mutation writes an `AdminActionLogs` entry.

| Method | Route | Purpose | Notes |
|---|---|---|---|
| `GET` | `/api/admin/email-settings` | current config for the screen | **never returns the key** — sends `hasApiKey: bool`, provider, From, Mailgun domain/region, last-test result |
| `PUT` | `/api/admin/email-settings` | upsert config | key field is **write-only**: omitted/blank → keep the stored key; present → validate + re-encrypt + replace. Validates provider-specific required fields (Mailgun needs domain + region) |
| `POST` | `/api/admin/email-settings/test` | send a test email | sends via the **saved or just-submitted** settings to the admin's own address; records `LastTest*`. Returns 200 + `{ok,error?}` so the UI shows a clear pass/fail before anyone relies on it |

Security posture:
- Secrets are **write-only in, masked out** — the plaintext key leaves the browser once (on save)
  and is never returned.
- Provider endpoints are **fixed known hosts**; the only admin-supplied host-ish values are Mailgun's
  **domain** (path segment, validated) and **region** (a `us`/`eu` enum, not a free URL) — so no SSRF
  surface. (Admins are already the app's most-trusted role.)
- Test-send has a simple **in-flight guard** so a double-click can't fan out real emails.

### 6.1 OpenAPI

Each action gets an XML `<summary>`, `<param>` docs on the request DTO, and a
`[ProducesResponseType<T>]` for every status (200 / 400 / 401 / 403), per the repo's
generated-OpenAPI convention. The response DTO models the masked shape (`hasApiKey`, never the key).

---

## 7. Admin UI

A new **`/admin/email`** screen (Cove-styled, `adminGuard` — the same guard the rest of `/admin`
uses; 403s never reach it). It mirrors the existing admin dialogs:

- **Provider** dropdown — None / Resend / Brevo / Mailgun. Selecting one reveals only that provider's
  fields (Mailgun adds domain + region).
- **API key** — a write-only field. When a key is already stored it shows `•••• configured` with a
  **Replace** affordance rather than a value; leaving it untouched keeps the stored key. Reuses the
  reveal-toggle pattern from the login/claim/profile inputs.
- **From address / From name.**
- **Send test email** — button + inline result (green "sent" / red error), driven by `POST …/test`,
  so the admin verifies delivery before saving becomes meaningful.
- **Save** — `PUT`; surfaces field validation via the shared `problem-details` helpers.

Nothing here is visible to non-admins: the route is `adminGuard`-protected and the API is
`Admin`-policy-gated, so this is the same two-layer gate (route + server) the console already uses.

---

## 8. Migration

One migration, `AddEmailSettings`:
- creates `keepr.EmailSettings` (§3) with the single-row CHECK and seeds the singleton `none` row;
- adds the `DataProtectionKeys` table (§4) via the EF Core key-storage package.

`Down()` drops both. No existing table changes, so the migration is additive and safe to roll back
(the down simply removes the two new tables; no data in older tables is touched).

---

## 9. What this unblocks / relationship to #36

This is the **configuration half** of the email seam #36 introduced. With it:
- #36's **email-invite / claim** path becomes usable by a self-service admin (previously it required
  ops to set env + redeploy, which is why that path is still "verification-pending" in
  feature-status).
- #26 **reset password** and #27 **change email** inherit a working, admin-toggleable mailer — they
  add flows, not infrastructure.

It does **not** change account provisioning, the invite token model, or the email template — only how
the sender is chosen and how its secret is stored.

---

## 10. Testing

- **Unit** — `EmailSettingsService` decrypt round-trip; `EmailSenderFactory` returns the right
  transport per provider and `NoOp` for `none`; `PUT` blank-key-keeps / present-key-replaces logic;
  Mailgun-requires-domain validation.
- **Transport** — each hosted sender builds the correct request (URL, auth header, body) against a
  stubbed `HttpMessageHandler`; a non-2xx provider response throws (so callers treat it as they treat
  a failed SMTP send).
- **Authorization** — `GET`/`PUT`/`test` are 401 anonymous, 403 for a non-admin, 200 for an admin
  (the pattern already covering `AdminController`).
- **Live** — against the dockerised stack: save a real free-tier key, send a test, confirm receipt;
  then run the #36 invite/claim path end-to-end (the piece still pending live verification).

---

## Sources (free tiers, checked July 2026)

- Resend — [account quotas & limits](https://resend.com/docs/knowledge-base/account-quotas-and-limits),
  [send email API](https://resend.com/docs/api-reference/emails/send-email)
- Brevo — [email API / free plan](https://www.brevo.com/features/email-api/),
  [send transactional email](https://developers.brevo.com/reference/sendtransacemail)
- Mailgun — [free plan overview](https://www.mailgun.com/blog/email/best-free-email-plans/),
  [messages API](https://documentation.mailgun.com/docs/mailgun/api-reference/openapi-final/tag/Messages/)
- SendGrid free-plan retirement — [Twilio changelog](https://www.twilio.com/en-us/changelog/sendgrid-free-plan)
- ASP.NET Data Protection — [overview](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/introduction),
  [key storage providers](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers),
  [key encryption at rest](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-encryption-at-rest)
