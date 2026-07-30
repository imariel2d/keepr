# Accessibility & Mobile Responsiveness — Design

> Status: **partial — a11y foundation + mobile drawer built; per-screen sweep ongoing**. Feature
> #35 in [feature-status.md](feature-status.md). Implemented: the Cove component-level a11y layer
> (focus ring, keyboard semantics, dialog/menu focus management) and the mobile hamburger drawer.
> Not yet: the per-screen sweep (trash, admin, login, upload toast, preview overlay, share viewer)
> and the open keyboard-nav decisions in §7.
>
> Decided by Ariel, 2026-07-29: (1) the mobile primary-nav pattern is a **hamburger drawer**, not a
> bottom tab bar or a stacked strip; (2) sequence the work **accessibility foundation first** at the
> component level, then mobile — so every screen inherits the a11y fixes at once.

---

## 1. Why a foundation-first approach

Keepr's UI is built on an internal design system, **Cove** (`src/ClientApp/src/app/cove/`), whose
components are shared across every feature screen. The tokens were in place — a full light/dark
theme, a `--focus-ring` colour, semantic surfaces — but the components had skipped the *interaction
and semantic* layer: clickable `<div>`s, hover-only affordances, dialogs with no dialog role.

That shape is the reason to fix it centrally. A keyboard or screen-reader user hitting the files
grid, a modal, or the nav rail hits the same handful of Cove components each time, so fixing them
once at the component level fixes them everywhere. Per-screen work (§7) is then only what a screen
adds on top.

The scope of this doc is **WCAG 2.1 AA-oriented keyboard operability, focus visibility, semantics,
and reduced-motion**, plus responsive layout down to a phone. It is not a full audit or an ARIA
authoring-practices certification.

---

## 2. The focus ring is an `outline`, not a `box-shadow`

The global focus indicator lives in one rule (`src/ClientApp/src/styles.scss`):

```css
*:focus-visible {
  outline: 2px solid var(--focus-ring);
  outline-offset: 2px;
}
```

**Why `outline` and not `box-shadow`.** The first version used `box-shadow: var(--shadow-focus)`.
It worked for buttons but silently failed on the file/folder cards — because those cards set a
`box-shadow` **inline** via `[ngStyle]`, and *inline styles win over any stylesheet selector*. The
ring the cards should have shown was overridden by their own inline shadow. `outline` is a separate
property that nothing in the app sets inline, it follows `border-radius`, and it is the literal
"orange outline" the design wants. Switching to it fixed the cards and immunised every other
focusable element against the same trap.

`--shadow-focus` still exists as a token: `cove-input` paints it on its wrapper, driven by the
input's own focus/blur signal (not `:focus-visible`), so text fields keep their softer glow.

**Reduced motion.** A global `@media (prefers-reduced-motion: reduce)` block collapses animations
and transitions to ~0 (kept, not removed, so state changes still register). The drawer slide-in and
all token-driven transitions honour it.

---

## 3. Landmarks & the skip link

`app.html` provides the page skeleton: `<header>` (banner), the primary-nav landmark (§4), and
`<main id="main-content" tabindex="-1">`. The sidebar wrapper is an `<aside>`, not a second
`<nav>`, so there is exactly one navigation landmark.

A **skip link** (`.skip-link`) is the first focusable element on the page; off-screen until focused,
it snaps into view and jumps focus to `#main-content`, letting keyboard users bypass the topbar.

---

## 4. Component semantics

| Component | File | What changed |
|---|---|---|
| **Sidebar nav** | `cove/lib/sidebar/sidebar.component.ts` | Nav items were `<div (click)>` — unreachable by keyboard. Now real `<button>`s inside `<nav aria-label="Primary">`, with `aria-current="page"` on the active item. |
| **Modal** | `cove/lib/modal/modal.component.ts` | Added `role="dialog"` + `aria-modal`, `aria-labelledby` (unique per-instance title id), **ESC to close**, a **focus trap** (Tab wraps, stray focus pulled back), initial focus into the panel, and **focus restore** to the trigger on close. Backdrop click-to-close logic preserved. |
| **File / folder cards** | `cove/lib/files/file-card.component.ts`, `folder-card.component.ts` | Cards were `<div (click)/(dblclick)>` — a keyboard dead zone. Now `tabindex="0"` + `role="button"` + `aria-label` (e.g. *"Reports, folder, 12 items"*). **Enter opens, Space selects** (ignored when a nested control is focused). The checkbox and more-actions button now reveal on **focus** as well as hover (`revealed = hover \|\| focused`), so they are reachable without a mouse. |
| **Context menu** | `cove/lib/context-menu/context-menu.component.ts` | Rows were `<div (click)>`. Now `role="menu"` with `<button role="menuitem">` rows; on open focus moves to the first item; **↑/↓** navigate (wrapping), **Home/End** jump, **Esc** closes, **Enter/Space** activate; focus restores to the trigger on close. |
| **Icon button** | `cove/lib/icon-button/icon-button.component.ts` | Added `ariaExpanded` / `ariaControls` inputs that forward to the inner `<button>` (a custom-element host can't carry them for AT). Used by the drawer's hamburger. |
| **Input** | `cove/lib/input/input.component.ts` | Added an `enter` output (`keydown.enter`) so a modal can submit on Enter. |

**Enter-to-submit** is wired into the New-folder and Rename modals in
`features/files/files.html` via `(enter)="submitCreate()"` / `(enter)="submitRename()"`.

### 4.1 Card focus model: `role="button"`, not a composite grid

The cards use `role="button"` with a couple of focusable controls inside (checkbox, more-actions),
each its own tab stop — the pragmatic pattern Google Drive uses. The fully-spec'd alternative is a
composite **grid/listbox with roving `tabindex`** (one tab stop for the whole grid, arrow keys
between items). That is better for very large grids but a bigger lift; it is recorded as an open
question in §7, not built.

---

## 5. Context-menu positioning under keyboard

Menus were positioned from `event.clientX/clientY`. A **keyboard-activated** click (Enter/Space on
the ⋮ button) is synthesised by the browser with `clientX = clientY = 0`, so the menu opened in the
top-left corner. `core/menu-anchor.ts` fixes this:

```
keyboard click  ==  event.type === 'click' && event.detail === 0
```

A real right-click is a `contextmenu` event (which keeps its true coordinates), and a real mouse
click has `detail >= 1` — so the discriminator is precise. For a keyboard click, the menu anchors
just under the triggering control's bounding box instead of at the origin. Used by both
`features/files/files.ts` and `features/files/share-dialog.ts`.

---

## 6. Mobile: the hamburger drawer

Below **720px** the persistent 240px rail is hidden and replaced by a hamburger button in the
topbar that opens an off-canvas drawer. State and behaviour live in `app.ts`; layout in `app.scss`.

- **Trigger** — a `cove-icon-button` (`.menu-btn`, shown only < 720px) with
  `aria-expanded` + `aria-controls="nav-drawer"`.
- **Drawer** — `role="dialog"` + `aria-modal`, a scrim, and a slide-in animation that the
  reduced-motion guard neutralises. It reuses the same `<cove-sidebar>` as the desktop rail.
- **Modal behaviour** — ESC closes, a **focus trap** keeps Tab inside, focus moves to the first
  item on open and **returns to the hamburger** on close. Choosing a nav item auto-closes it.
- **Shared component** — `cove-sidebar`'s inner width changed from a hardcoded `240px` to `100%`,
  so the one component fills both the desktop rail and the drawer.

The login screen was confirmed to have no horizontal overflow at 375px.

---

## 7. Open questions & follow-ups

- **Q-A1 — Tab inside the menu.** Strict WAI-ARIA menu semantics close the menu on Tab (arrows
  navigate). This surprised the owner, who expected Tab to move between items. Candidate change:
  make **Tab / Shift+Tab cycle** the items (arrows still work, Esc/click-outside still close).
  *Not yet applied — pending decision.*
- **Q-A2 — `aria-haspopup` / `aria-expanded` on the ⋮ trigger.** The more-actions icon button opens
  a `role="menu"` but does not yet advertise `aria-haspopup="menu"` + `aria-expanded`. Small,
  worth doing for AT.
- **Q-A3 — Composite grid navigation.** Whether to upgrade the card grid from per-card
  `role="button"` tab stops to a roving-`tabindex` grid/listbox with arrow-key navigation (§4.1).
- **Q-A4 — Per-screen sweep.** Trash, admin, login, upload toast, preview overlay, and the public
  share viewer have not been walked for touch-target sizing, small-viewport layout, headings, and
  live-region announcements.
- **Verification constraint.** The drawer, modal, sidebar, cards, and context menu all render only
  behind `auth.isAuthenticated()`, which needs the full backend (Postgres + MinIO + .NET). These
  were compile-verified and DOM/semantics-verified on the login screen; the two focus traps were
  **not** exercised live. The focus-ring `outline` was verified live via a `:focus-visible` probe.
