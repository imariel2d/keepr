# Registration Input Validation — Design

> Status: **implemented**. Code: `src/Api/Features/Auth/EmailPolicy.cs`,
> `PasswordPolicy.cs`, `PwnedPasswordsClient.cs`, wired in `AuthController.Register`.
> Frontend: `src/ClientApp/src/app/features/login/`.
>
> Decided by Ariel, 2026-07-23: account creation currently accepts any string as an email and any
> string as a password. Add real validation to both.
>
> This sits alongside [registration-gate-design.md](registration-gate-design.md). That doc answers
> *who may enrol*. This one answers *what a valid account looks like*. They are deliberately
> separate mechanisms — see §2.

---

## 1. What is wrong today

`AuthController.Register` performs no input validation of any kind. There is not a single
`[Required]`, `EmailAddressAttribute`, or `ModelState` check anywhere in the API project.

```csharp
var email = req.Email.Trim().ToLowerInvariant();   // normalised, never validated
...
PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password)   // any string, including ""
```

Concretely, with a valid invite code, `{"email": "x", "password": ""}` creates a working account
with an empty password. `RegisterRequest`'s non-nullable `string` types do **not** enforce anything
at runtime — ASP.NET Core's model binder happily binds `null` into them.

Three findings from checking the current behaviour rather than assuming it:

| Finding | Detail |
|---|---|
| **BCrypt silently truncates at 72 bytes** | Verified against BCrypt.Net-Next 4.0.3: `HashPassword` on a 97-byte password does not throw, and the resulting hash verifies against *any* password sharing its first 72 bytes. Everything beyond is discarded with no signal. |
| **72 is bytes, not characters** | Non-ASCII costs 2–4 bytes each, so an emoji passphrase can cross the limit well under 72 visible characters. Validation must measure UTF-8 byte length, never `string.Length`. |
| **Work factor is the library default, 11** | Hashes carry the `$2a$11$` prefix. Not changed here; see Q-V4. |

---

## 2. Validation is not the gate

It is tempting to fold "is this a disposable address?" into `IRegistrationGate` — it is, after all,
a reason to refuse a registration. It is kept separate, because the two answer different questions
and owe the caller different answers:

| | Gate | Validation |
|---|---|---|
| Question | *Are you allowed to enrol?* | *Is this input well-formed?* |
| Status | `403 Forbidden` | `400 Bad Request` |
| Fix | Go ask the owner for a code | Retype the field |
| Depends on | A configured secret | Only the submitted values |

Collapsing them would mean the gate returns `400` sometimes and `403` others, and that
`GateDecision.Reason` carries both "you weren't invited" and "your password is too short". The gate
interface stays narrow, exactly as [registration-gate-design.md §2](registration-gate-design.md)
argues it should.

---

## 3. Ordering: gate first, then validation

```csharp
1. gate.EvaluateAsync(...)        → 403 if not invited
2. ValidateEmail / ValidatePassword → 400 if malformed
3. duplicate-email check          → 409
4. create user
```

The gate doc establishes that step 3 must not precede step 1, or `/register` becomes an
account-existence oracle. That reasoning is unchanged. The new question is where validation goes
relative to the gate, and the answer is **after**, for two reasons:

- **Consistency with the oracle argument.** An uninvited caller should learn exactly one thing:
  that they were not invited. Not also which addresses the server considers plausible.
- **The breach check makes an outbound HTTPS request** (§5.3). Validating before the gate would let
  any anonymous caller drive unbounded outbound requests from the server by POSTing passwords. Gate
  first means only invite holders can do that.

The cost is that an invited user with two bad fields fixes the invite code before learning the
password is too short. Acceptable: the invite code is right on the first try or never.

---

## 4. Email validation

### 4.1 Deliberately narrower than RFC 5322

.NET's `EmailAddressAttribute` accepts `a@b`. Full RFC 5322 accepts quoted local parts
(`"john doe"@example.com`), IP-address literals (`user@[192.168.1.1]`), and comments. All are valid
and essentially nobody has one.

The rule applied is **what mail providers actually issue**, checked against the already-normalized
(trimmed, lowercased) address:

| Rule | Limit |
|---|---|
| Total length | ≤ 254 (RFC 5321 path limit) |
| Exactly one `@` | — |
| Local part | 1–64 chars; no leading, trailing, or consecutive dots; no whitespace or control chars |
| Domain | ≥ 1 dot; labels are alphanumeric + hyphen, not hyphen-initial or hyphen-final |
| TLD | ≥ 2 characters, alphabetic |

Rejecting a technically-valid-but-exotic address is a deliberate trade. If it ever bites a real
user, the fix is one relaxed rule in one file — with a test naming the address that motivated it.

### 4.2 Normalization stays as-is

`Trim().ToLowerInvariant()` before validation and before the uniqueness check is already correct and
does not change. Notably **not** added: Gmail-style dot-and-plus stripping
(`a.b+tag@gmail.com` → `ab@gmail.com`). That is provider-specific behaviour, wrong for most domains,
and would silently merge addresses their owners consider distinct.

### 4.3 Disposable-domain blocklist

A curated list of throwaway-inbox domains, vendored into the repo as an embedded resource, loaded
once at startup into a `FrozenSet<string>` (`OrdinalIgnoreCase`) and checked against the domain part.

**Runtime cost is a hash lookup** — no DNS, no network, no I/O per request. The cost of this feature
is not runtime; it is the two things below.

**The list rots.** New disposable domains appear continuously. This vendors a *snapshot* and accepts
staleness rather than auto-refreshing from a third-party URL — auto-ingesting a remote list into a
security control adds both a network dependency and a supply-chain surface, for a control the invite
gate already largely covers. Refresh is a manual PR when it matters.

**Alias services must be stripped before vendoring.** Community lists routinely include SimpleLogin,
addy.io, Apple Hide My Email (`icloud.com` aliases), Firefox Relay (`mozmail.com`), and DuckDuckGo
(`duck.com`). Those are *durable* forwarding aliases used by privacy-conscious people, not
throwaways. Blocking them tells an invited user their email provider is not allowed, which is a
hostile dead end. The chosen list will be read and pruned before it lands, not trusted wholesale.

**What was actually shipped: a hand-curated list, not a vendored community one.** The plan was to
vendor a community snapshot and prune it. On reflection that inverted the risk: those lists run to
tens of thousands of entries, every one of which is a potential false positive that locks an invited
user out, and reviewing them all to catch the handful of alias services is not realistic. A short
hand-written list of well-known throwaway providers (`src/Api/Features/Auth/disposable-domains.txt`)
covers the same realistic cases with a blast radius small enough to actually read. It will miss more
domains — acceptable, given the invite gate is the real control and this is insurance.

**Honest limit.** Registration is already invite-gated, so mass throwaway signup by strangers is
not reachable. What this catches is an *invited* user choosing a throwaway address — and the person
harmed by that is mostly them, once account recovery exists. This is inexpensive insurance, not a
load-bearing control.

---

## 5. Password policy

Follows NIST SP 800-63B: length and breach-checking do the work; composition rules do not.

### 5.1 The rules

| Rule | Value | Why |
|---|---|---|
| Minimum length | 12 characters | Above NIST's floor of 8; the cheapest real defence. |
| Maximum length | 72 **UTF-8 bytes**, enforced | Not cosmetic — §1 proved anything beyond is silently discarded. |
| Composition | **None** | No forced upper/digit/symbol. NIST explicitly discourages it: it shrinks the practical search space while producing `Passw0rd!`. |
| Breach corpus | Rejected if found | §5.3. |
| Contains the email local part | Rejected | NIST's "context-specific words" rule; one string comparison. |
| Unicode | Allowed | All printable characters accepted, including spaces and emoji. |

**The 64-vs-72 tension, stated plainly.** NIST says accept at least 64 characters. A 72-byte cap
delivers that for ASCII (72 ≥ 64) but not for a 64-character passphrase in a non-Latin script, which
can exceed 72 bytes and will be rejected with an explicit message. Rejecting loudly is strictly
better than BCrypt truncating silently. Removing the cap entirely is Q-V1.

### 5.2 Login is not re-validated

The policy applies at **registration only**. Enforcing it in `Login` would lock out any existing
account whose password predates the rules — turning a hardening change into an outage. Upgrading
existing passwords is Q-V3.

### 5.3 Breach check, and why it fails *open*

Have I Been Pwned's Pwned Passwords range API, used with k-anonymity: SHA-1 the password, send only
the **first 5 hex characters** of the digest, and match the remainder against the returned list
locally. The password never leaves the server, and neither does its full hash.

Requirements: a named `HttpClient` with a short timeout, a `User-Agent` header (HIBP rejects
requests without one), and the `Add-Padding` header to stop response size leaking the bucket.

**On timeout or error, registration proceeds.** This is the opposite of the invite gate's
fail-closed behaviour, and the difference is deliberate:

| | Invite gate | Breach check |
|---|---|---|
| Failure means | *You* misconfigured a secret you control | *A third party* is unreachable |
| Failing closed | Makes a dangerous misconfiguration loud and harmless | Hands a stranger's outage the power to close your signups |
| Therefore | Closed | Open, with a warning log |

A skipped breach check leaves the length rule intact, so failing open degrades to a weaker policy
rather than to no policy.

---

## 6. Error surface

Validation failures are `400` problem+json, consistent with the rest of the API. All failures are
returned at once rather than first-only, so the form can mark every bad field in one round-trip:

```jsonc
{
  "status": 400,
  "detail": "Password does not meet the requirements.",
  "errors": {
    "password": ["Use at least 12 characters.", "This password has appeared in a data breach."]
  }
}
```

`detail` is populated as well as `errors` because the existing client renders `error.detail`
verbatim ([registration-gate-design.md §6](registration-gate-design.md)); it keeps working
unchanged, and the per-field `errors` map is additive.

| Case | Status | Message |
|---|---|---|
| Malformed email | `400` | Enter a valid email address. |
| Disposable domain | `400` | That email provider isn't supported. Use a permanent address. |
| Password too short | `400` | Use at least 12 characters. |
| Password too long | `400` | Passwords are limited to 72 bytes (about 72 characters). |
| Password breached | `400` | This password has appeared in a data breach. Choose another. |
| Password contains email | `400` | Don't use your email address in your password. |

Breach wording says *appeared in a data breach*, not *is weak*. It is a statement about exposure,
and users who understand that are likelier to change it than to argue with it.

---

## 7. Frontend

The server stays authoritative. The client mirrors the rules to avoid teaching them one failed
submit at a time.

- A live requirements checklist under the password field in register mode, covering **every** rule
  the browser can evaluate: minimum length, the 72-byte maximum, and the email-reuse check. The
  checklist also gates the submit button, so the client never sends something the server is certain
  to reject. The byte rule matters more than it looks — it is the one a naive character count would
  miss, since 19 emoji are 19 characters but 76 bytes.
- **The breach check is not mirrored.** It would mean the browser sending password hash prefixes to
  a third party. Server-only, surfaced on submit.
- **`autocomplete` is currently wrong**: `login.html` hardcodes `current-password` in both modes. In
  register mode it must be `new-password`, or password managers offer to autofill the existing
  password instead of generating a new one. Fixing this is a prerequisite, not a nicety — a policy
  that fights the password manager pushes users toward weaker, hand-typed passwords.
- Rule *text* lives in one place per side, and the two are kept honest by a test asserting the
  client's minimum length matches the server's.

---

## 8. Testing

`EmailPolicyTests` and `PasswordPolicyTests`, table-driven in the style of the existing
`FolderNameValidationTests`. 113 tests pass in ~40 ms.

- Email accept/reject, each rejection naming the rule it exercises — including `imariel@gmail`, the
  address that prompted this work.
- Blocklist hit, plus an explicit test that alias services (`mozmail.com`, `duck.com`,
  `simplelogin.io`, `icloud.com`, …) are **not** blocked. That is the regression that would
  otherwise ship silently.
- Password: boundaries at 11/12 characters and 72/73 bytes, `welcome1` specifically, a 19-emoji
  password that is short in characters but 76 bytes, and email-in-password including the
  short-local-part exemption.

### 8.1 Verified end-to-end

Against the dockerised stack, 2026-07-23:

| Case | Result |
|---|---|
| **`imariel@gmail` / `welcome1`** | `400` — both failures reported at once |
| `user@localhost` | `400` Enter a valid email address. |
| `user@mailinator.com` | `400` That email provider isn't supported. |
| `unique-alias@mozmail.com` | **Created** — alias services are not blocked |
| 11-character password | `400` Use at least 12 characters. |
| `password1234` (12 chars, breached) | `400` This password has appeared in a data breach. |
| Password containing the email | `400` Don't use your email address in your password. |
| `wharf lantern gusto plum` | **Created** |

The breached case is the one that proves the HIBP round-trip works: it passes every local rule and
is rejected only because the corpus knows it.

In the browser: the checklist updates live and gates the submit button, a server-only failure
(breach) renders under the password field, a gate failure still renders in the summary banner, and
the password field reports `autocomplete="new-password"` in register mode.

---

## 9. Open questions

### ⏳ Q-V1 — Remove the 72-byte cap via `EnhancedHashPassword`?
BCrypt.Net-Next offers `EnhancedHashPassword`, which pre-hashes with SHA-384 and removes the 72-byte
limit. It is not adopted here because it is a different verification path: existing hashes were made
with plain `HashPassword`, so switching needs verify-old/rehash-on-login migration. Worth doing
alongside Q-V3, not before.

### ⏳ Q-V2 — Unicode normalization (NFKC)
NIST says passwords *should* be normalized before hashing, so the same passphrase typed with
different Unicode composition verifies. Not adopted: it must be applied identically at register and
login forever, and adding it later invalidates every password containing non-ASCII. Cheap to add
**now**, expensive to add later — flagged so the choice is deliberate rather than accidental.

### ⏳ Q-V3 — Upgrading existing passwords
§5.2 exempts existing accounts permanently. The usual remedy is rehash-on-successful-login, which
also covers a work-factor bump (Q-V4) and Q-V1. Unnecessary while the user count is ~1.

### ⏳ Q-V4 — BCrypt work factor
Currently the library default of 11. Raising it is a one-line change but only takes effect for new
hashes without Q-V3.

### ⏳ Q-V5 — Blocklist refresh
The vendored snapshot has no refresh mechanism (§4.3). If it ever matters, the options are a manual
PR or a scheduled job — the latter reintroducing the supply-chain surface this design avoided.

### ⏳ Q-V6 — Still no proof of ownership
None of this proves the address exists or belongs to the registrant; it proves it is well-formed and
not obviously disposable. Only a confirmation email does that, and it is the prerequisite for
password reset. Deferred, and the reason the disposable blocklist is framed as insurance rather than
a control.
