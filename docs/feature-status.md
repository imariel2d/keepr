# Feature Status

Tracking the planned feature set against what is actually implemented in the codebase.

Keepr is a **personal media store** with a folder hierarchy, rename, and a 10-day trash:
single-owner (no user-to-user sharing yet). Of the 38 planned features, **14 are complete**
(backend + UI), 3 are partial (built, verification or per-screen work remaining), and 21 are not started.

**Legend:** ✅ Done · 🟡 Partial · 📐 Designed (not built) · ❌ Not started

---

## Tier 1 — Core (nothing works without these)

| # | Feature | Status | Evidence / gap |
|---|---------|--------|----------------|
| 1 | Upload/download files | ✅ | Presigned S3 multipart upload (`src/Api/Features/Uploads/UploadsController.cs`) + presigned GET download (`src/Api/Features/Media/MediaController.cs`) |
| 2 | Folder hierarchy (create, nested, move) | ✅ | Backend: `Folder` entity (adjacency list), recursive-CTE subtree/breadcrumbs, cycle + depth guards, auto-suffix naming. UI: `src/ClientApp/src/app/features/files/` — breadcrumb nav, card grid, drag-and-drop + "Move to…" picker. Storage stays flat `{ownerId}/{uuid}` (FD1) |
| 3 | Authentication | ✅ | Register/login/logout (`src/Api/Features/Auth/AuthController.cs`). The session is an opaque id in an HttpOnly cookie backed by a `Sessions` table, so it is revocable and slides over 30 days — see [feature-3-cookie-session.md](feature-3-cookie-session.md). Signup is invite-gated behind `IRegistrationGate` — see [feature-3-registration-gate.md](feature-3-registration-gate.md) |
| 4 | File/folder metadata storage | ✅ | File metadata (`src/Api/Domain/MediaFile.cs`) + folder metadata (`src/Api/Domain/Folder.cs`) |
| 5 | Rename/delete | ✅ | Rename via `PATCH /api/media/{id}` + `PATCH /api/folders/{id}`, exposed in the card context menu. Delete is now soft (#8) |

## Tier 2 — Makes it usable as a product

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 6 | Sharing with specific users (view/edit) | ❌ | No share/permission model; everything is owner-scoped |
| 7 | Shareable links | ✅ | [feature-7-shareable-links.md](feature-7-shareable-links.md). Single-file capability URLs (`src/Api/Features/Sharing/`), owner-editable expiry (1/7/30 days or never), per-link + whole-file revoke, presigned-R2 resolve gated by `PreviewPolicy`. UI: public `/s/:token` viewer + an owner Share dialog (create, copy active links, edit expiry, revoke). Verified end-to-end against the dockerised stack. Tokens are stored so links are re-copyable (Q-S5); Q5 risk accepted for single-owner sharing, scanning still required before #6 |
| 8 | Trash / soft delete with restore | ✅ | `DeletedAt`/`DeletedRootId`, EF global query filters, `TrashController`, `TrashPurgeService` sweeper at 10 days. UI: `features/trash/` with restore, purge, empty, and a "in Trash" line on the quota meter. **Overrides Q9 hard delete** |
| 9 | Search by file name | ✅ | [feature-9-search.md](feature-9-search.md). **Done — verified live 2026-08-02.** Owner-scoped name search over the whole tree, matching **files and folders** by a case-insensitive substring (`SearchController` on `OriginalNameLower`/`NameLower`, LIKE metacharacters escaped via a shared `LikeEscape`); each hit carries its folder path, built in memory from one folder-skeleton read (no per-result CTE). UI: a topbar search box that drives `/files?q=` — the Files grid switches to a flat results mode (per-hit location, folder-click-navigates, marquee/DnD gated off), with `role="search"` + live result count. Trash excluded by the soft-delete filters. Unit + compile/template verified and exercised end-to-end against the dockerised stack (substring match, location paths, trash exclusion, `%`/`_` escaping, empty-term 400, topbar→results mode, Enter-opens-folder-and-clears-`q`, clean mobile layout) |
| 10 | In-browser preview (images, PDFs) | ✅ | Full-screen overlay with prev/next + keyboard (`features/files/preview-overlay.ts`). Server-side allowlist (`PreviewPolicy`) decides what may render; images/SVG via `<img>`, PDFs via `<iframe>` with a forced content type, plus video/audio. Lazy size-capped grid thumbnails |

## Account management (self-service)

Numbered 26–29 rather than slotted into a tier: features 6–25 are referenced by number in the
summary below and in other docs, so inserting mid-list would break those references. Password
reset is really a Tier 2 usability concern; the profile edits are Tier 3.

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 26 | Forgot / reset password | ✅ | **Built end-to-end (backend + UI + tests) and verified live — [feature-26-password-reset.md](feature-26-password-reset.md).** Two paths: **self-service email link** (gated on a mail provider being configured *and* the account being `EmailVerified`) and **admin manual reset** (direct-set or emailed link) — the fallback the *"Contact your admin"* login copy points at. Backend: `PasswordResetController` (forgot / preview / reset / capabilities) + `PasswordResetService`, a `PasswordResetTokens` table (twin of `AccountInvites`, 1-hour tokens) via the `AddPasswordReset` migration, and the app's **first rate limiter** (`RateLimiterPolicies`, per-IP). Completing a reset revokes all sessions then auto-signs-in ([feature-3-cookie-session.md](feature-3-cookie-session.md) Q-C3). The **email-verification blocker is resolved**: `User.EmailVerified` (Q-V6 / #36 §9 Q-P2), set only by proving inbox control, gates every email-based reset. UI: `/forgot-password` + `/reset-password/:token` screens and a "Forgot password?" link on login; admin gains a reset-password action. **Verified end-to-end:** Playwright **journey E** ([password-reset.spec.ts](../tests/e2e/tests/password-reset.spec.ts)) drives the whole self-service path against the Mailpit overlay — mint a verified account (invite→claim), request a reset from the login screen, read the reset email from Mailpit, set a new password and land signed in, then assert the old password is dead, the new one works, the used link `410`s, and an unknown address still returns the neutral `202` with no email. Runs in the `e2e` CI job on a fresh stack (default `Provider=None` → env-SMTP → Mailpit) and **confirmed green locally 2026-08-04**. The **admin manual-reset** path (§6) isn't in the journey yet but needs no email and is exercisable directly |
| 27 | Change email | 🟡 | **Backend + Angular UI built and verified live — [feature-27-change-email.md](feature-27-change-email.md).** Self-service change on the `/profile` screen, re-authenticated by the current password, branching on whether mail is configured: **mail on → verify-before-commit** (a confirmation link to the *new* address; the change lands and the address becomes `EmailVerified` only on click — the old address keeps signing in until then) and **mail off → immediate but `EmailVerified = false`** (no channel to prove the new inbox). Reuses #26's `EmailVerified` invariant so the change-email→reset takeover stays closed for free, and emails the old address on completion. Backend: `EmailChangeTokens` table (twin of `PasswordResetTokens`) via the `AddEmailChange` migration, `POST`/`DELETE /api/me/email` (re-auth + per-user rate limit), anon `confirm-email/{token}` preview/confirm, two Cove email templates, and `ProfileResponse` gaining `emailVerified`/`pendingEmail`. **Verified end-to-end against the dockerised stack (2026-08-04):** request→202 (email stays old, profile shows pending), Mailpit confirm link, side-effect-free preview, confirm→email swapped + `EmailVerified`, single-use token (410 on reuse), new email signs in / old rejected, old-address heads-up, plus the edge paths (wrong password, unchanged, in-use, cancel). **UI:** a Change-email card on `/profile` (verified/unverified badge, new-email + current-password fields, pending/resend/cancel state, a mail-off note) and a public `/confirm-email/:token` page (`EmailChangeService` in the client). **Verified live through the real UI (2026-08-07):** the mail-on flow end-to-end (request → pending line → Mailpit confirm link → confirm page → email swapped + Verified badge, pending cleared), plus responsive (mobile/tablet/desktop, no horizontal scroll — also fixed a pre-existing long-email header overflow) and a11y (roles, labels, text-not-colour badges). **Remaining:** the automated Playwright **journey F**, the mail-off live run (manual), and admin direct-set (deferred, Q-27-5) |
| 28 | Change password | ✅ | **Backend + UI done** as part of #36 ([feature-36-account-provisioning.md](feature-36-account-provisioning.md) §7.2): `POST /api/me/password` verifies the current password, re-runs `PasswordPolicy` + breach check, re-hashes with BCrypt, revokes the user's other sessions, and clears `MustChangePassword`; the change-password panel lives in the `/profile` screen. **Verified end-to-end against the dockerised stack (2026-07-31):** the forced first-login change (can't-skip guard redirects `/files` → `/profile?changePassword=1`), self-service change, other-sessions revocation (a second live session returned 401 after the change), and old-password rejection were all confirmed |
| 29 | Profile: first & last name | ✅ | **Backend + UI done** as part of #36 ([feature-36-account-provisioning.md](feature-36-account-provisioning.md) §7). `User` gained `FirstName`/`LastName` (migration `AddAccountProvisioning`) plus `GET`/`PATCH /api/me/profile`, surfaced in the `/profile` screen (feeds `cove-avatar` initials). **Verified end-to-end against the dockerised stack (2026-07-31):** edit + save, persistence across a full reload (fields rehydrate from the server), and the avatar initials updating (`Q` → `QT`) were all confirmed |
| 34 | Admin panel — account administration | ✅ | **Backend + Angular UI done.** Design: [feature-34-admin-console.md](feature-34-admin-console.md). Introduces the **role/authorization model** (`Role` enum on `User`, a `role` claim, an `"Admin"` policy) that was the hard prerequisite, plus an env-driven first-admin bootstrap (`AdminSeeder`). `AdminController` lists accounts, adjusts quota, and kicks (`DELETE` revokes sessions + marks for deletion; `AccountWipeService` then hard-deletes all files and the account). Audited to `AdminActionLogs`. Force sign-out as a standalone action is deferred (Q-A3); reset-password/disable are covered by the self-service items above. This is the account-focused slice of the broader **#21** admin console. **#36 extends this** with admin-created accounts + role assignment |
| 36 | Admin-provisioned accounts & email invites | 🟡 | [feature-36-account-provisioning.md](feature-36-account-provisioning.md); admin-managed email providers in [feature-36-email-providers.md](feature-36-email-providers.md) (**backend + Angular `/admin/email` screen implemented 2026-08-01** — runtime provider config Resend/Brevo/Mailgun via a per-send factory, API keys encrypted at rest via Data Protection with the key ring in Postgres). **The account-provisioning Angular UI** (admin create/role/resend dialogs, profile, claim page) **is implemented** (2026-07-30); the separate **email-settings `/admin/email` screen is now built** (backend + Angular, build-verified; live run pending). The **admin direct-provision → forced-first-login-change** path is **live-verified (2026-07-31)** (admin creates an account with a password, the account signs in and is forced through a can't-skip password change); the **email-invite / public-claim** path is now **verified end-to-end in CI** by Playwright **journey A** ([invite-claim.spec.ts](../tests/e2e/tests/invite-claim.spec.ts)) against a Mailpit SMTP sink (`docker-compose.e2e.yml`, wired into the `e2e` CI job) — admin sends an invite email, the invitee reads it from Mailpit, claims, sets a password, signs in, and the claim link then dies; the inviter is shown by name and their address never leaks. The one remaining unverified slice — the `/admin/email` **runtime-provider send** — **can't be automated against the local stack by design**: the stored providers are Resend/Brevo/Mailgun (HTTP APIs) and SMTP is deliberately excluded as a *stored* provider ([feature-36-email-providers.md](feature-36-email-providers.md) §2.2), so Mailpit (SMTP-only) can't stand in for it. e2e therefore exercises the **env-SMTP fallback** (the `Provider=None` path), not the admin-managed provider config. Closing this last slice needs a one-off **manual live run** against a real provider account + API key: in `/admin/email` pick a provider, paste its key (stored encrypted at rest), Save, then *Send test* and confirm delivery. The dev DB already carries a **real Mailgun sandbox** config from this work — it currently `403`s only because the sandbox domain refuses non-authorized recipients (a provider-side allowlist step, not a Keepr defect); authorizing a recipient (or moving to a verified domain) completes the run. Replaces public invite-code self-signup (#3) with **admin-provisioned accounts**: the admin creates accounts and assigns a role, either setting a password directly (usable immediately, no email needed) or sending an **email invite** the user claims to set their own password. Introduces a **provider-agnostic `IEmailSender`** (SMTP baseline via MailKit, no-op default → email is optional) — reusable infra that also unblocks #26/#27 and Q-A4. Ships a **Cove-styled HTML email template**. Folds in the **profile section** (#29) and the **change-password** core (#28, needed for forced first-login change). Invite-code self-registration is **disabled, not deleted** (`ClosedRegistrationGate` wired; invite gate kept dormant) |

## Localization

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 30 | Spanish language (i18n) | ❌ | No i18n framework installed (`@angular/localize`/`ngx-translate`) and no locale config in `angular.json`; all UI copy is hardcoded English. **Not a frontend-only job:** user-facing server strings — validation and gate messages (`EmailPolicy`, `PasswordPolicy`, `RegistrationGate`) — are English prose returned in problem+json `detail` and rendered verbatim by the client. Full Spanish means either the server localizes off `Accept-Language`, or the API returns stable error *codes* and the client owns the copy. The latter is the cleaner fork but reworks the current `detail`-rendering contract |

## Tier 3 — Expected by users who've used real Drive/Dropbox

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 11 | Version history + restore | ❌ | One row per file, overwritten in place |
| 12 | Storage quota tracking per user | ✅ | Full quota reserve/reconcile/release (`src/Api/Services/QuotaService.cs`), live meter via `src/Api/Features/Me/MeController.cs` |
| 13 | Activity log (who did what, when) | ❌ | No event/audit table |
| 14 | Starred/favorites | ❌ | No flag on `MediaFile` |
| 15 | "Recent" and "Shared with me" views | ❌ | List is sorted newest-first, but no dedicated views; sharing doesn't exist |
| 16 | Thumbnail generation | ❌ | Docs note post-processing is deferred |
| 37 | Media-type views (Images / Videos / Documents) in My Files | ❌ | Sub-navigation under **My Files** (the `files` entry in `app.ts`'s sidebar) for **Images**, **Videos**, and **Documents** — each a filtered flat view of the owner's whole tree by `MediaFile.ContentType` (`image/*`, `video/*`, and a document allowlist, reusing `PreviewPolicy`'s categories). Backend: a type-filtered listing (a `type=` param on the existing list, or a small dedicated endpoint). UI: the three sidebar children (+ `aria-current`) driving the flat results-grid mode #9 already introduced (per-hit location, marquee/DnD gated off). No new data — `ContentType` is already stored |
| 38 | PDF thumbnail previews | ❌ | A small rendered first-page preview for PDF files in the grid — today PDFs show a generic file icon; only images get a size-capped inline thumbnail (#10). Needs server-side first-page rasterization (a PDF render step via a library/sidecar), cached like any other derivative. A specialization of **#16** thumbnail generation, which currently reuses the original image and skips non-images |

## Tier 4 — Collaboration layer

| # | Feature | Status |
|---|---------|--------|
| 17 | Comments on files | ❌ |
| 18 | Notifications (shared/edited/commented) | ❌ |
| 19 | Real-time presence ("who's viewing this") | ❌ |

## Tier 5 — Scale/enterprise concerns

| # | Feature | Status |
|---|---------|--------|
| 20 | Sync client with offline support + conflict resolution | ❌ |
| 21 | Admin console / org-wide management | ❌ |
| 22 | Audit logs for compliance | ❌ |
| 23 | Virus/malware scanning on upload | ❌ (explicitly deferred in README) |
| 24 | Shared drives / team spaces (vs. personal-only) | ❌ |
| 25 | Retention/compliance policies | ❌ |

## Operations & observability

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 31 | Monitoring & analytics | ❌ | Only a `/health` liveness endpoint exists (`Program.cs`, wired to DO App Platform's `health_check`); logging is default console. No metrics, distributed tracing, or error tracking (OpenTelemetry / App Insights / Sentry), and no product-usage analytics on the client. **Two distinct halves:** *ops monitoring* (uptime, latency, error rates, alerts) and *product analytics* (which features get used) — different tools, can land independently. Distinct from #13 (in-app activity log) and #22 (compliance audit trail), which are user- and compliance-facing rather than operational |

## Legal pages

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 32 | Terms & Conditions page | ❌ | No public route exists — every route but `/login` is behind `authGuard`, and there is no footer or public layout to link from. The engineering is small (a public route + a content component); the real dependency is the **legal copy itself**, which has to be authored, not built |
| 33 | Privacy Notice page | ❌ | Same shape as #32, but more than a formality here: the app stores personal data (account email, uploaded files), and this is the disclosure side of that. Naturally paired with the register flow (a link or consent checkbox on signup) and with account data handling (#26–29, especially account/data deletion) |

## Accessibility & responsive UI

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 35 | Accessibility & mobile responsiveness | 🟡 | [feature-35-accessibility-mobile.md](feature-35-accessibility-mobile.md). **Foundation + mobile drawer done, per-screen sweep ongoing.** Fixed at the Cove component level so every screen benefits: outline-based `:focus-visible` ring (`styles.scss`), `prefers-reduced-motion` guard, skip link + landmarks, keyboard-operable sidebar nav (`<button>` + `aria-current`), accessible modal (`role="dialog"`, ESC, focus trap + restore), keyboard-operable file/folder cards (`role="button"`, Enter=open/Space=select, controls reveal on focus), accessible context menu (`role="menu"`, arrow-key nav, focus restore) + keyboard-aware positioning (`core/menu-anchor.ts`) that now **flips/clamps to stay in-viewport** near screen edges, and Enter-to-submit inputs. Mobile (<720px): hamburger **off-canvas drawer** (`role="dialog"`, focus trap, ESC) replacing the desktop rail. **Pending:** per-screen sweep (trash/admin/login/upload-toast/preview/share-viewer), `aria-haspopup`/`aria-expanded` on the ⋮ trigger, optional roving-`tabindex` grid nav, and the Tab-in-menu decision (Q-A1). Interactive changes are compile- + semantics-verified; the focus traps were not exercised live (need the backend) |

---

## Summary

- **Done (14):** upload/download, auth, quota tracking, file+folder metadata, folder hierarchy,
  rename/delete, trash, in-browser preview, shareable links, admin account administration (#34),
  change-password (#28), profile names (#29), search by file name (#9), forgot/reset password (#26).
- **Partial (3):** change email (#27) — **backend + Angular UI built and verified live** (the mail-on
  flow end-to-end through the real UI, plus responsive + a11y); the automated Playwright journey F and
  the mail-off live run remain ([feature-27-change-email.md](feature-27-change-email.md));
  accessibility & mobile (#35) — foundation + drawer in, per-screen sweep remains;
  admin-provisioned accounts & email invites (#36) — direct-provision + forced-change path
  live-verified and the email-invite/claim path now verified in CI via Playwright journey A against
  Mailpit; only the `/admin/email` runtime-provider **send** remains, and that can't be automated
  (Mailpit is SMTP-only; the stored providers are HTTP) — it needs a one-off manual live run against
  a real provider key.
- **Not started (21):** everything else. **Tier 1 is complete.**

### Next: Tier 2

The cheapest next wins, in order (**#9 search by file name shipped — verified live 2026-08-02**):

1. **#14 starred** — one boolean on `MediaFile` plus a sidebar view.
2. **#16 thumbnails** — grid thumbnails are currently capped at 500 KB and reuse the original
   image; generating real derivatives would lift that cap and cut the bytes ~200×.

**#6 sharing** is the big one, and per [my-decisions.md](my-decisions.md) Q5 it is the trigger
for revisiting malware scanning and content moderation — those become required before sharing
ships, not after.

**Account management (#26–29)** clusters around one prerequisite: **email verification** (Q-V6).
Reset-password (#26) **is now done and verified** ([feature-26-password-reset.md](feature-26-password-reset.md)):
it delivers the `EmailVerified` flag that unblocks the cluster, and its self-service flow is covered
end-to-end by Playwright journey E against the Mailpit overlay. Change-email (#27) now reuses that
same flag (its backend is built + verified). Change-password (#28) and
profile names (#29) are already done. Sequence: #26 delivered verification → **#27's backend + Angular
UI are now built and verified live** ([feature-27-change-email.md](feature-27-change-email.md)); the
automated **journey F** (mail-on, against Mailpit) **is next**.

### Known follow-ups

- **Sweeper leasing (Q-F).** `UploadCleanupService` and `TrashPurgeService` are both
  single-instance-safe only. Two instances would double-release quota — add
  `pg_try_advisory_lock` before scaling past one instance.
- ~~Browser uploads against the dockerized API don't work.~~ **Fixed 2026-07-22** by splitting
  `Storage:ServiceUrl` (what the API calls) from `Storage:PublicUrl` (the host baked into
  presigned URLs). The dockerised stack now uses `minio:9000` internally and `localhost:9000`
  for the browser.
- **Trashed-file downloads (Q-G).** Currently 404 via the global query filter — the recommended
  behaviour, but never explicitly decided.
- **Retention configurability (Q-E).** `Cleanup:TrashRetentionDays` defaults to 10.
