---
name: i18n-translations
description: >
  Keep the app's translations complete and in step whenever user-facing text changes. Use this
  WHENEVER you add or edit any string a user reads — a label, button, heading, placeholder, toast,
  empty state, aria-label, or any `.html`/`.ts` copy under src/ClientApp — or add/change a
  user-facing server error (a `Problem(...)`/`Detail = ...` in src/Api). The app is localized into
  English (source), Spanish, and French via `@angular/localize` (see
  docs/feature-30-localization.md). The load-bearing rule: a UI string is not "done" until it is
  marked for translation and present in ALL THREE locales; a server error is not "done" until it
  carries a stable `code` with a translated client message. Consult this before writing UI copy,
  before adding a server error, and before claiming either is finished.
---

# i18n / Translations

The app ships in **English (source), Spanish (`es`), and French (`fr`)**. Localization is
**compile-time** via `@angular/localize`, and server errors are localized **client-side off stable
`code`s**. The full design is [feature-30-localization.md](../../../docs/feature-30-localization.md);
this skill is the discipline that keeps it from rotting.

## The one rule

**Any string a user can read must exist in all three locales before the change is done.** English
alone is a half-change — the same way an inaccessible or non-responsive UI is (see
[frontend-developer](../frontend-developer/SKILL.md)). If you add a word to the UI and don't add its
`es` and `fr` translations, you've shipped an untranslated app.

## When you add or change UI copy (client)

1. **Mark it.** Never leave a bare literal a user sees.
   - **Template text/attributes:** add `i18n` / `i18n-<attr>` with a **stable custom id**:
     ```html
     <h1 i18n="@@profile.title">Profile</h1>
     <button i18n="@@profile.save">Save changes</button>
     <input i18n-placeholder="@@login.email.ph" placeholder="you@example.com" />
     <button i18n-aria-label="@@nav.close" aria-label="Close menu">✕</button>
     ```
   - **TypeScript strings** (toasts, computed copy, aria set in code): use `$localize` with the same
     id convention:
     ```ts
     this.toast = $localize`:@@profile.saved:Your profile was saved.`;
     ```
   - **Counts / interpolation:** ICU in the template, not string concatenation:
     ```html
     <span i18n="@@trash.count">{n, plural, =0 {No items} =1 {1 item} other {{{n}} items}}</span>
     ```
2. **Custom ids are mandatory.** Always `@@namespace.thing`. Ids (not the English text) key the `es`
   and `fr` catalogs, so reworded English never orphans a translation. Reuse an existing id for the
   same concept; never duplicate one for two different meanings.
3. **Re-extract:** `npm run extract-i18n` (from `src/ClientApp`) updates the source catalog.
4. **Translate into BOTH `es` and `fr`.** Add the new id to `src/locale/messages.es.xlf` and
   `src/locale/messages.fr.xlf`. Don't leave a locale missing an id — Angular would silently fall
   back to English and the app would be half-translated. Mark machine translations
   `state="needs-review"` (Q-30-4) so a human pass is findable.
5. **Never translate user data.** File names, folder names, and anything the user typed stay
   verbatim in every locale. Only *app chrome* is translated.

## When you add or change a user-facing server error (API)

Server errors are translated by the **client**, off a stable `code` — the server never returns
localized prose. So:

1. **Give the `Problem()` a stable `code`** from `ErrorCodes` (add a constant if it's new):
   ```csharp
   var pd = new ProblemDetails { Status = 409, Detail = "That email is already in use." };
   pd.Extensions["code"] = ErrorCodes.EmailInUse; // snake_case, stable, never reworded
   ```
   Keep the English `detail` — it's the fallback and the code's documentation.
2. **Add the client message in all three locales.** Register the code in `ERROR_MESSAGES`
   (`core/problem-details.ts`) as a `$localize` string with an `@@errors.<code>` id, then translate
   that id in `messages.es.xlf` and `messages.fr.xlf` — same as any UI string.
3. **Render via `errorMessage(e)`**, not `problemDetail(e, 'english fallback')`, so the code path is
   used and the message comes out in the user's language.
4. **Codes are contract.** Once shipped, a code is stable — rewording the English `detail` is fine,
   renaming the `code` breaks the client mapping. Add a new code instead of repurposing one.

## Before you claim it's done

- **Catalogs are complete:** every id in the source catalog exists in `messages.es.xlf` **and**
  `messages.fr.xlf` — no missing, no orphaned. (`npm run extract-i18n` then diff the id sets.)
- **All three build:** `ng build --localize` (or at least `ng build`) compiles `en`, `es`, and `fr`
  without error.
- **No raw literal a user sees is unmarked** — grep your diff for added quoted strings in templates
  and user-facing `.ts`; each should be `i18n`/`$localize`.
- **Every new/changed `Problem()` has a `code`** and a matching `ERROR_MESSAGES` entry.
- **Report honestly** which locales you actually filled and whether translations are human or
  machine (`needs-review`).

## Also keep the docs in step

If this changes what's localized or how, update
[feature-30-localization.md](../../../docs/feature-30-localization.md) and the #30 row in
[feature-status.md](../../../docs/feature-status.md) (per
[software-engineering-basics](../software-engineering-basics/SKILL.md) and the docs-in-step rule).
