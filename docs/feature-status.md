# Feature Status

Tracking the planned feature set against what is actually implemented in the codebase.

Keepr is a **personal media store** with a folder hierarchy, rename, and a 10-day trash:
single-owner (no user-to-user sharing yet). Of the 36 planned features, **13 are complete**
(backend + UI), 3 are partial (built, verification or per-screen work remaining), and 20 are not started.

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
| 26 | Forgot / reset password | 🟡 | **Built end-to-end (backend + UI + tests) — [feature-26-password-reset.md](feature-26-password-reset.md); live e2e pending.** Two paths: **self-service email link** (gated on a mail provider being configured *and* the account being `EmailVerified`) and **admin manual reset** (direct-set or emailed link) — the fallback the *"Contact your admin"* login copy points at. Backend: `PasswordResetController` (forgot / preview / reset / capabilities) + `PasswordResetService`, a `PasswordResetTokens` table (twin of `AccountInvites`, 1-hour tokens) via the `AddPasswordReset` migration, and the app's **first rate limiter** (`RateLimiterPolicies`, per-IP). Completing a reset revokes all sessions then auto-signs-in ([feature-3-cookie-session.md](feature-3-cookie-session.md) Q-C3). The **email-verification blocker is resolved**: `User.EmailVerified` (Q-V6 / #36 §9 Q-P2), set only by proving inbox control, gates every email-based reset. UI: `/forgot-password` + `/reset-password/:token` screens and a "Forgot password?" link on login; admin gains a reset-password action. **Verification pending** like #36's email path — the self-service link needs a configured mail provider the local stack doesn't set; the admin direct-set path is exercisable without one |
| 27 | Change email | ❌ | `Email` is already unique + normalized (`AppDbContext`). A change must re-run `EmailPolicy` and, once #26's verification exists, re-verify the new address before it takes effect |
| 28 | Change password | ✅ | **Backend + UI done** as part of #36 ([feature-36-account-provisioning.md](feature-36-account-provisioning.md) §7.2): `POST /api/me/password` verifies the current password, re-runs `PasswordPolicy` + breach check, re-hashes with BCrypt, revokes the user's other sessions, and clears `MustChangePassword`; the change-password panel lives in the `/profile` screen. **Verified end-to-end against the dockerised stack (2026-07-31):** the forced first-login change (can't-skip guard redirects `/files` → `/profile?changePassword=1`), self-service change, other-sessions revocation (a second live session returned 401 after the change), and old-password rejection were all confirmed |
| 29 | Profile: first & last name | ✅ | **Backend + UI done** as part of #36 ([feature-36-account-provisioning.md](feature-36-account-provisioning.md) §7). `User` gained `FirstName`/`LastName` (migration `AddAccountProvisioning`) plus `GET`/`PATCH /api/me/profile`, surfaced in the `/profile` screen (feeds `cove-avatar` initials). **Verified end-to-end against the dockerised stack (2026-07-31):** edit + save, persistence across a full reload (fields rehydrate from the server), and the avatar initials updating (`Q` → `QT`) were all confirmed |
| 34 | Admin panel — account administration | ✅ | **Backend + Angular UI done.** Design: [feature-34-admin-console.md](feature-34-admin-console.md). Introduces the **role/authorization model** (`Role` enum on `User`, a `role` claim, an `"Admin"` policy) that was the hard prerequisite, plus an env-driven first-admin bootstrap (`AdminSeeder`). `AdminController` lists accounts, adjusts quota, and kicks (`DELETE` revokes sessions + marks for deletion; `AccountWipeService` then hard-deletes all files and the account). Audited to `AdminActionLogs`. Force sign-out as a standalone action is deferred (Q-A3); reset-password/disable are covered by the self-service items above. This is the account-focused slice of the broader **#21** admin console. **#36 extends this** with admin-created accounts + role assignment |
| 36 | Admin-provisioned accounts & email invites | 🟡 | [feature-36-account-provisioning.md](feature-36-account-provisioning.md); admin-managed email providers in [feature-36-email-providers.md](feature-36-email-providers.md) (**backend + Angular `/admin/email` screen implemented 2026-08-01** — runtime provider config Resend/Brevo/Mailgun via a per-send factory, API keys encrypted at rest via Data Protection with the key ring in Postgres). **The account-provisioning Angular UI** (admin create/role/resend dialogs, profile, claim page) **is implemented** (2026-07-30); the separate **email-settings `/admin/email` screen is now built** (backend + Angular, build-verified; live run pending). The **admin direct-provision → forced-first-login-change** path is **live-verified (2026-07-31)** (admin creates an account with a password, the account signs in and is forced through a can't-skip password change); the **email-invite / public-claim** path is still verification-pending — it needs a configured mail provider, which the local stack doesn't set (`CreateUser` correctly returns the `email_not_configured` 409 without one). Replaces public invite-code self-signup (#3) with **admin-provisioned accounts**: the admin creates accounts and assigns a role, either setting a password directly (usable immediately, no email needed) or sending an **email invite** the user claims to set their own password. Introduces a **provider-agnostic `IEmailSender`** (SMTP baseline via MailKit, no-op default → email is optional) — reusable infra that also unblocks #26/#27 and Q-A4. Ships a **Cove-styled HTML email template**. Folds in the **profile section** (#29) and the **change-password** core (#28, needed for forced first-login change). Invite-code self-registration is **disabled, not deleted** (`ClosedRegistrationGate` wired; invite gate kept dormant) |

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

- **Done (13):** upload/download, auth, quota tracking, file+folder metadata, folder hierarchy,
  rename/delete, trash, in-browser preview, shareable links, admin account administration (#34),
  change-password (#28), profile names (#29), search by file name (#9).
- **Partial (3):** forgot/reset password (#26) — built end-to-end (backend + UI + tests), live e2e
  pending a mail provider (admin direct-set path exercisable without one); accessibility & mobile
  (#35) — foundation + drawer in, per-screen sweep remains; admin-provisioned accounts & email
  invites (#36) — direct-provision + forced-change path live-verified, email-invite/claim path
  pending a mail provider.
- **Not started (20):** everything else. **Tier 1 is complete.**

### Next: Tier 2

The cheapest next wins, in order (**#9 search by file name shipped — verified live 2026-08-02**):

1. **#14 starred** — one boolean on `MediaFile` plus a sidebar view.
2. **#16 thumbnails** — grid thumbnails are currently capped at 500 KB and reuse the original
   image; generating real derivatives would lift that cap and cut the bytes ~200×.

**#6 sharing** is the big one, and per [my-decisions.md](my-decisions.md) Q5 it is the trigger
for revisiting malware scanning and content moderation — those become required before sharing
ships, not after.

**Account management (#26–29)** clusters around one prerequisite: **email verification** (Q-V6).
Reset-password (#26) **is now built** ([feature-26-password-reset.md](feature-26-password-reset.md)):
it delivers the `EmailVerified` flag that unblocks the cluster (live e2e still pending a mail
provider), and change-email (#27) inherits that same flag when built. Change-password (#28) and
profile names (#29) are already done. Sequence: #26 delivered verification → **#27 is next** and
reuses it.

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
