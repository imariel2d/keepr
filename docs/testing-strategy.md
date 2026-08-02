# Testing strategy & CI

**Status:** 🟡 in progress — Phases 1 (CI + unit expansion) and 2 (Playwright scaffold + journey A)
landing on branch `test/ci-unit-e2e`; Phase 3 (journeys B→D) still to come.
Not a single-feature doc, so it takes a plain name (see the **docs-naming** skill).

How Keepr is tested, and what runs in CI on every push to `main` and every pull request.

## Two layers, on purpose

| Layer | Tool | Runs where | Covers |
|---|---|---|---|
| **Unit** | xUnit (`tests/Api.Tests`) | `dotnet test`, **no Docker** | Pure logic: policies, validators, domain liveness rules, rendering, math, token primitives |
| **E2E** | Playwright (`tests/e2e`) | the full `docker compose` stack | Real user journeys through Angular → API → Postgres → MinIO → (Mailpit) |

There is **no separate .NET integration-test layer** (no `WebApplicationFactory`, no Testcontainers).
That is a deliberate choice: the HTTP + EF + MinIO paths are exercised **end-to-end through Playwright**
against the real running stack, which is the same fidelity an integration test would give, without a
second harness to maintain. This matches the philosophy the existing unit tests already state in
their doc-comments ("the persistence paths are covered end-to-end").

### The consequence: some rules must be extracted to be unit-testable

Several important rules live *inside controllers*, tangled with `DbContext` calls, so a unit test
can't reach them without a database. Rather than stand up integration tests, we **extract each rule
into a small pure function** and unit-test that; the controller keeps the surrounding I/O and calls
the pure function. Phase 1 does this for:

- `User.DisplayName(first, last)` — the inviter-name logic pulled out of `AdminController.ActorDisplayNameAsync`.
- `AdminInvariants.IsSelfDemotion` / `WouldRemoveLastAdmin` — the role-change guards pulled out of `AdminController.UpdateRole`.

The domain liveness predicates (`AccountInvite.IsClaimable`, `Session.IsActive`, `ShareLink.IsLive`)
were already pure and are already well covered — they need no change.

## Unit tests (`tests/Api.Tests`)

Each is a pure function of its inputs — no clock reads, no I/O. Scenario → expected output:

| Unit | Scenarios → expected |
|---|---|
| `User.DisplayName` | `("Jane","Doe")→"Jane Doe"`; `("Jane",null)→"Jane"`; `(null,"Doe")→"Doe"`; `(null,null)→null`; whitespace-only → `null`; trims surrounding space |
| `AdminInvariants.IsSelfDemotion` | self + non-admin target → `true`; self + admin → `false`; other user → `false` |
| `AdminInvariants.WouldRemoveLastAdmin` | Admin→User with 0 other admins → `true`; with ≥1 → `false`; User→anything → `false` |
| `SecureToken` | `Generate()` unique + URL-safe + decodes to 32 bytes; `Hash` deterministic, differs per token, never equals the raw token |

Already covered and kept as regression guards: `EmailTemplates` (invite name line + HTML-encoding),
`EmailPolicy`, `PasswordPolicy`, `PreviewPolicy`, `CredentialValidator`, the four email senders,
`AccountInvite`/`Session`/`ShareLink` liveness, `NameAllocator`, `LikeEscape`, `PresignHeaders`.

## E2E tests (`tests/e2e`, Playwright)

Run against the full stack plus a **Mailpit** container (SMTP sink) so the invite email can be read
without a real inbox. A third compose overlay, `docker-compose.e2e.yml`, adds Mailpit and points the
API's env-SMTP fallback at it (`Email__Provider=smtp`, `Email__Smtp__Host=mailpit`, port 1025 as an
unencrypted dev relay) and bakes `Email__PublicBaseUrl=http://localhost:4200` so the claim link is
browser-openable. Playwright reads messages via Mailpit's HTTP API (`http://localhost:8025`). Base
URL `http://localhost:4200`; the seeded dev admin (`admin@keepr.local` / `keepr-dev-admin`) is the
authenticated entry point.

```bash
docker compose -f docker-compose.yml -f docker-compose.api.yml -f docker-compose.e2e.yml up -d --build
cd tests/e2e && npm install && npx playwright install --with-deps chromium && npx playwright test
```

The seeded admin carries **must-change-password** and has **no name**, so the suite bootstraps it on
first sign-in: it completes the forced password change and sets a first/last name (`Ada Lovelace`) so
the invite's inviter line is exercised. That bootstrap assumes a **fresh stack** (the CI contract);
for a deterministic local rerun, reset first with `docker compose … down -v`.

Each journey is specified with its **full expected output**, per the **feature-e2e-design** skill.

### A. Invite → claim → sign-in
1. Sign in as an admin **that has a first/last name** (so the name line is exercised).
2. Accounts → Create user `newuser@example.com`, *Send invite* → **201**, success notice, row shows **Pending**.
3. Mailpit → **exactly one** message to `newuser@example.com`, subject "You're invited to Keepr", body
   contains **"<Name> has invited you to Keepr"** and **never** an `@`-address as the inviter.
4. Open the claim link → set a valid password → redirect to `/files`, authenticated.
5. Sign out, sign back in with the new password → success. Re-open the old claim link → **dead** (already claimed).

### B. Auth / session
Valid login → `/files`; wrong password → inline error, still on `/login`; logout → protected route
redirects to login; non-admin hitting `/admin` → blocked.

### C. Files / folders / trash / sharing
Upload (real MinIO PUT) → appears with correct size; create folder + move; download → bytes match;
delete → appears in Trash, restore works, purge removes; create share link → open in a fresh
unauthenticated context → file viewable; revoke → link shows expired.

### D. Admin console
Change quota → reflected in the row; change role → reflected; demote the sole admin → blocked with the
guard message; kick a user → their sessions die; audit entries appear.

## CI (`.github/workflows/ci.yml`)

Triggers: `push` to `main` and every `pull_request`. Concurrency cancels superseded PR runs.
Coverage is reported (cobertura artifact) but **never gates** — only a failing test fails the build.

- **`unit`** — `setup-dotnet` (net10) → `dotnet test tests/Api.Tests` → upload coverage artifact. *(Phase 1)*
- **`e2e`** — bring the compose stack + Mailpit up → wait for the API/SPA/Mailpit to answer →
  `setup-node@22` → `playwright install --with-deps chromium` → `playwright test` → on failure
  upload the HTML report and dump service logs → `compose down -v`. *(Phase 2 — landed; runs
  journey A. Phase 3 adds journeys B→D.)*

## Phasing

1. **Phase 1** — CI `unit` job, the extract-refactors, and the unit tests above. *(done)*
2. **Phase 2** — Playwright scaffold (`tests/e2e`) + Mailpit overlay + journey **A**, wired into the
   CI `e2e` job. *(done — this branch)*
3. **Phase 3** — journeys **B → C → D**.
