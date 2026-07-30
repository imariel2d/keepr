---
name: docs-naming
description: >
  Naming conventions for files under docs/ in this repo. Use this whenever creating a new document
  in docs/, renaming an existing one, or wiring up cross-references to a design doc — and whenever
  you add or change a feature that needs a design doc. Feature design docs are named
  feature-<N>-<slug>.md where <N> is the feature number in docs/feature-status.md. Consult this
  before adding a doc so the name matches the convention and every reference stays in step.
---

# Docs Naming Conventions

Files in `docs/` follow predictable names so a reader can find the doc for a feature from its
number alone, and so cross-references don't rot. Apply these whenever you add or rename a doc.

## Feature design docs → `feature-<N>-<slug>.md`

A design doc that describes one product feature is named:

```
feature-<N>-<slug>.md
```

- **`<N>`** is the feature's number in [`feature-status.md`](../../docs/feature-status.md) — the
  single source of truth mapping numbers to features. If the feature isn't listed there yet, **add
  its row first**, then use that number.
- **`<slug>`** is a short, lowercase kebab-case name for the feature. No `-design` suffix — the
  `feature-` prefix already marks it as a feature design doc.

Examples (the current set):

| Feature | Doc |
|---|---|
| #2 Folder hierarchy | `feature-2-folder-hierarchy.md` |
| #7 Shareable links | `feature-7-shareable-links.md` |
| #8 Trash / soft delete | `feature-8-trash-soft-delete.md` |
| #9 Search by file name | `feature-9-search.md` |
| #34 Admin console | `feature-34-admin-console.md` |
| #35 Accessibility & mobile | `feature-35-accessibility-mobile.md` |

### One feature, several docs

When a feature is big enough to warrant more than one doc, they **share the `feature-<N>-`
prefix** with distinct slugs. Authentication (#3) is the worked example:

```
feature-3-cookie-session.md
feature-3-registration-gate.md
feature-3-registration-validation.md
```

The shared prefix groups them; the slug says which slice.

## Non-feature docs

Not everything in `docs/` maps to a single feature. These keep descriptive names:

- **The feature index** — `feature-status.md`. Fixed name; it's the map every `feature-<N>-` doc
  is numbered against.
- **Decision logs** — `my-decisions.md` (the human's calls, authoritative) and
  `ai-design-decisions.md` (the assistant's recommendations). Fixed names.
- **Cross-cutting references** — a doc that spans features or is a reference rather than a feature
  design (e.g. `api-changes-frontend.md`) takes a plain, descriptive kebab-case name. Do **not**
  give it a `feature-<N>-` name; that prefix is reserved for single-feature design docs.
- **Non-Markdown artefacts** — config or data files (e.g. `r2-cors.json`) keep whatever name their
  tool expects. Out of scope for this convention.

## When you add or rename a doc

1. **New feature doc:** add the feature's row to `feature-status.md` (which assigns `<N>`), then
   create `feature-<N>-<slug>.md`. Open it with the house-style status blockquote — a **Status**
   line and "Feature #N in feature-status.md" — like the existing design docs.
2. **Renaming:** the name appears in more than the file itself. Update **every** reference in the
   same change:
   - links in other docs (including `feature-status.md` and `my-decisions.md`),
   - `See docs/<name>.md` comments and error-message strings in source (`.cs`, `.ts`) and
     `appsettings.json`,
   - the assistant's memory files under `~/.claude/.../memory/` if any point at the doc.

   After renaming, grep the repo for the old basename — there should be zero hits left.
3. Prefer updating an existing doc over adding a parallel one that will drift from it (this mirrors
   the rule in [software-engineering-basics](../software-engineering-basics/SKILL.md)).
