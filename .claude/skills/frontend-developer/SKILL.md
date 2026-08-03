---
name: frontend-developer
description: >
  Building or changing anything the user sees in the Angular client. Use this whenever you add or
  edit UI — a component, template, stylesheet, layout, screen, dialog, form, table, or nav element —
  or touch any file under src/ClientApp (`.ts` components, `.html` templates, `.scss`), or add/reuse
  a Cove component. The load-bearing rules: every visible change must be RESPONSIVE (work and read
  well on mobile, tablet, and desktop widths, not just the size it was built at) and ACCESSIBLE
  (a11y — keyboard-operable, correct roles/names, visible focus). Consult this before writing UI and
  before claiming a UI change is done.
---

# Frontend Developer

## The one rule: everything visible is responsive

Any change the user can see must hold together across **three ranges — mobile phones, tablets, and
regular (desktop) screens.** A layout that only works at the width you happened to build it at is
**not done**. Design mobile-first, then let it grow — don't bolt a media query on at the end to
rescue a desktop-only layout.

## Match the repo — don't reinvent

- **Cove design system.** Reuse the components in `src/ClientApp/src/app/cove/lib` (button, input,
  modal, icon, checkbox, tabs, sidebar, …) instead of bespoke markup. Style with the **design
  tokens** — CSS custom properties like `var(--space-*)`, `var(--text-*)`, `var(--surface-*)`,
  `var(--border-*)`, radii, shadows, `--control-height-*`. Never hardcode a px or hex value where a
  token exists; that's what keeps spacing, theming, and light/dark consistent.
- **Accessibility + mobile groundwork** lives in
  [feature-35-accessibility-mobile.md](../../../docs/feature-35-accessibility-mobile.md). Follow it:
  focus-visible rings, keyboard operability, `role`/`aria-*` on custom controls, and the off-canvas
  drawer that replaces the sidebar on small screens. Responsiveness and accessibility ship together.

## Accessibility (a11y) is not optional

Every UI change — a component, template, dialog, form, table, menu, or nav element — must be
**accessible (a11y)** from the first line, the same way it must be responsive. This is a load-bearing
requirement, not a polish pass. When you add or edit anything visible:

- **Semantics first.** Reach for the right native element (`<button>`, `<a>`, `<label>`, `<nav>`,
  `<table>`) before adding `role`. A real `<button>` is keyboard- and screen-reader-ready for free;
  a `<div>` with a click handler is not.
- **Keyboard operable.** Everything interactive works with Tab / Shift+Tab / Enter / Space / Escape
  and arrow keys where appropriate (menus, tabs). Nothing is mouse- or hover-only. Focus order is
  logical and focus is never trapped except in an open modal (where it should be trapped, and Escape
  closes).
- **Names & roles.** Every control has an accessible name — visible label, `aria-label`, or
  `aria-labelledby`. Icon-only buttons **must** carry an `aria-label`. Custom widgets get the
  correct `role` and state (`aria-expanded`, `aria-selected`, `aria-checked`, `aria-current`,
  `aria-disabled`).
- **Visible focus.** Never remove focus outlines without replacing them — keep the Cove
  focus-visible ring.
- **Announce the dynamic.** Errors, toasts, and async status use `role="alert"` / `aria-live` so
  screen readers hear them; form errors are tied to their field via `aria-describedby` /
  `aria-invalid`.
- **Perceivable.** Text contrast meets WCAG AA (use the design tokens — they're tuned for it);
  never signal state with colour alone; images/icons that carry meaning have alt text, decorative
  ones are hidden (`alt=""` / `aria-hidden`).
- **Prefer Cove.** Reusing a Cove component gets most of this for free — that's another reason not to
  hand-roll markup.

## Breakpoints — match existing usage, don't scatter new ones

- The app's **mobile boundary is ~720px** (below it the sidebar becomes a hamburger drawer). Treat
  `< 720px` as mobile.
- **Tablet** is roughly `720–1024px`; **desktop** is above that.
- Use `@media (max-width: …)` blocks like the existing screens do (e.g.
  `features/admin/email-settings.scss` stacks its two-column `.split` at `560px`). Prefer
  **content-driven** breakpoints — stack/reflow when a row gets cramped — over device-specific pixel
  guesses. If an existing screen already breaks at a given width, reuse it rather than inventing a
  neighbour.

## Build it responsive from the first line

- **Fluid layout:** flexbox/grid with `gap`, `%`, `rem`, and `min()/max()/clamp()`. Cap content with
  `max-width` + auto margins, **never a fixed width**.
- **No horizontal body scroll, ever.** Wide content (tables, long code, wide inputs) scrolls inside
  its **own** `overflow-x: auto` container — the admin table's `.table-wrap` is the pattern to copy.
- **Stack on narrow screens:** multi-column rows go `flex-direction: column`; inputs and primary
  actions go full width.
- **Media:** `max-width: 100%` on images/embeds.
- **Touch targets** stay comfortably tappable on mobile — don't shrink Cove controls below their
  token heights.
- **Motion & theme:** respect `prefers-reduced-motion` (Cove has a global guard — don't add motion
  that ignores it), and use tokens so both light and dark themes render correctly.

## Verify before claiming done

- **Build is the authority for compile/template errors:** from `src/ClientApp`, run
  `npx ng build --configuration development`.
- **Exercise all three widths** when a preview/dev server is available. The Browser pane's
  `resize_window` presets map exactly to the ranges — **mobile (375×812), tablet (768×1024), desktop
  (1280×800)** — plus check **dark mode**. Confirm: no horizontal body scroll, nothing clipped or
  overlapping, tap targets reachable, text legible, and the layout reflows (not just shrinks) as it
  narrows.
- **Check a11y, not just looks.** Tab through the change with the keyboard alone (reach every
  control, visible focus, Escape closes modals), confirm icon-only buttons and custom widgets have
  accessible names/roles, and that errors/status are announced. `role`/`aria-*` presence is
  verifiable in `read_page`'s accessibility tree.
- **Report honestly.** If you couldn't run it live, say which widths are unverified —
  "build passes; not exercised at mobile width live" is a fine sentence.
