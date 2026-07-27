# Admin Console — Roles & Account Administration — Design

> Feature #34 in [feature-status.md](feature-status.md), the account-focused slice of the broader
> #21 admin console. Status: **backend implemented** (`src/Api/Features/Admin/AdminController.cs`,
> `Services/AdminAuditService.cs`, `Services/AccountWipeService.cs`, `Features/Auth/AdminSeeder.cs`;
> migrations `AddUserRole`, `AddAdminActionLog`, `AddUserDeletionRequestedAt`). **Angular UI (§6)
> pending.**
>
> Decided by Ariel, 2026-07-24. This doc introduces the **role/authorization model** that #34
> names as its hard prerequisite ("every account is an equal owner today"), and specifies the
> first two admin capabilities: adjust anyone's storage, and remove an account (with a full file
> wipe).
>
> Relates to Q-R3 in [registration-gate-design.md](registration-gate-design.md) (first-admin
> bootstrap) and reuses the purge path from [trash-soft-delete-design.md](trash-soft-delete-design.md).

---

## 1. Scope, and what "project" means here

The request used the word "project" ("look at the project settings", "kick someone out of the
project"). Keepr has **no project entity** — it is single-tenant: each `User` owns their own files
against a per-user quota ([User.cs](../src/Api/Domain/User.cs)). So "project" is read as **the
Keepr instance as a whole**, and an admin manages *accounts*, not membership in a project.

Multi-tenant projects (shared folders, per-project membership and roles) are explicitly **out of
scope** and are a much larger build; nothing here forecloses it.

In scope for this pass:

1. A **role model** — `Admin` and `User`.
2. **First-admin bootstrap** from environment variables.
3. Admin **lists accounts** and **adjusts anyone's quota**.
4. Admin **removes an account** ("kick") — **access is revoked immediately**, and the user's files
   and the account itself are then **hard-deleted asynchronously** after the kick is queued
   (irreversibly, bypassing the 10-day trash grace — no recovery window).
5. A **basic audit trail** of admin actions.

---

## 2. Roles

Two roles for now: `User` (every existing account) and `Admin`.

Stored as an **enum**, not a boolean `IsAdmin`:

```csharp
public enum Role { User = 0, Admin = 1 }
```

Same storage cost as a bool, but it extends without a migration-of-meaning if a third tier ever
lands. The only future role worth naming: a **read-only auditor/support** role (view accounts and
usage, cannot change quota or remove anyone) — useful once support staff exist, deliberately *not*
built now.

**Why not a separate `Roles` table / claims-per-user?** Keepr is a small private deployment with
two flat roles and no per-resource permissions. A join table and a permission system would be
architecture the app does not use. A single column matches the actual requirement; it can grow
into a table later the same way any enum can.

### 2.1 Migration

One column on `Users`:

| Column | Type | Default | Meaning |
|---|---|---|---|
| `Role` | `varchar(16)`, not null | `'User'` | Every existing row backfills to `User` — no account is silently promoted. |

Stored as a **string** (`HasConversion<string>()`), not an int, matching how `MediaFile.Status`
is already persisted — the column reads `'User'`/`'Admin'` instead of an opaque number.

### 2.2 Carrying the role through auth

The session handler currently emits `sub` + `email`
([SessionAuthenticationHandler.cs](../src/Api/Features/Auth/SessionAuthenticationHandler.cs)). Add
a third claim from `user.Role`:

```csharp
new Claim(KeeprClaims.Role, user.Role.ToString())   // KeeprClaims.Role = "role"
```

Register an authorization policy in `Program.cs`:

```csharp
options.AddPolicy("Admin", p => p.RequireClaim(KeeprClaims.Role, nameof(Role.Admin)));
```

Admin endpoints are then `[Authorize(Policy = "Admin")]`. A `User`-role caller gets **403**
(authenticated but not permitted), an anonymous caller **401** — the standard split, no custom
handling. Because the role rides in the session ticket, changing a user's role only takes effect on
their **next** session validation; for an immediate effect (e.g. demotion), revoke their sessions.

---

## 3. First-admin bootstrap

There is no admin until one is seeded, and registration is invite-gated, so we cannot rely on
"just sign up and flip a flag". Mirror the existing env-driven `Registration__InviteCode` pattern:

```
Admin__Email=you@example.com
Admin__Password=<long random initial password>
```

On startup, a hosted `AdminSeeder` runs once:

- **No account with that email** → create it, `Role = Admin`, password BCrypt-hashed via the same
  path as register, default quota. Log that a bootstrap admin was created.
- **Account exists but is not Admin** → **promote** it to `Admin` (idempotent, does *not* touch the
  password). This lets you grant admin to an already-registered account by setting the env var.
- **Account exists and is Admin** → no-op.
- **Env unset / blank** → do nothing (fail-safe: no accidental admin). If *no* admin exists at all,
  log a warning, same spirit as the registration gate's "no code configured" warning.

**Password hygiene:** the env password is an *initial* secret. After the first successful admin
sign-in the UI surfaces a persistent "change your password" nudge until it's changed (a
`MustChangePassword`-style flag set at seed time, cleared on first password change). This depends on
change-password (#28); if #28 isn't in yet, the nudge is copy-only ("update the password you set in
`Admin__Password`") and the flag lands with #28.

> **Q-A1 — plaintext password in env.** The initial admin password sits in an env var / App
> Platform secret. Accepted for a small private deployment and time-boxed by the change-password
> nudge. Rotation = change the env var *only* matters until first sign-in; after that the DB hash is
> the source of truth and the env password is dead. Documented so no one expects editing
> `Admin__Password` later to reset a forgotten password (it won't — that's reset-password, #26).

---

## 4. Admin capabilities

New `AdminController` at `api/admin`, entirely `[Authorize(Policy = "Admin")]`.

| Method & route | Does | Notes |
|---|---|---|
| `GET /api/admin/users` | List accounts: id, email, role, `QuotaBytes`, `UsedBytes`, `CreatedAt`, active session count | Paginated; the instance is small but the shape shouldn't assume that. |
| `GET /api/admin/users/{id}` | One account's detail (same fields + `TrashedBytes`) | Powers the account drawer. |
| `PATCH /api/admin/users/{id}/quota` | Set `QuotaBytes` to a new value | See §4.1. |
| `DELETE /api/admin/users/{id}` | **Kick**: revoke sessions, wipe all files, delete the account | See §4.2. |

### 4.1 Adjust quota

`PATCH /api/admin/users/{id}/quota` with `{ "quotaBytes": <long> }`.

- Validate `quotaBytes >= 0`. Setting a quota **below** the user's current `UsedBytes` is
  **allowed** — it puts them over quota, which the upload path already handles (they simply can't
  upload more until they free space; existing files are untouched). This is a legitimate admin
  action (tightening an over-provisioned account), so it is not rejected, but the response echoes
  the resulting `RemainingBytes` (which will be `0`) so the UI can warn.
- Writes one audit entry (§5).

### 4.2 Kick — remove access and wipe files

Decided semantics (Ariel, 2026-07-24): **full account deletion** and an **irreversible hard delete**
of all files, **bypassing the normal 10-day trash grace** (no recovery window). "Bypassing the
grace" is about *reversibility*, not *timing*: **access is revoked immediately**, but the hard
delete itself is **asynchronous** — the kick only queues it, and a background sweep carries it out.

A user may own thousands of objects, so this is **not** done entirely in the request. Two phases:

**Phase 1 — synchronous, in the request (fast, transactional where it can be):**
1. Guardrails (§4.3). Reject self-kick and last-admin removal *before* anything is touched.
2. **Revoke every session** for the user (`Sessions` table) — access is gone the instant this
   returns, before a single byte is deleted. This is the "remove their access" the request asked
   for, and it's nearly free (cookie-session-design Q-C3).
3. Mark the account **`PendingDeletion`** (a nullable `DeletionRequestedAt` timestamp + the account
   is treated as disabled: login refused, all endpoints 401/403). The email is *not* freed yet —
   it's still an occupied row until phase 2 finishes.
4. Write the audit entry (§5) now, while we still have the actor and target context.
5. Return **202 Accepted** — the wipe is in progress.

**Phase 2 — background sweep (the only step that touches R2):**
A hosted sweeper (sibling to `TrashPurgeService`) picks up `PendingDeletion` accounts and, in
batches, **hard-deletes all their files regardless of trash state** — live and already-trashed
alike — via the existing `TrashService.PurgeFilesAsync` (R2 objects + rows + quota release), then
purges their folder rows, then **deletes the `User` row**. Deleting the row frees the email to be
registered again (the chosen "delete account fully" semantics).

**Why background, and why reuse purge:**
- Purge is already "the only place bytes leave R2", already batched, already retries a failed item
  on the next tick instead of failing in someone's request
  ([TrashPurgeService.cs](../src/Api/Services/TrashPurgeService.cs)). A kick is "purge *everything*
  this user owns, now, ignoring the retention clock" — the same operation with a different
  selection predicate.
- Doing it inline would risk an HTTP timeout mid-delete and leave a half-wiped account with a
  confusing partial state. Phase 1 already achieved the security goal (no more access); phase 2 is
  cleanup that is safe to be eventually-consistent.

> Same single-instance caveat as the other sweepers: two instances wiping at once would
> double-release quota. Add an advisory lock before scaling out. Already true of
> `TrashPurgeService` / `UploadCleanupService`; not a new constraint.

> **Q-A2 — audit survives account deletion.** The `User` row is deleted, but the audit entry for
> the kick must not be. The audit table therefore stores the target's **email and id as plain
> columns**, not an FK to `Users` — see §5.

### 4.3 Guardrails

Enforced server-side, not just hidden in the UI:

- **No self-kick, no self-demote.** An admin cannot delete or demote their own account. Prevents
  accidental lockout and the "delete the last admin" footgun in one move.
- **Cannot remove the last admin.** `DELETE`/demote is refused with 409 if it would leave zero
  admins. The count is checked against non-pending admins other than the target. Note: for the
  **kick path alone** this is defensive — removing the last admin necessarily means removing
  *yourself*, which the self-kick guard already blocks, so the 409 is currently unreachable via
  `DELETE`. It becomes load-bearing once a **demote** endpoint exists (an admin demoting the only
  other admin, or themselves). Kept now so that invariant lives with the guardrails, not the
  future endpoint.
- Quota edits and kicks target `User`-role accounts freely; an admin editing *another* admin's
  quota is allowed (no ranking among admins), but deleting another admin still trips the
  last-admin check.

---

## 5. Audit trail

Basic, but present from day one because the destructive action (kick) is irreversible.

New table `AdminActionLogs`:

| Column | Type | Meaning |
|---|---|---|
| `Id` | uuid | PK |
| `ActorUserId` | uuid | The admin who acted. |
| `ActorEmail` | text | Denormalized snapshot — readable even if the actor is later removed. |
| `Action` | text | `QuotaChanged`, `UserKicked` (extensible). |
| `TargetUserId` | uuid | The affected account. **No FK** — the target may be deleted (§4.2). |
| `TargetEmail` | text | Denormalized snapshot — the whole point of an audit entry outliving its target. |
| `Details` | jsonb | Action-specific: e.g. `{ "from": 5368709120, "to": 10737418240 }` for a quota change. |
| `CreatedAt` | timestamptz | When. |

Deliberately **no FKs to `Users`** on the target (and arguably not on the actor either): audit rows
must outlive the accounts they describe. Denormalized emails make the log readable on its own.

Write one row inside the same transaction as the state change it records, so an action and its audit
entry are all-or-nothing. Exposed later via `GET /api/admin/audit` (a small addition; the table is
the load-bearing part now).

This is the seed of #13/#22 (broader audit logging), scoped here to admin actions only.

---

## 6. Frontend (sketch)

Admin-only `/admin` route, guarded by the role claim from `GET /api/auth/session` (extend
`SessionResponse` to include `role` so the SPA can gate the nav item and route). Non-admins never
see the entry point, and the API refuses them regardless.

- **Accounts table** — email, role, a used/quota meter, session count, actions.
- **Quota editor** — inline edit or a small dialog; shows the resulting remaining space, warns when
  set below current usage.
- **Kick** — a destructive confirm dialog that spells out "this permanently deletes the account and
  **all** their files — this cannot be undone", requiring the admin to type the target email to
  confirm. Matches the irreversible-action guardrail pattern used elsewhere for delete.

`SessionResponse` gaining `role` is the one auth-contract change the SPA depends on; everything else
is new endpoints.

---

## 7. Build order

1. `Role` enum + `Users.Role` migration (backfill `User`); role claim in the session handler;
   `"Admin"` policy in `Program.cs`. **← unblocks everything, changes no behavior.**
2. `AdminSeeder` + `Admin__Email`/`Admin__Password` env (+ `.env.example`, `.do/app.yaml`).
3. `AdminActionLogs` table + write path.
4. `AdminController`: list, get, quota PATCH (with audit). Guardrails.
5. Kick: phase-1 endpoint (revoke + mark `PendingDeletion` + audit) and the phase-2 sweeper
   (reusing `TrashService.PurgeFilesAsync`).
6. `SessionResponse.role`; Angular `/admin` UI.
7. Change-password nudge (folds into #28).

Steps 1–5 are backend and independently testable; the existing `tests/Api.Tests` project is the
home for role-gate and guardrail (self-kick, last-admin) tests.

---

## 8. Open questions

| # | Question | Current decision |
|---|---|---|
| Q-A1 | Plaintext initial admin password in env | Accepted, time-boxed by the change-password nudge (§3). |
| Q-A2 | Audit entries outliving deleted targets | Denormalized email/id, no FK (§4.2, §5). |
| Q-A3 | Force sign-out as a *standalone* admin action (revoke without deleting) | Deferred. The kick already revokes; a standalone "sign this user out everywhere" is a trivial add on the same `Sessions` machinery (#34 notes it's "nearly free") but isn't required by this request. |
| Q-A4 | Notifying a kicked user (email "your account was removed") | Deferred — depends on outbound email, which the app doesn't have yet (blocks #26 too). |
