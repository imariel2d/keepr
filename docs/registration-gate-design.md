# Registration Gate — Design

> Status: **implemented**. Code: `src/Api/Features/Auth/RegistrationGate.cs`,
> wired in `src/Api/Program.cs`, used by `src/Api/Features/Auth/AuthController.cs`.
> Frontend: `src/ClientApp/src/app/features/login/`.
>
> Decided by Ariel, 2026-07-23: Keepr is a small private deployment, so **reaching the site must
> not imply being allowed to sign up**. Registration requires a shared invite code.
>
> This refines Q1 in [my-decisions.md](my-decisions.md) (own JWT auth, no external IdP). Q1
> settled *how* users authenticate; it never said *who may enrol*. This doc answers that.

---

## 1. The problem is enrolment, not authentication

The request that started this was "add a 2FA step so not everyone who has the URL can create an
account." Those are two different controls, and it is worth writing down why only one of them
solves the stated problem:

| Control | Protects against | Stops open signup? |
|---|---|---|
| **2FA (TOTP, email code)** | A stolen or guessed password on an **existing** account | **No** |
| **Registration gate** | Strangers **creating** accounts at all | **Yes** |

2FA is a login-time control. An uninvited visitor hitting `POST /api/auth/register` never reaches
a login — they create the account *and* enrol *their own* authenticator, and 2FA has changed
nothing about whether they got in. The gate is the control that matches the goal.

2FA remains worth adding later, for a different reason: it protects Ariel's account if the
password leaks. It is out of scope here and is **not** a prerequisite for this.

---

## 2. Why an interface for something this small

A single `if (code != expected) return 403;` in the controller would be four lines and would work.
It was rejected because the *policy* is the part expected to change, and the change is
foreseeable — a shared code is the weakest workable gate (see §7), so it will be replaced.

Everything that varies between gate policies is: what evidence the caller supplies, and the
verdict. Everything that does not vary is: the controller calls the gate first, and refuses with
the gate's reason. Putting the seam there means a future email-invite gate is a new class plus one
line in `Program.cs`, and `AuthController` is never reopened.

The seam is deliberately **narrow** — one method, no lifecycle, no gate-specific endpoints. A gate
that needs its own endpoints (issuing invites, listing them) adds its own controller; it does not
widen this interface.

---

## 3. Contract

```csharp
public readonly record struct RegistrationAttempt(string Email, string? Secret);

public readonly record struct GateDecision(bool Allowed, string? Reason, int StatusCode);

public interface IRegistrationGate
{
    Task<GateDecision> EvaluateAsync(RegistrationAttempt attempt, CancellationToken ct);
}
```

Three things worth noting about the shape:

- **`Secret`, not `InviteCode`.** The field is named for its role, not for today's implementation.
  An emailed invite token, a signed URL fragment, or an allow-list gate that ignores it entirely
  all arrive through the same field without a rename or a DTO change.
- **`Reason` is user-facing.** Gate reasons are written for the person filling in the form
  ("That invite code is not valid."), and the frontend renders them verbatim. Anything the user
  should not see belongs in a log, not in `Reason` — see §5.
- **`StatusCode` travels with the decision.** A future gate may want `429` (rate-limited) or `402`
  where this one wants `403`, without the controller learning about gate internals.

`Task`-returning even though the current implementation is synchronous: a database or mail lookup
is the obvious next implementation, and widening a sync interface later would touch every caller.

---

## 4. The invite-code implementation

`InviteCodeRegistrationGate` compares the supplied code against `Registration:InviteCode`.

### 4.1 It fails closed

An unset code means **registration is refused**, not that the check is skipped:

```csharp
if (string.IsNullOrWhiteSpace(expected))
{
    log.LogWarning("Registration refused: no Registration:InviteCode is configured...");
    return GateDecision.Deny("Registration is currently closed.");
}
```

The alternative — treating "no code configured" as "no gate" — means a deployment that forgets the
secret is silently open to the internet, which is the exact failure this feature exists to
prevent. Failing closed makes the misconfiguration loud and harmless instead of quiet and
dangerous. The warning log names the setting so the cause is obvious from the logs alone.

### 4.2 Constant-time comparison, on digests

```csharp
CryptographicOperations.FixedTimeEquals(
    SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
    SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
```

`FixedTimeEquals` requires equal-length inputs and returns early otherwise, which would leak the
code's length. Hashing both sides first makes them fixed-width, so only equality is observable.

This is close to over-engineering at this scale — remote timing on a string compare across the
public internet is not a realistic attack here — but it is three lines and removes the need to
think about it again.

---

## 5. Ordering: the gate runs before the database

```csharp
var decision = await registrationGate.EvaluateAsync(new RegistrationAttempt(email, req.InviteCode), ct);
if (!decision.Allowed) return Problem(decision.Reason, statusCode: decision.StatusCode);

if (await db.Users.AnyAsync(u => u.Email == email, ct))
    return Problem("Email already registered.", statusCode: 409);
```

The order is load-bearing. Checking the email first turns `/register` into an account-existence
oracle: an uninvited caller submits addresses and reads `409` vs `403` to learn who has an
account, without ever needing a valid code. Gating first means an uninvited caller learns exactly
one thing — that they were not invited.

The cost is that a **valid** invite holder can still enumerate addresses via `409`. That is
accepted: they were invited, and hiding it would mean a deliberately vague "check your email"
flow that this project does not need.

---

## 6. API and error surface

`POST /api/auth/register` gains one optional-in-transport, required-in-practice field:

```jsonc
{ "email": "...", "password": "...", "inviteCode": "..." }
```

Responses are problem+json, matching the rest of the API (`detail` carries the user-facing text):

| Case | Status | `detail` |
|---|---|---|
| No code supplied | `403` | An invite code is required to create an account. |
| Wrong code | `403` | That invite code is not valid. |
| No code configured | `403` | Registration is currently closed. |
| Code fine, email taken | `409` | Email already registered. |
| Success | `200` | `{ accessToken }` |

`POST /api/auth/login` was changed only in shape — `Unauthorized(new { error })` became
`Problem(..., 401)` — so every auth failure is problem+json and the client has one parser.

### Frontend

The invite field renders **only in register mode**, with the hint "Keepr is invite-only. Ask the
owner for a code." `login.ts` now surfaces `error.detail` instead of a fixed string, so the user
is told which field is wrong rather than being left to guess.

---

## 7. What this does and does not buy

Honest limits of a single shared code:

- **It is shared.** Everyone who registers uses the same string, so a leak (a screenshot, a
  forwarded message) opens signups to anyone holding it until it is rotated.
- **No attribution.** Nothing records *which* invite produced an account, because there is only
  one invite.
- **No revocation short of rotation.** Rotating locks out everyone who has not yet signed up.
- **No rate limiting.** Nothing stops a caller guessing codes as fast as they can POST. A short
  random code is guessable given enough attempts; use a long one (see Q-R2).

What it does buy, which is the whole point: a stranger who finds the URL cannot create an account.
For a single-user private deployment that is the entire threat model, and the ceremony of
per-address invites would be cost without benefit.

---

## 8. Configuration

| Environment | Where | Value |
|---|---|---|
| Local (host) | `appsettings.Development.json` | `keepr-dev` |
| Local (docker) | `docker-compose.api.yml` → `Registration__InviteCode` | `keepr-dev` |
| Production | `.do/app.yaml` → `Registration__InviteCode`, `type: SECRET` | set in the DO dashboard |

`appsettings.json` ships the key **empty**, so a deployment that never sets the secret fails
closed (§4.1). Rotation is a config change and a restart; no migration, no code deploy.

---

## 9. Swapping the implementation later

The intended next step, if the shared code stops being enough:

1. Add an `Invites` table — `Code` (hashed), `Email` (nullable, to pin an invite to one address),
   `ExpiresAt`, `UsedAt`, `UsedByUserId`.
2. Add `InviteTokenRegistrationGate : IRegistrationGate` — look the token up, check unused and
   unexpired, and (if pinned) that `attempt.Email` matches.
3. Change one line in `Program.cs`.
4. Add an admin-only controller for issuing and listing invites. **It does not touch
   `IRegistrationGate`** — issuing is a separate concern from evaluating.

`AuthController` and the frontend field are unchanged: the field is already "a secret the server
will judge", and the reason string already comes from the gate.

One thing that *would* need a small change: marking an invite used. `EvaluateAsync` is a query,
not a command, and consuming a single-use token has to happen inside the same transaction that
creates the user. Options: return a token id on `GateDecision` for the controller to consume, or
give the gate an `OnRegisteredAsync` hook. Prefer the hook — it keeps the controller ignorant of
gate internals. Left undesigned until it is needed.

---

## 10. Verification

Exercised against the local dockerised stack, 2026-07-23:

| Check | Result |
|---|---|
| No code | `403` "An invite code is required to create an account." |
| Wrong code | `403` "That invite code is not valid." |
| Correct code + existing email | `409` "Email already registered." — proves the gate **passed** |
| Login with bad credentials | `401` problem+json |
| UI: invite field in register mode only | Confirmed present/absent |
| UI: wrong code submitted | Server `detail` rendered, no token, no navigation |
| Rows created by the above | **0** |

The third row is how the allow-path was proven without creating an account: a valid code against
an address that already exists gets past the gate and lands on the duplicate check. Blocked
attempts write nothing, confirming the gate runs ahead of persistence (§5).

---

## 11. Open questions

### ⏳ Q-R1 — Rate limiting on `/register` and `/login`
Nothing throttles guesses at the invite code or at passwords. ASP.NET Core's built-in rate
limiter, keyed by IP, would cover both. Not done: single-user deployment, and App Platform sits
behind a proxy so the client IP needs `X-Forwarded-For` handling to be meaningful.

### ⏳ Q-R2 — Code strength and rotation policy
No minimum length or entropy is enforced, and `keepr-dev` is deliberately weak for local use.
Production should use a long random string. Consider refusing to start when a non-Development
environment has a short code — the same fail-loud instinct as §4.1.

### ⏳ Q-R3 — Should the first account bootstrap differently?
Today the first account is created with the same invite code as any other. An alternative is a
one-shot bootstrap that closes after the first user exists. Unnecessary while the gate fails
closed by default.

### ⏳ Q-R4 — 2FA, on its own merits
Deferred, not rejected. It protects an existing account against a leaked password, which the gate
does not. Revisit alongside **#6 sharing** in [feature-status.md](feature-status.md): more
accounts and shared data raise the value of a second factor.
