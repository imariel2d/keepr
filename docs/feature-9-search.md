# Search by File Name — Design

> Status: **Done — verified live 2026-08-02**. Feature #9 in [feature-status.md](feature-status.md).
> Backend (`SearchController`), client (`SearchService`, topbar box, Files dual-mode) and the a11y
> pass are all in; unit-tested, compile/template-verified, and exercised end-to-end against the
> dockerised stack. The live run confirmed: case-insensitive substring match on both files and
> folders (`voice` → `Invoice-…`), each hit's `location` path (`My Files / Reports / 2026`), trash
> excluded (a soft-deleted file never surfaces), LIKE-metacharacter escaping (a search for `%`
> returns only names containing a literal `%`, not everything), empty/whitespace term → `400`, the
> topbar box driving `/files?q=` into results mode with an `aria-live` count, a folder result
> opening via Enter (keyboard-operable) and clearing `q`, and a clean mobile layout with no
> horizontal scroll.
>
> Decided by Ariel, 2026-07-29: (1) search lives in the **topbar** and drives the existing **My
> Files** grid into a flat "results" mode — no separate `/search` route/component; (2) it matches
> **both files and folders** by name. Substring match, case-insensitive.
>
> This supersedes the earlier feature-status note that assumed a files-only `?q=` on `/api/media`:
> once folders are in scope and each result needs a *location*, a small dedicated endpoint is
> cleaner than overloading the media list.

---

## 1. What search is, and what it is not

Search is a **name filter over the owner's own live items**, global across the folder tree. Type
`report` and every file or folder whose name contains "report" — anywhere in your Drive — comes
back in one flat grid, each labelled with where it lives.

It is deliberately *not*: full-text/content search, tag/metadata search, trash search, or
cross-owner search. Everything stays owner-scoped and name-only, matching the personal-drive model.
Those are all natural follow-ups (§7), not part of #9.

The match is **case-insensitive substring**: `voice` finds `Invoice-2026.pdf`. Prefix-only was
considered and rejected — users expect "find anything containing this word", the Drive/Dropbox
default.

---

## 2. Backend: a dedicated `GET /api/search`

Feature status called for `?q=` on `GET /api/media`. That held while search was files-only. With
folders in scope, each result also needs a **location** (a result set spanning the tree is useless
without "where is this?"), and mixing two item kinds into the media list would distort it. So
search gets its own controller and returns the shape the grid already renders:

```
GET /api/search?q=report
→ {
    "folders": [ { "id", "name", "parentId", "createdAt", "updatedAt", "location" } ],
    "files":   [ { ...MediaListItem, "location" } ]
  }
```

`location` is the human path of the item's **containing** folder — `"My Files / Reports / 2026"`,
or `"My Files"` for an item at the root. For a folder result it is the path of its *parent*.

### 2.1 Matching

- **Columns.** The lowercased, always-maintained `MediaFile.OriginalNameLower` and
  `Folder.NameLower` — no `ToLower()` in SQL, no new column.
- **Query.** `EF.Functions.Like(col, pattern, "\\")` where `pattern = "%" + Escape(q) + "%"`.
- **Escaping.** LIKE metacharacters are escaped exactly as
  [`NameAllocator.SeriesPattern`](../src/Api/Services/NameAllocator.cs) already does —
  `\` → `\\`, `%` → `\%`, `_` → `\_` — so a search for `50%` is a literal, not "match everything".
  Factor this into a shared `LikeEscape` helper and have `NameAllocator` call it too.
- **Scope.** Owner-scoped; global (folder is ignored). Live only — folders carry a `DeletedAt`
  query filter; files add `Status == Ready && DeletedAt == null`. Trash never appears.
- **Empty `q`.** Whitespace-only or empty → `400`. The client never calls search in that state
  (an empty box means browse mode), so this only guards direct callers.
- **Cap.** Return at most 200 of each kind, ordered as the grid expects (folders by name, files by
  `CreatedAt` desc). A personal 5 GB drive is very unlikely to exceed this; if it ever does, the
  client shows a "narrow your search" hint. Not a security bound, just payload sanity.

### 2.2 Locations without N recursive CTEs

Breadcrumbs are normally a recursive CTE per folder ([`FolderService.BreadcrumbsAsync`](../src/Api/Services/FolderService.cs)).
Running that once per result would be N round-trips. Instead, load the owner's folder skeleton once:

```
SELECT "Id", "Name", "ParentId" FROM "Folders" WHERE "OwnerId" = @u   -- live only (query filter)
```

A personal drive has at most a few hundred folders (depth is capped at 32), so this is one cheap
indexed read. Build an in-memory `id → (name, parentId)` map and walk `parentId` upward to render
any item's path. No CTE, no per-result query. The same map serves every file and folder result.

---

## 3. Frontend: the topbar box drives the grid via the URL

Search state is a **query param on `/files`**, not component-to-component wiring. That keeps the
topbar (app shell) and the routed Files component decoupled, and makes a search linkable.

- **Topbar** ([app.html](../src/ClientApp/src/app/app.html)) — a `search` input, shown only when
  authenticated. Debounced ~250 ms; on change it does
  `router.navigate(['/files'], { queryParams: { q: term || null } })`. Searching from inside a
  subfolder navigates to `/files?q=…`, dropping folder context — search is always global.
- **Files component** ([files.ts](../src/ClientApp/src/app/features/files/files.ts)) — reads `q`
  from `route.queryParamMap` next to the existing `paramMap` subscription:
  - `q` empty → today's browse behaviour, untouched.
  - `q` present → **search mode**: call `SearchService.search(q)` and map the response into the
    existing `FolderContents` shape (`folder: null`, `breadcrumbs: []`, `folders`, `files`) so the
    grid template renders almost unchanged. The breadcrumb bar is replaced by a
    "Results for '…'" header, and each card shows its `location`.

### 3.1 Interactions in search mode

Everything reuses the existing paths:

| Action | Behaviour |
|---|---|
| Click a file | Preview (or download) — unchanged |
| Click a folder result | **Navigate into it**, which clears `q` and returns to browse |
| Context menu (rename/move/delete/share/download) | Reused verbatim |
| Checkbox / bulk selection | Reused |
| `refresh()` after a mutation | Becomes search-aware: re-runs the search instead of reloading a folder |
| Marquee select, drag-to-move | **Disabled** while `q` is active — there is no coherent drop target in a cross-folder flat list |

### 3.2 Empty & loading states

- No matches → "No files or folders match '…'." (reuses the empty-state slot).
- The result count is announced for assistive tech (§4).

### 3.3 Motion: a steady response while typing

Search-as-you-type re-fetches on every (debounced) keystroke, so the naive version blanked the
grid to a "Loading…" line and snapped the next set in — a flicker that read as broken. The states:

- **While a search resolves — the debounce window *and* the fetch — a skeleton grid** of
  placeholder cards replaces the results (`skeletonVisible`). The skeleton comes up the instant a
  keystroke makes the box diverge from the loaded query (`pendingSearch`), *before* navigation or
  any request — so typing has an immediate, stable response rather than stale results sitting
  frozen. A small `cove-spinner` replaces the result count in the header throughout.
- Detecting the debounce window needs the live box text in the view, which owns none of it — so
  the topbar publishes it to a tiny [`SearchStore`](../src/ClientApp/src/app/core/search.store.ts)
  the Files view reads. `pendingSearch = liveTerm ≠ loadedQuery`.
- Each result card **eases in on mount** (`cell-in`: fade + 6px rise). The grid tracks by id, so on
  a plain folder navigation only genuinely new cards animate.
- **Folder browsing** keeps a lighter treatment — the current grid stays and dims in place
  (`.results--busy`) during its fetch — since there's no per-keystroke churn to cover. The
  full-screen spinner is first-load only.
- New shared Cove components:
  [`cove-spinner`](../src/ClientApp/src/app/cove/lib/spinner/spinner.component.ts) and
  [`cove-skeleton`](../src/ClientApp/src/app/cove/lib/skeleton/skeleton.component.ts) (a shimmering
  box you compose into card shapes). Every animation here collapses to an instant state change
  under the global `prefers-reduced-motion` guard, so it's a11y-safe by construction.

---

## 4. Accessibility & mobile

Built on the #35 foundation.

- The input sits in a `role="search"` region with a real label ("Search files and folders"); a
  clear (`×`) button resets it and returns to browse.
- An `aria-live="polite"` region announces the outcome ("12 results", "No results") so keyboard and
  screen-reader users aren't left guessing after typing.
- **Responsive.** The topbar below 720 px is already tight (hamburger + brand + actions). The box
  collapses to a search icon that expands on activation, or drops to a full-width row beneath the
  header — decided at layout time.

---

## 5. Why this shape

- **Reuses the grid, not a new screen.** The Files component already knows how to render a
  folders-then-files grid with selection, context menus, preview, rename/move/delete. Search is a
  data source swap, not a second implementation — so the two can never drift.
- **URL-as-state** matches the rest of the app: folder id in the path, share token in the path,
  now the query in `?q=`. Back/forward and reload all behave.
- **No migration, no new index.** The lowercased columns and the `(OwnerId, ParentId)` folder
  index already exist. A per-owner scan is fine at personal-drive scale.

---

## 6. Task breakdown

1. **Backend.** `SearchController` + result DTOs; extract a shared `LikeEscape` helper (and route
   `NameAllocator` through it); in-memory path builder. Unit-test escaping, match, and path
   rendering.
2. **Client core.** `SearchService` + `SearchResults` model (`folders`/`files` with `location`).
3. **Topbar.** Search input + debounced URL wiring + clear button.
4. **Files dual-mode.** Query-param read, search header, `location` on cards, folder-click
   navigates, search-aware `refresh()`, marquee/DnD gated off under `q`.
5. **A11y/mobile pass** + verify end-to-end against the dockerised stack.
6. **Docs.** Flip this status to built, feature-status #9 → ✅, `my-decisions.md` entry.

---

## 7. Follow-ups (out of scope)

- **Search within the current folder** (a scoped toggle) rather than always-global.
- **Trash search** — the trash view has its own list; a filter there is separate.
- **Content / full-text search** — needs extracted text and a real index (tsvector or external).
- **Filters** — by type (image/pdf/…), date, size; by starred once #14 lands.
- **Server-side result cap → pagination** if the 200 cap is ever hit in practice.
