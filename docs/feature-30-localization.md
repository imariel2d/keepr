# Localization (i18n) — English / Spanish / French — Design

> Feature #30 in [feature-status.md](feature-status.md). Status: **🟡 Backend done; client foundation
> + login localized** (design 2026-08-07, backend 2026-08-07, client foundation 2026-08-07). Supersedes
> the old #30 scope ("Spanish language") with full localization into **English (default), Spanish, and
> French**, covering both **client UI copy** and **user-facing server errors**, plus a **per-user
> preferred language**.
>
> **Built (backend):** `User.PreferredLanguage` (nullable) + the `AddPreferredLanguage` migration;
> `SupportedLanguages` (the pure `en`/`es`/`fr` validator, unit-tested); `ProfileResponse` gains
> `preferredLanguage`; `PATCH /api/me/profile` accepts + validates it (`400 invalid_language`); the
> **server error-`code` sweep** (§5) — `ErrorCodes` + `CodedProblem` in `src/Api/Http/`, every
> problem+json error across the 15 controllers and the folder/trash/quota service exceptions coded;
> and the **locale-serving** change in `Program.cs` (§4.3) — `UseDefaultFiles` + a `/` → `/{locale}/`
> redirect (`LocalePicker`, cookie-or-English) + per-locale SPA fallbacks.
>
> **Built (client foundation, §4):** `@angular/localize` wired (`angular.json` i18n block +
> `"localize": true` production build + polyfill + `$localize` types); `core/locale.ts` +
> `LocaleService` (current locale, cookie-persisting `switchTo` that reloads into `/{locale}/`); a
> shared `LanguageSwitcher`; `errorMessage()` in `problem-details.ts` (code → `$localize` copy, server
> `detail` fallback). The **login screen is fully localized** and the **/profile language card** wires
> the switcher to `PATCH /api/me/profile`. `es`/`fr` XLIFF catalogs (machine-translated,
> `needs-review`) — the production build emits `browser/{en,es,fr}/` with the right base hrefs and
> per-locale copy (verified, no English leak in `es`); the Dockerfile carries all three unchanged.
>
> **Not built yet:** the remaining screens' copy (files, trash, admin, share viewer, the other auth
> screens — still hardcoded English), the Phase-2 field-validation codes (§5.3), the client
> bootstrap auto-redirect for a signed-in user whose account preference differs from the current build
> (the server cookie redirect covers the same-browser case), and emails (§10 P3).
>
> Two load-bearing decisions the user made up front (see §2):
>
> 1. **Client copy is localized at build time with `@angular/localize`.** One built bundle per
>    locale, served side-by-side under `/en/`, `/es/`, `/fr/`. A user's preferred language is honored
>    by routing them to their locale's build; **changing language is a page reload into the other
>    build, not an instant in-place swap** — an accepted consequence of the compile-time approach
>    (Q-30-1).
> 2. **Server errors are localized by the client, off stable `code`s.** Every user-facing `Problem()`
>    carries a stable machine `code`; the client owns the translated copy for each code, with the
>    server's English `detail` as the fallback. Translations live in **one** place (the client), not
>    two. Extends the `code` extension already seeded in the repo (`email_in_use`,
>    `email_not_configured`, `email_unverified`).
>
> Builds on: `ProfileResponse` + `PATCH /api/me/profile` (#29,
> [feature-36-account-provisioning.md](feature-36-account-provisioning.md) §7); the problem+json
> readers in [`problem-details.ts`](../src/ClientApp/src/app/core/problem-details.ts); the `code`
> extension pattern in `MeController`, `AdminController`, `EmailChangeController`.

---

## 1. The problem and the scope

Today **every** user-facing string is hardcoded English, in two places:

- **Client UI copy** — labels, buttons, headings, empty states, toasts, aria-labels — literals in
  `.html` templates and `.ts` components across `src/ClientApp/src/app`.
- **Server error prose** — ~88 `Problem(...)` / `Detail = ...` sites across 15 controllers return
  English sentences in the RFC 7807 `detail`, which the client renders verbatim via
  `problemDetail(e, fallback)`.

The goal: the whole app reads in the user's language — **English (default), Spanish, or French** —
with a **preferred language on the account** that can be null (→ English). "Whatever comes from the
server" that a user reads must be translatable too, so server errors are in scope. Emails are also
server-rendered user-facing text; they are scoped as **Phase 3** (§10) because they can't use
client-owned copy — they need server-side per-locale templates keyed on the recipient's preference.

**Out of scope:** user-*generated* content (file names, folder names) is never translated — it's the
user's own data. Right-to-left languages are out of scope (all three target languages are LTR).

## 2. The two load-bearing decisions

### 2.1 Client: compile-time `@angular/localize` (Q-30-1)

The client localizes at **build time**. Template strings are marked with the `i18n` attribute and TS
strings with the `$localize` tagged template; `ng extract-i18n` produces a source catalog, and each
target locale gets a translated XLIFF file. `ng build --localize` then emits **one fully-built app
per locale**.

**Consequence, stated plainly:** `@angular/localize` has no runtime language switch. Each locale is a
separate build served under its own base href (`/en/`, `/es/`, `/fr/`). Honoring a user's preferred
language means **serving them the right build** (a redirect on entry); **changing** language means
persisting the new preference and **navigating into the other locale's build (a full reload)**, not
swapping strings in place. This is the tradeoff of the compile-time approach and is accepted.

Why it still works well here: language is a rarely-changed account setting, not a per-interaction
toggle; a reload on change is fine. Compile-time gives the smallest runtime (no i18n library shipped
to run in the browser, no dictionary fetch, no flash of untranslated keys) and is Angular-native.

### 2.2 Server: stable error `code`s, client owns the copy

Every user-facing `Problem()` gains a stable, machine-readable `code` (snake_case) in
`ProblemDetails.Extensions["code"]`. The client maps `code → localized message` using its own
`$localize` strings, so error copy lives **only** on the client and is translated by the exact same
mechanism as the rest of the UI. The server keeps its English `detail` as the **fallback** for any
unmapped code and for non-browser API consumers.

Rejected alternative: server localizes `detail` off `Accept-Language`. That splits translations
across server *and* client (double the surface for the sync skill to police) and localizes errors in
the request's language rather than the reader's current UI language. Codes avoid both.

## 3. Languages and the preferred-language model

Supported locales: **`en` (source, default), `es`, `fr`.** A single source of truth:

```ts
// src/ClientApp/src/app/core/locale.ts
export const SUPPORTED_LOCALES = ['en', 'es', 'fr'] as const;
export type Locale = (typeof SUPPORTED_LOCALES)[number];
export const DEFAULT_LOCALE: Locale = 'en';
export const LOCALE_NAMES: Record<Locale, string> = {
  en: 'English', es: 'Español', fr: 'Français', // each shown in its own language
};
```

### 3.1 `User.PreferredLanguage` (nullable)

```csharp
// src/Api/Domain/User.cs
/// <summary>
/// The account's preferred UI language as a supported locale code ("en" | "es" | "fr"), or
/// <c>null</c> to mean "unset — fall back to the default (English)". Set from the profile screen
/// (#30). Read on entry to route the user to their locale build, and by the server to localize
/// outbound email (Phase 3). Never inferred from Accept-Language automatically; a null value is a
/// real, honored state (default English), not a missing one.
/// </summary>
public string? PreferredLanguage { get; set; }
```

- **Migration** `AddPreferredLanguage` — adds a nullable `varchar` column. Default null → English.
- **Validation:** the API accepts only a value in `SUPPORTED_LOCALES` or null; anything else → `400`
  with code `invalid_language`.
- **Nullable is deliberate:** null distinguishes "never chose" (follow the default, and follow the
  default *even as the default changes*) from an explicit `"en"` pick.

### 3.2 The endpoint

Reuse the existing profile surface rather than adding a new controller:

- **`ProfileResponse`** gains `preferredLanguage: string | null` (mirrors `User.PreferredLanguage`).
- **`PATCH /api/me/profile`** carries `preferredLanguage` as part of the **full-profile replace**
  (like the name fields — the client sends the whole set each call): a supported code sets it;
  blank/null clears it back to the default (English); any other value → `400 invalid_language`.
  Validation is `SupportedLanguages.TryNormalize` (trims + lowercases, blank → null). *(Built.)*
- **Anonymous users** (login, claim, confirm-email, reset screens) have no account yet. With **no
  explicit choice they get English** (Q-30-3) — the browser's `Accept-Language` is deliberately
  **not** consulted. Only picking a language in the switcher records it, in a **cookie**
  (`keepr_lang`, `SameSite=Lax`, 1 year) plus `localStorage`. The server reads this cookie only for
  the root redirect (§4.3) — never to auto-set the account preference.

## 4. Client architecture (`@angular/localize`)

### 4.1 Setup

- `ng add @angular/localize` (adds the package + polyfill import in `main.ts`).
- `angular.json` → `projects.ClientApp`:
  ```jsonc
  "i18n": {
    "sourceLocale": "en",
    "locales": {
      "es": "src/locale/messages.es.xlf",
      "fr": "src/locale/messages.fr.xlf"
    }
  },
  ```
  and the `build` options gain `"localize": true` so a plain build emits all three. The dev server
  runs a single locale at a time (`ng serve --configuration development` stays English; pass
  `"localize": ["es"]` to preview Spanish).

### 4.2 Marking strings

- **Templates:** every user-visible text node / attribute gets `i18n` / `i18n-<attr>`, with a
  stable, namespaced **custom id** so ids don't churn when copy is reworded:
  ```html
  <h1 i18n="@@profile.title">Profile</h1>
  <button i18n="@@profile.save">Save changes</button>
  <input i18n-placeholder="@@profile.email.placeholder" placeholder="you@example.com" />
  <img i18n-alt="@@avatar.alt" alt="Your avatar" />
  ```
- **TypeScript:** `$localize` with the same id convention:
  ```ts
  this.toast = $localize`:@@profile.saved:Your profile was saved.`;
  ```
- **Pluralization / interpolation** uses ICU in the template:
  ```html
  <span i18n="@@trash.count">{count, plural, =0 {No items} =1 {1 item} other {{{count}} items}}</span>
  ```
- **Custom ids are mandatory** in this repo (the sync skill enforces it) so that the `es`/`fr`
  catalogs key off ids, not off the English source text — reword English without orphaning
  translations.

### 4.3 Serving the locale builds

`ng build` emits `dist/ClientApp/browser/{en,es,fr}/` each with its own `index.html` (base href
`/en/` etc.). The Dockerfile copies the whole `browser/` tree into `wwwroot/`, so the image contains
all three builds. `Program.cs` changes its single SPA fallback into **per-locale fallbacks + a root
redirect**:

```csharp
app.UseStaticFiles();

// Root and any unprefixed path → pick the locale (cookie → en) and redirect.
app.MapGet("/", (HttpContext ctx) => Results.Redirect($"/{LocalePicker.Pick(ctx)}/"));

// Per-locale SPA deep-link fallback: /es/files → es/index.html, etc.
foreach (var loc in new[] { "en", "es", "fr" })
    app.MapFallbackToFile($"{{*path}}", $"{loc}/index.html")
       .Add(/* constrained to the /{loc}/ prefix */);
```

`LocalePicker.Pick` reads the `keepr_lang` cookie; **absent → `en`**. `Accept-Language` is
deliberately **ignored** for the redirect (Q-30-3), so the default is always English until the user
explicitly picks otherwise. A signed-in user whose `PreferredLanguage` differs from the build they
landed on is bounced to the right one by a tiny client guard on bootstrap (it also sets the
`keepr_lang` cookie so future root hits go straight there).

### 4.4 The language switcher

- **Signed-in:** the language `<select>` on the **profile screen** persists via
  `PATCH /api/me/profile`, then navigates to `/{lang}/…` (full reload into that build). A compact
  switcher also lives in the sidebar/topbar user area for quick access.
- **Anonymous:** a switcher on the login/claim/reset/confirm screens sets the `keepr_lang` cookie +
  `localStorage` and navigates to `/{lang}/` — no account write.
- Both use native `<select>`/`<button>` with an `aria-label`, following
  [frontend-developer](../.claude/skills/frontend-developer/SKILL.md) (keyboard-operable, labelled).

### 4.5 Locale-aware formatting

- Register locale data for `es` and `fr` (`registerLocaleData`) so `DatePipe`/`DecimalPipe` format
  per locale; `@angular/localize` wires `LOCALE_ID` per build automatically.
- The custom [`bytes.pipe.ts`](../src/ClientApp/src/app/core/bytes.pipe.ts) needs the **unit words**
  localized but keeps the number grouping via `DecimalPipe`. Units (`KB`, `MB`, `GB`) are largely
  language-neutral, but the pipe's any-word output (e.g. a leading "of") must be marked.

## 5. Server architecture (error codes)

### 5.1 The code registry

Introduce one authoritative list so codes are discoverable and never silently diverge from the
client:

```csharp
// src/Api/Http/ErrorCodes.cs — the canonical set of user-facing error codes.
public static class ErrorCodes
{
    public const string EmailInUse = "email_in_use";
    public const string PasswordIncorrect = "password_incorrect";
    public const string QuotaExceeded = "quota_exceeded";
    public const string InvalidLanguage = "invalid_language";
    // …one per user-facing Problem() across the 15 controllers (~40 codes).
}
```

A shared extension standardizes attaching a code — it goes through the controller's
`ProblemDetailsFactory`, so a coded response still gets Type/Title/traceId (strictly better than the
ad-hoc `new ProblemDetails { … }` sites it replaced):

```csharp
// src/Api/Http/ControllerErrorExtensions.cs
public static ObjectResult CodedProblem(this ControllerBase c, int status, string code, string detail);
```

Each user-facing `Problem()` site is swept to attach a code (existing `email_in_use`,
`email_not_configured`, `email_unverified` keep their exact strings — the client branches on them).
The English `detail` stays as the fallback and as the API's own documentation of what the code means.
Service-layer errors carry the code too: `FolderException`/`TrashException` gained a `Code` (like the
`StatusCode` they already carried), so the controllers that forward them (`Problem(ex.Message,
ex.StatusCode)` → `CodedProblem(ex.StatusCode, ex.Code, ex.Message)`) localize for free.

> **Status: done.** `ErrorCodes` (~40 constants) + the `CodedProblem` extension live in
> `src/Api/Http/`; every problem+json `Problem()` across the 15 controllers and the 19 service-exception
> throw sites now carry a stable code. A unit test asserts the code values are unique + snake_case.
> **Deferred (Phase 2):** the `ValidationProblemDetails` field-error maps (password/email/register
> validation stay English for now, §5.3) and the two bespoke non-problem+json shapes in
> `UploadsController` (the anonymous `{ error }` guardrails and the `413` `{ error, remaining }`
> quota body, which carries extra data).

### 5.2 Client-side rendering

Extend [`problem-details.ts`](../src/ClientApp/src/app/core/problem-details.ts):

```ts
// code → localized message, each entry a $localize string (translated in es/fr like any UI copy).
const ERROR_MESSAGES: Record<string, () => string> = {
  email_in_use: () => $localize`:@@errors.email_in_use:That email is already in use.`,
  password_incorrect: () => $localize`:@@errors.password_incorrect:That password is incorrect.`,
  quota_exceeded: () => $localize`:@@errors.quota_exceeded:You don't have enough space.`,
  // …
};

/** Localized, user-facing message for a failed call: mapped code → server detail → generic. */
export function errorMessage(e: unknown): string {
  const code = problemCode(e);
  if (code && ERROR_MESSAGES[code]) return ERROR_MESSAGES[code]();
  return problemDetail(e, $localize`:@@errors.generic:Something went wrong. Please try again.`);
}
```

Call sites move from `problemDetail(e, '…english…')` to `errorMessage(e)`. `problemDetail` stays for
the rare place that genuinely wants raw server text.

### 5.3 Field validation (400 with an `errors` map)

ASP.NET model-validation messages (`[Required]`, `[MaxLength]`, custom `EmailPolicy`/`PasswordPolicy`
text) surface in the `errors` map and are English prose. Handling, phased:

- **Phase 1:** the top-level `detail`/`code` is localized (covers the message the user actually sees
  for most failures). Field-level `errors` entries stay English.
- **Phase 2:** the small set of **custom** validators (`EmailPolicy`, `PasswordPolicy`,
  `RegistrationGate`) emit codes; the client renders a localized per-field message from the code.
  Generic framework validators map to a handful of localized templates (`field_required`,
  `too_long`) on the client keyed by the validation kind.

This keeps Phase 1 shippable without rewriting every validator on day one.

## 6. What needs translating (inventory)

| Surface | Where | Mechanism | Phase |
|---|---|---|---|
| UI copy (labels, buttons, headings, empty states) | `.html` templates | `i18n` attr + custom id | 1 |
| UI copy in code (toasts, computed strings, aria) | `.ts` components | `$localize` | 1 |
| Server business errors | ~88 `Problem()` sites | `code` + client `ERROR_MESSAGES` | 1 |
| Field validation messages | ModelState `errors` map | codes on custom validators | 2 |
| Locale formatting (dates, numbers, bytes units) | pipes | `registerLocaleData` + marked units | 1 |
| Outbound email (invite, reset, change-email, heads-up) | `EmailTemplates.cs` | server-side per-locale templates keyed on recipient `PreferredLanguage` | 3 |

## 7. Build & deployment changes

- **`angular.json`** — `i18n` block + `"localize": true` (§4.1).
- **`package.json`** — `@angular/localize` dependency; an `extract-i18n` script
  (`ng extract-i18n --output-path src/locale`).
- **Dockerfile** — `RUN npm run build` already emits `dist/ClientApp/browser/`; with `localize` on,
  that dir now holds `en/ es/ fr/`. The existing `COPY --from=client …/browser/ …/wwwroot/` carries
  all three in unchanged. **Budgets ×3:** three builds means the `production` build runs longer;
  bundle budgets are per-locale and unaffected.
- **`Program.cs`** — replace the single `MapFallbackToFile("index.html")` with the root redirect +
  per-locale fallbacks (§4.3). This is the one non-mechanical server change.

## 8. E2E scenarios and expected output

No browser-e2e framework here (see [feature-e2e-design](../.claude/skills/feature-e2e-design/SKILL.md));
scenarios are expressed at the boundary each crosses — xUnit for the API, a documented click-path for
the client, and the build for the localized bundles.

### 8.1 Happy path — a user sets Spanish and the app is Spanish (full-stack)

1. **Given** a signed-in user with `PreferredLanguage = null`, on `/en/profile`.
2. **When** they pick **Español** in the language switcher and save.
3. **Then** `PATCH /api/me/profile` with `{ "preferredLanguage": "es" }` returns **`200`** and
   `ProfileResponse.preferredLanguage == "es"`; the row's `User.PreferredLanguage` is `"es"`.
4. The client navigates to `/es/profile` (full reload). **Expected on screen:** the profile heading
   reads **"Perfil"**, the save button **"Guardar cambios"**, the language select shows **Español**
   selected. The `keepr_lang` cookie is now `es`.
5. **When** they later sign out and return to `/`, **then** `LocalePicker.Pick` reads the `es` cookie
   and redirects to `/es/login` — the login screen renders in Spanish before any account is loaded.

**xUnit slice** (`ProfilePreferredLanguageTests`): `PATCH /api/me/profile {preferredLanguage:"es"}` →
`200`, `GET /api/me/profile` → `preferredLanguage:"es"`; `PATCH {preferredLanguage:null}` → `200`,
persisted null; `PATCH {preferredLanguage:"de"}` → `400`, `code == "invalid_language"`; unchanged when
the field is omitted.

### 8.2 Server error renders in the user's language (full-stack)

1. **Given** the app is running in French (`/fr/…`), a user changes their email to one already taken.
2. **When** `POST /api/me/email` returns **`409`** with `detail = "That email is already in use."`
   and **`code = "email_in_use"`**.
3. **Then** the client's `errorMessage(e)` maps `email_in_use` → **`$localize @@errors.email_in_use`**,
   whose French catalog entry **"Cet e-mail est déjà utilisé."** is shown. The English `detail` is
   **not** shown. **Invariant:** the message reflects the
   *current UI language*, independent of what `Accept-Language` the request carried.
4. **Fallback path:** a `Problem()` whose `code` isn't in `ERROR_MESSAGES` → the user sees the
   server's English `detail` (never a blank or a raw code), proving graceful degradation.

### 8.3 Default / null-preference path

- **Given** a brand-new account with `PreferredLanguage = null` and no `keepr_lang` cookie, a browser
  sending `Accept-Language: fr`. **When** they hit `/`. **Then** `LocalePicker.Pick` → **`en`** — the
  browser hint is ignored (Q-30-3), so first-touch is always English — and `User.PreferredLanguage`
  **stays null** (never auto-written). Only after the user explicitly picks Français (cookie set,
  or preference saved) does `/` redirect to `/fr/`. Confirms both that English is the hard default and
  that null is a live default, not a silent `"en"` write.

### 8.4 Invariants (must always hold)

- **User content is never translated** — a file named "Report" reads "Report" in all three locales.
- **No untranslated keys reach the user** — a missing `es`/`fr` catalog entry falls back to the
  English source (Angular's default), never renders the raw id. The build fails CI if a catalog is
  missing an id (`ng extract-i18n` diff check — the skill, §9).
- **Every user-facing `Problem()` has a `code`** — asserted by a test that scans controllers (or a
  reviewer checklist) so new errors can't ship code-less and thus untranslatable.
- **Language change is a reload, not a swap** — after switching, `location.pathname` starts with the
  new `/{lang}/` prefix (the accepted Q-30-1 consequence, verified, not a bug).

### 8.5 Build/verification slice

- `ng build --localize` produces `dist/ClientApp/browser/{en,es,fr}/index.html`, each with the
  correct base href — the deployable proof all three locales compile.
- `ng extract-i18n` produces a source catalog whose id set **equals** the id sets in `messages.es.xlf`
  and `messages.fr.xlf` (no missing, no orphaned) — the sync check the skill automates.

## 9. Keeping translations in sync — the skill

A repo skill, **`.claude/skills/i18n-translations/`**, makes "translations are up to date" a
load-bearing rule the same way `docs-naming` and `frontend-developer` are. It triggers whenever UI
text is added/changed or a server error is added, and requires: mark the string
(`i18n`/`$localize` with a custom id), re-extract, add the `es` **and** `fr` translations, and — for
server errors — assign a stable `code` and add its three client messages. See that skill for the
mechanics.

## 10. Rollout / phasing

1. **Phase 1 — foundation + primary surfaces.** `@angular/localize` setup, `es`/`fr` catalogs, the
   `User.PreferredLanguage` column + endpoint + profile switcher, the locale-serving `Program.cs`
   change, error `code`s on all business `Problem()`s + client `errorMessage`, and the sync skill.
   Translate the highest-traffic screens first (login, files, profile, trash, share viewer).
2. **Phase 2 — field validation.** Codes on custom validators; localized per-field messages.
3. **Phase 3 — outbound email.** Server-side per-locale templates keyed on recipient
   `PreferredLanguage` (invite, reset, change-email, old-address heads-up).

## 11. Open questions / decisions

- **Q-30-1 (decided).** Client i18n is compile-time `@angular/localize`; language change is a reload
  into the per-locale build, accepted over a runtime dictionary swap.
- **Q-30-2 (decided).** Server errors localized client-side off stable `code`s; server `detail` is the
  English fallback. No `Accept-Language` server localization.
- **Q-30-3 (decided).** `/` with no cookie **always defaults to `/en/`**; `Accept-Language` is
  ignored for the redirect. English is the hard default until the user explicitly picks a language;
  the account preference is still never auto-written.
- **Q-30-4 (decided).** Ship the `es`/`fr` catalogs **machine-translated**, entries marked
  `state="needs-review"` so a human pass is findable; correct iteratively rather than holding a
  locale for full human translation.
- **Q-30-5 (deferred).** Additional locales / RTL — the architecture (locale list + per-locale build)
  extends to more languages by adding a catalog + build; RTL would add a `dir` concern, not needed
  for en/es/fr.
