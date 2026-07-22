# Folder Hierarchy — Data Model Design

> Feature #2 in [feature-status.md](feature-status.md) (Tier 1), and the folder half of #4
> (file/folder metadata). Status: **designed, not implemented.**
>
> This doc covers the **data model only** — entities, keys, indexes, invariants, and the
> migration. API endpoints and UI are sketched at the end for context but are a separate step.
>
> **New here? Read [§8, the worked example](#8-appendix--worked-example), first** — it shows a
> populated tree and which rows change on create, rename, and move. The sections above it
> explain *why* the model is shaped that way.
>
> 🔄 **Under revision:** [Q-H](#-q-h--should-pathdepth-exist-at-all-raised-by-ariel-2026-07-21)
> concludes the `Path`/`Depth` denormalization should be dropped in favour of a pure adjacency
> list. §§2–5 and §8 still describe the `Path` design. Read Q-H before implementing.

---

## 1. Framing decisions (settle these first)

### FD1 — Folders are metadata, not storage layout ✅ recommended
Object keys stay flat: `{ownerId}/{uuid}{ext}`. A folder is a row that a file *points at*; it
never appears in the R2 key.

Why: renaming or moving a folder with 10k files underneath would otherwise mean 10k
`CopyObject` + `DeleteObject` calls against R2 — slow, expensive, non-atomic, and impossible to
roll back with the DB transaction. Keeping keys immutable makes move/rename a single UPDATE.
This also preserves the existing `StorageKey` unique index and the quota/sweeper logic
untouched.

Consequence: there is no way to browse the bucket "as folders". That's fine — the bucket is
private (Q8) and the DB is already the ledger (D3).

### FD2 — Hierarchy is per-owner, single-owner ✅
A folder belongs to exactly one user, same as `MediaFile`. Sharing (#6) will layer grants on
top of this tree later; nothing here should assume the tree is only ever readable by its owner.
Concretely: keep every access check in one place (`OwnedFolder(...)`, mirroring the existing
`OwnedReady(...)` helper) so it can later become a permission lookup.

### FD3 — Root is `ParentId = null`, not a synthetic root row ✅ recommended
Alternative was giving every user one real "My Files" row. Rejected: it costs a row per user at
registration, an extra join for every listing, and a "can't delete/rename the root" special case
anyway. `null` parent already means "top level" and needs no backfill for existing files.

Cost: the uniqueness constraint needs care (see §3.2), because Postgres normally treats NULLs
as distinct in unique indexes.

---

## 2. Hierarchy representation

Three standard options:

| Option | Read subtree | Move subtree | Breadcrumbs | Complexity |
|---|---|---|---|---|
| **A. Adjacency list** (`ParentId` only) | recursive CTE | 1 row UPDATE | recursive CTE | lowest |
| **B. Adjacency list + materialized path** | `WHERE path LIKE 'x/%'` | 1 UPDATE of subtree paths | free, path *is* the ancestor list | low |
| **C. Closure table** (ancestor/descendant rows) | indexed join | delete+insert O(depth×subtree) | indexed join | highest |

**Recommendation: B.** `ParentId` stays the source of truth; `Path` is a denormalized cache
maintained in the same transaction.

Why not A: every operation that matters here — cycle check on move, breadcrumb bar, "delete
folder and everything under it", "count bytes in this subtree" — is a *subtree* or *ancestor*
query. With A each one is a recursive CTE; correct, but you write and index for them repeatedly.

Why not C: the closure table is the right answer for deep, heavily-queried DAGs. This is a
strict tree of user-made folders, realistically <10 deep, and C triples the write cost of a move
for benefit we won't use.

The cost of B is the one operation that rewrites paths — moving a folder rewrites `Path` for its
whole subtree — and that is a single `UPDATE ... WHERE path LIKE @old || '%'` with prefix-index
support. Acceptable.

### Path format
`Path` is the **materialized chain of ancestor ids, including self**, using compact GUIDs and a
`/` separator, with leading and trailing slashes:

```
/                                        ← conceptual root (never stored)
/2f1c…a9/                                ← a top-level folder
/2f1c…a9/8b30…04/                        ← its child
```

Rules:
- Ids, not names — so a rename touches exactly one row, and the path never needs escaping.
- Trailing slash — so `LIKE '/2f1c…a9/%'` cannot match a sibling whose id shares a prefix.
- `Depth` = number of segments, stored alongside so it's cheap to cap and to sort.

> **See [§8 for a worked example](#8-appendix--worked-example)** — a real tree with populated
> rows, and exactly which rows change on create, rename, and move.

---

## 3. Schema

### 3.1 `Folder` (new table `keepr.Folders`)

| Column | Type | Notes |
|---|---|---|
| `Id` | uuid PK | `Guid.NewGuid()`, matching existing entities |
| `OwnerId` | uuid, FK → `Users.Id`, cascade | same shape as `MediaFile.OwnerId` |
| `ParentId` | uuid **null**, FK → `Folders.Id`, **`OnDelete: Restrict`** | null = top level |
| `Name` | varchar(255) not null | display name as typed |
| `NameLower` | varchar(255) not null | `Name.ToLowerInvariant()`, written by the app; index target for case-insensitive uniqueness |
| `Path` | varchar(1200) not null | materialized `/id/id/` chain incl. self (§2) |
| `Depth` | int not null | segment count; root-level folder = 1 |
| `CreatedAt` / `UpdatedAt` | timestamptz | same convention as `MediaFile` |

Notes on choices:
- **`ParentId` FK is `Restrict`, not `Cascade`.** A cascade delete would silently drop
  descendant folder rows while their files' bytes stayed in R2 and their quota stayed charged.
  Deleting a subtree must go through application code that also deletes objects and releases
  quota (§4.3). Restrict makes the wrong path fail loudly.
- **`Name` 255**, vs `OriginalName` 1024 for files. Folder names are typed, not derived from a
  filesystem; 255 matches every OS limit and keeps the composite unique index inside Postgres's
  ~2704-byte btree row limit.
- **`NameLower` as a stored column** rather than `citext` or a functional index: explicit, works
  identically in EF LINQ, and no extension dependency on managed Postgres. The app is the only
  writer, so drift risk is contained to one `SetName` helper.
- **`Path` 1200** = 32 levels × (32 hex + 1 slash) + 1. Pairs with the depth cap (INV4).
- No `SizeBytes` rollup column. Folder size is derivable and would be another running total to
  keep honest alongside `users.used_bytes`; if the UI wants it later, compute it with a subtree
  sum on read, and only denormalize if it actually shows up as slow.

### 3.2 Indexes and constraints

```
UNIQUE (OwnerId, ParentId, NameLower)   NULLS NOT DISTINCT
       WHERE "DeletedAt" IS NULL                                -- INV1
INDEX  (OwnerId, ParentId)                                      -- list one folder's children
INDEX  (OwnerId, Path)                                          -- subtree scans via LIKE prefix
```

The `DeletedAt IS NULL` filter comes from the trash decision (Q-C): a name held by a folder
sitting in the trash must not block reusing it. See
[trash-soft-delete-design.md §2.2](trash-soft-delete-design.md#22-index-changes).

`NULLS NOT DISTINCT` (Postgres 15+, and we target 16 per ai-design-decisions §5) is what makes
the uniqueness actually hold at the root, where `ParentId` is null. In EF/Npgsql that's
`.IsUnique().AreNullsDistinct(false)`. Without it, a user could create ten top-level folders all
named "Photos" — the classic bug with FD3.

The `(OwnerId, Path)` btree serves `Path LIKE '/x/y/%'` because the pattern is a literal prefix.

### 3.3 `MediaFile` changes

| Column | Type | Notes |
|---|---|---|
| `FolderId` | uuid **null**, FK → `Folders.Id`, `OnDelete: Restrict` | null = file sits at the user's root |
| `OriginalNameLower` | varchar(255) not null | lowercased `OriginalName`; index target for the no-duplicates rule (Q-B) |

Indexes:

```
INDEX  (OwnerId, FolderId, Status)                                        -- folder listing
UNIQUE (OwnerId, FolderId, OriginalNameLower) NULLS NOT DISTINCT
       WHERE "Status" <> 'Failed' AND "DeletedAt" IS NULL                 -- INV7 (Q-B, Q-C)
```

The listing index replaces the current `(OwnerId, Status)` index's role in the main list query.

`Restrict` for the same reason as above: a folder cannot be deleted out from under its files by
the database; the application drains it first.

**On the unique index being partial** — `WHERE Status <> 'Failed'` is load-bearing, not
tidiness. Uploads create a `Pending` row *before* any bytes move (D2), so:
- If the index covered **all** rows, every abandoned upload would poison that filename forever:
  a `Failed` row for `report.pdf` would block the user from ever re-uploading `report.pdf`.
  Excluding `Failed` means the sweeper (D-cleanup) returns the name to the pool automatically.
- If the index covered **`Ready` only**, two concurrent uploads of the same name would both
  succeed at `init` and then collide at `complete` — *after* the user waited through the whole
  upload. Covering `Pending` too moves the collision to `init`, where suffixing is free.

Because `Pending` and `Ready` are both inside the index, the `Pending → Ready` flip at complete
never introduces a new conflict — the name was already claimed at `init`.

**On the 255 cap:** `OriginalName` is currently `varchar(1024)`. A composite btree entry of
uuid + uuid + 1024 chars can exceed Postgres's ~2704-byte index-tuple limit once multibyte
characters are involved, which would turn a long filename into a failed INSERT. Narrowing to 255
(every mainstream filesystem's own limit) keeps the worst case around 1 KB. See §5 for the
migration check.

`FolderId` being nullable is what makes this an **almost** zero-backfill migration — every
existing file becomes a root-level file with no data movement. `OriginalNameLower` does need a
backfill, and the new unique index can fail on existing data; §5 covers both.

### 3.4 Entity sketch

```csharp
public class Folder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    /// <summary>Null = top level. Restrict on delete: subtrees are drained by app code (§4.3).</summary>
    public Guid? ParentId { get; set; }
    public Folder? Parent { get; set; }
    public ICollection<Folder> Children { get; set; } = new List<Folder>();
    public ICollection<MediaFile> Files { get; set; } = new List<MediaFile>();

    public string Name { get; set; } = default!;
    /// <summary>Lowercased <see cref="Name"/>; the uniqueness/index target (§3.2).</summary>
    public string NameLower { get; set; } = default!;

    /// <summary>Materialized ancestor chain including self: "/{id:N}/{id:N}/". See §2.</summary>
    public string Path { get; set; } = default!;
    public int Depth { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

---

## 4. Invariants and the operations that threaten them

| # | Invariant |
|---|---|
| INV1 | No two sibling folders of the same owner share a name, case-insensitively. |
| INV2 | A folder's `ParentId` and `OwnerId` agree: `parent.OwnerId == child.OwnerId`. |
| INV3 | The graph is acyclic — a folder is never its own ancestor. |
| INV4 | `Depth <= 32`, and `Path` is exactly the `/`-joined ids from root to self. |
| INV5 | A file's `FolderId` folder has the same `OwnerId` as the file. |
| INV6 | Deleting a folder never leaves R2 objects unreferenced or quota charged for bytes that are gone. |
| INV7 | No two live files in the same folder share a name, case-insensitively (Q-B). |

INV1 and INV7 are enforced by the DB (§3.2, §3.3) — the unique index is the referee, and the
application *reacts* to violations rather than pre-checking, so two concurrent writers can't
both pass a check-then-insert. INV2/INV3/INV4/INV5 are application-enforced; no constraint can
express them.

### 4.0 Name allocation (Q-A + Q-B) — used by create, move, rename, and upload
Collisions **auto-suffix** rather than erroring: `Photos` → `Photos (2)` → `Photos (3)`. One
helper serves folders and files both, so every entry point behaves identically.

```
AllocateName(ownerId, destination, desiredName, kind):
  base, ext = SplitExtension(desiredName)      // files only; folders have no extension
  base      = StripTrailingCounter(base)       // "Trip (2)" -> "Trip", so series continue
  taken     = SELECT NameLower FROM <table>
              WHERE OwnerId = @owner AND <parent/folder> IS NOT DISTINCT FROM @dest
                AND (NameLower = @base+ext OR NameLower LIKE @base + ' (%)' + ext)
  for n in 1..N:  candidate = n == 1 ? base+ext : $"{base} ({n}){ext}"
                  if candidate.ToLower() not in taken: return candidate
```

Three details that matter:

- **Extension-aware for files.** `report.pdf` must become `report (2).pdf`, never
  `report.pdf (2)` — the latter breaks the extension and, downstream, the content-type the UI
  infers from it.
- **Strip an existing counter** so moving `Trip (2)` into a folder that already has one yields
  `Trip (3)`, not `Trip (2) (2)`. The tradeoff: a file genuinely named `Budget (2).xlsx` gets
  treated as the second `Budget.xlsx`. That's the same thing every OS file manager does, and the
  alternative compounds parentheses forever.
- **The probe is advisory, the index is authoritative.** Two parallel requests can probe the same
  gap and both try `(2)`. Catch `PostgresException` with `SqlState 23505` on the unique index,
  re-run the allocation, retry — bounded at ~5 attempts, then 409. Without this, auto-suffix
  reintroduces exactly the race that made pre-checking wrong in the first place.

### 4.1 Create
Load the parent scoped to the caller (INV2), reject if `parent.Depth + 1 > 32` (INV4), allocate
the name (§4.0), then `Path = parent.Path + id + "/"`, `Depth = parent.Depth + 1`. Root-level:
`Path = "/" + id + "/"`, `Depth = 1`.

### 4.2 Move (`folder.ParentId = newParent`)
This is the only genuinely tricky operation. In one transaction
(worked through on real rows in [§8.6](#86-move-italy-e5f6-from-2026-into-documents-0709)):

1. Load the folder and the target parent, both owner-scoped. Same-parent → no-op.
2. **Cycle check (INV3):** reject if `newParent.Path.StartsWith(folder.Path)` — i.e. the target
   is the folder itself or lives inside it. With materialized paths this is a string comparison
   on rows already in hand, no query.
3. **Depth check (INV4):** `newParent.Depth + subtreeHeight <= 32`, where `subtreeHeight` is
   `MAX(Depth) - folder.Depth + 1` over the subtree (one aggregate query).
4. Name collision in the destination (INV1) → **auto-suffix** via §4.0 (Q-A). The move succeeds
   with a changed name; the response returns the final name so the UI can show what landed.
5. Rewrite the subtree in one statement:
   ```sql
   UPDATE keepr."Folders"
      SET "Path"  = @newPrefix || substring("Path" from @oldPrefixLen + 1),
          "Depth" = "Depth" + @delta,
          "UpdatedAt" = now()
    WHERE "OwnerId" = @owner AND "Path" LIKE @oldPrefix || '%';
   ```
   (Schema-qualified, per the convention in `AppDbContext.Schema`.)
6. Set the moved folder's own `ParentId`.

Steps 5–6 in one transaction so a crash can't leave paths disagreeing with `ParentId`. Because
`ParentId` is the source of truth, a repair job can always recompute `Path`/`Depth` from it —
the same "recompute to correct drift" posture already used for `users.used_bytes` (D3).

Moving a **file** is far simpler: validate the destination folder is owned by the caller
(INV5), set `FolderId`, done. No path, no quota, no R2 call.

### 4.3 Delete → **superseded by Q-C: this is now a soft delete**
`DELETE /api/folders/{id}` stamps `DeletedAt` across the subtree in one transaction with no R2
calls, and a background sweeper purges after 10 days. See
[trash-soft-delete-design.md §4](trash-soft-delete-design.md#4-operations). The section below is
retained because the purge job's object-before-row ordering comes from it.

<details>
<summary>Original hard-delete analysis</summary>

Two modes, and the choice matters:

- **Empty-only (recommended for the first cut):** refuse with 409 if the folder has any child
  folder or any file. Trivial, no bulk R2 work, no partial-failure story.
- **Recursive:** collect the subtree by `Path LIKE`, delete every file's object from R2
  (batched `DeleteObjects`, 1000 keys per call), release quota for the sum, then delete rows
  child-first. The hazard is INV6 under partial failure: R2 deletes are not transactional with
  Postgres. Order it *objects first, rows second* — an orphaned object costs storage until the
  sweeper (D-cleanup) catches it, whereas a deleted row with a live object leaks silently and
  permanently.

When trash/soft-delete (#8) arrives, recursive delete becomes "stamp `DeletedAt` on the subtree"
and this whole hazard goes away. That's an argument for shipping empty-only now and doing
recursive delete *after* soft-delete lands, rather than building the risky version twice.

</details>

---

## 5. Migration plan

One EF migration. Steps 1–2 are additive and safe; **steps 3–5 exist only because of Q-B** and
are the only part that can fail on existing data.

1. `CreateTable Folders` with the columns, FKs, and indexes from §3.1–3.2.
2. `AddColumn MediaFiles.FolderId` (nullable) + FK (`Restrict`) + index
   `(OwnerId, FolderId, Status)`.
3. `AddColumn MediaFiles.OriginalNameLower` (nullable at first), then backfill:
   ```sql
   UPDATE keepr."MediaFiles" SET "OriginalNameLower" = lower("OriginalName");
   ```
   then `AlterColumn` to `NOT NULL`.
4. **Narrow `OriginalName` to varchar(255)** (§3.3). This *truncates* if any existing name is
   longer, so guard it — fail the migration loudly rather than silently mangling a filename:
   ```sql
   DO $$ BEGIN
     IF EXISTS (SELECT 1 FROM keepr."MediaFiles" WHERE length("OriginalName") > 255) THEN
       RAISE EXCEPTION 'Names over 255 chars exist; resolve before narrowing the column.';
     END IF;
   END $$;
   ```
5. **De-duplicate before adding the unique index.** Every current file has `FolderId = null`, so
   all of a user's files land in one namespace at root — and today nothing stops two of them
   being `invoice.pdf`. Adding the index on such a table fails outright. Suffix the losers first,
   keeping the oldest as the unsuffixed one:
   ```sql
   WITH dupes AS (
     SELECT "Id",
            row_number() OVER (PARTITION BY "OwnerId", "FolderId", lower("OriginalName")
                               ORDER BY "CreatedAt", "Id") AS rn
       FROM keepr."MediaFiles"
      WHERE "Status" <> 'Failed'
   )
   UPDATE keepr."MediaFiles" m
      SET "OriginalName"      = m."OriginalName" || ' (' || d.rn || ')',
          "OriginalNameLower" = lower(m."OriginalName" || ' (' || d.rn || ')')
     FROM dupes d
    WHERE d."Id" = m."Id" AND d.rn > 1;
   ```
   Then create the partial unique index from §3.3.

> ⚠️ The dedupe SQL appends the suffix *after* the extension (`invoice.pdf (2)`), unlike the
> runtime helper in §4.0. Doing it properly in SQL means regex-splitting the extension; doing it
> in a one-time C# data migration is cleaner if the row count is small. **Check the real row
> count first** — on a dev database with no duplicates this step is a no-op and the question is
> moot.

Every existing row still works: `FolderId = null` means root, which is exactly where all current
files conceptually live. Nothing is rewritten in R2, and `StorageKey` is untouched.

Rollback drops the index, the columns, and the table — but note that steps 4–5 are **not**
reversible: widening `OriginalName` back to 1024 restores the type, not any truncated or
suffixed name. Take a database snapshot before running this in an environment whose data
matters.

---

## 6. API surface this implies (next step, not this doc)

```
POST   /api/folders                 { name, parentId? }        → 201 folder
GET    /api/folders/{id}/children                              → { folders[], files[] }
GET    /api/folders/{id}/breadcrumbs                           → ancestors, from Path
PATCH  /api/folders/{id}            { name? , parentId? }      → rename and/or move
DELETE /api/folders/{id}                                       → 409 if non-empty (§4.3)

GET    /api/media?folderId=         → list files in one folder (null = root)
PATCH  /api/media/{id}              { originalName?, folderId? }  → rename (#5) + move
```

`PATCH /api/media/{id}` is worth noting: it closes the rename gap (feature #5) and file-move in
the same endpoint, so folders and rename ship together and Tier 1 completes in one pass.

Also required for correctness: `POST /api/uploads/init` needs an optional `folderId` so a file
lands in the right folder atomically, instead of uploading to root and moving afterwards.

**Every one of these endpoints must return the name that was actually stored**, because
auto-suffix (Q-A/Q-B) means the server can hand back something different from what the client
asked for. `init` in particular already returns `{ mediaId, key, uploadId, partSize }` — it now
also needs `originalName`, or the client will spend the whole upload displaying a filename that
isn't the one in the database.

---

## 7. Questions and decisions

### ✅ Q-A — Name collision on move/create → **auto-suffix** (Ariel, 2026-07-21)
`Photos` → `Photos (2)`. Never a 409 for a name clash. Algorithm, extension handling, and the
mandatory 23505-retry are in [§4.0](#40-name-allocation-q-a--q-b--used-by-create-move-rename-and-upload).

### ✅ Q-B — Duplicate file names in one folder → **not allowed** (Ariel, 2026-07-21)
Dropbox-style. Overrides the earlier recommendation to allow them. Enforced by a partial unique
index on `(OwnerId, FolderId, OriginalNameLower)` excluding `Failed` rows (§3.3), with
auto-suffix from Q-A as the collision behavior — so uploading a second `invoice.pdf` silently
stores `invoice (2).pdf` rather than failing.

Consequences this decision pulled in, none of which existed before it:
- a new `OriginalNameLower` column with a backfill,
- `OriginalName` narrowed 1024 → 255 to stay inside Postgres's index-tuple limit,
- a **de-duplication pass over existing rows** before the index can be created (§5),
- `POST /api/uploads/init` must return the stored name, since it may differ from the request.

### ✅ Q-C — Recursive folder delete → **ships now, via soft delete** (Ariel, 2026-07-21)
Delete moves to Trash and purges after 10 days, which makes recursive delete transactional and
reversible — so the "empty folders only" restriction is unnecessary and `DELETE
/api/folders/{id}` is recursive from day one. **This overrides Q9 (hard delete).** Full design:
[trash-soft-delete-design.md](trash-soft-delete-design.md). The analysis that led here is kept
below because it explains *why* the ordering in the purge job is what it is.

<details>
<summary>Original analysis — the hazard soft delete removes</summary>

**The question.** Deleting `Photos/` when it contains 3 subfolders and 400 files: does the API
refuse ("folder not empty"), or does it delete everything underneath?

**Why it's not obvious.** Recursive delete is the one operation that has to keep two systems
agreeing with no transaction spanning them. `DELETE /api/folders/{id}` on a populated subtree
must: gather the subtree, issue batched `DeleteObjects` to R2 (1000 keys per call — 400 files is
1 call, 40k files is 40), release quota for the summed bytes, and delete the rows. Postgres can
roll back its half. **R2 cannot.** So a crash, a timeout, or an R2 5xx midway through leaves a
partial state, and which partial state you get is decided by the order you chose:

| Order | Crash outcome | Recoverable? |
|---|---|---|
| Objects first, rows second | objects gone, rows still present → files that 404 on download | **Yes** — rows are visible, user retries, sweeper can reconcile |
| Rows first, objects second | rows gone, objects still billing → invisible orphans | **No** — nothing left points at those keys |

So the ordering is forced (objects first). But even done right, the user can see files that exist
in the list and fail to download, and quota can be momentarily wrong. That's the cost of hard
delete (Q9) applied to a whole subtree at once.

**Why waiting for #8 removes the problem rather than deferring it.** With trash/soft delete,
recursive delete stops touching R2 at all — it becomes one UPDATE stamping `DeletedAt` across
`Path LIKE '/x/%'`, fully transactional, instantly undoable. The actual byte deletion moves to a
background reaper that processes one file at a time and can retry each independently, which is
exactly the shape that tolerates R2 failing. The dangerous version and the safe version are
**different code**, so building the dangerous one now is work you throw away.

**Recommendation: empty-only now** — `DELETE` returns 409 with a count of what's inside. The cost
is real (clearing a deep tree by hand is tedious), and the mitigation is to sequence #8 soon
after Tier 1 rather than to rush recursion now.

</details>

*Outcome: #8 was pulled forward instead, so recursion ships safely on the first pass.*

### ✅ Q-D — Depth cap → **32** (Ariel, 2026-07-21)
`Path` is `varchar(1200)` = 32 × 33 + 1. Raising it later is cheap (widen the column, bump the
constant — existing paths stay valid); lowering it is not, since existing folders could already
violate the new limit. Rationale below.

<details>
<summary>Why a cap exists at all</summary>

**Why there's a cap at all.** `Path` is a `varchar`, so it needs a maximum length, and that
length is a direct function of maximum depth: 32 levels × 33 chars (32 hex + slash) + 1 = 1057,
rounded to `varchar(1200)`. The cap also bounds things that recurse — breadcrumb rendering, the
tree sidebar, and any future sync client walking the hierarchy.

**Why a cap protects you.** Humans do not nest 32 deep; Drive users average around 3–5. The cap
isn't aimed at people, it's aimed at loops — a buggy client (or the eventual sync client, #20)
creating `a/a/a/a/...` unbounded, each level pushing `Path` longer until inserts start failing on
the index-tuple limit with a confusing error. A depth check fails early with a clear message
instead.

**What changing it costs.** Raising the cap later is cheap: widen the `varchar` and change the
constant — no data rewrite, since existing paths stay valid. Lowering it later is *not* cheap,
because existing folders may already violate it. So the asymmetry argues for starting low-ish.
For reference: 64 levels → `varchar(2200)`, still comfortably inside the btree limit; 256 levels
→ ~8.5 KB, which breaks the `(OwnerId, Path)` index entirely.

**Recommendation: keep 32.** It is ~6× beyond realistic human use and leaves the index small.

</details>

### ⏳ Q-E, Q-F, Q-G — trash retention, sweeper leasing, downloading trashed files
Moved to [trash-soft-delete-design.md §8](trash-soft-delete-design.md#8-open-questions).

### 🔄 Q-H — Should `Path`/`Depth` exist at all? (raised by Ariel, 2026-07-21)

> *"Isn't the whole point of storing `ParentId` to build a structure where we don't need to rely
> on strings?"*

**Yes — and §2 chose wrong.** `Path` is a cache of a fact `ParentId` already stores, and the
§2 comparison table omitted the decisive number:

| Operation | Option B (`Path`, as designed) | Option A (adjacency only) |
|---|---|---|
| **Move a subtree** | rewrite `Path` + `Depth` for **every descendant** | **one row**: `SET ParentId = @dest` |
| List children (the hot query) | `ParentId = X` | `ParentId = X` — *identical, `Path` unused* |
| Breadcrumbs / cycle check / subtree scan | string ops | recursive CTE, ≤32 levels, indexed |

The listing query — by far the most frequent — never touches `Path`. Everything that does is a
write-path or per-page-view operation, and a recursive CTE over a personal media store's few
thousand folders with the `(OwnerId, ParentId)` index is sub-millisecond. Soft delete reinforces
this: trashing stamps `DeletedAt` on every row in the subtree, so **reads never walk ancestors**.

So the denormalization makes the feature's headline operation (move) strictly more expensive,
adds a second source of truth that can drift, and requires a repair job — to speed up queries
that were never going to be slow.

**Revised recommendation: drop `Path` and `Depth`; pure adjacency list.** Knock-on effects:
- §3.1 loses two columns; §3.2 loses the `(OwnerId, Path)` index.
- §4.1 depth check, §4.2 cycle check, and §8.6 become recursive CTEs / a one-row UPDATE.
- [Trash §4.1](trash-soft-delete-design.md#41-trash-a-folder-recursive-one-transaction-zero-r2-calls)'s
  `LIKE` cascade becomes a `WITH RECURSIVE` UPDATE.
- **Q-D changes character:** the depth cap was forced by `Path` being a bounded `varchar`.
  Without it, 32 becomes an application guard against runaway nesting (still worth having, e.g.
  against a looping sync client) but a freely changeable constant, not a column width.

**Reversibility argues for dropping it now:** derived data can be reintroduced at any time with
one backfill CTE and no risk of loss. The case that would justify it later — `Path LIKE`
composing into a paginated "search anywhere under this folder", or per-request ancestor
permission checks once sharing (#6) lands — is speculative and not Tier 1.

*Status: revision agreed in principle; §§2–5 and §8 still describe the `Path` design and need
rewriting to match.*

---

## 8. Appendix — worked example

Ids are shortened to 4 hex chars for readability; real values are 32-hex (`Guid:N`).
Owner is one user, `u-01`.

### 8.1 The tree

```
(root, ParentId = null)
├─ Photos/            a1b2
│  └─ 2026/           c3d4
│     └─ Italy/       e5f6
│        ├─ beach.jpg
│        └─ rome.png
├─ Documents/         0709
│  ├─ resume.pdf
│  └─ Taxes/          8a9b
│     └─ w2.pdf
└─ scratch.txt        (FolderId = null → root)
```

### 8.2 `keepr."Folders"`

| Id | OwnerId | ParentId | Name | NameLower | Path | Depth |
|---|---|---|---|---|---|---|
| `a1b2` | u-01 | *null* | Photos | photos | `/a1b2/` | 1 |
| `c3d4` | u-01 | `a1b2` | 2026 | 2026 | `/a1b2/c3d4/` | 2 |
| `e5f6` | u-01 | `c3d4` | Italy | italy | `/a1b2/c3d4/e5f6/` | 3 |
| `0709` | u-01 | *null* | Documents | documents | `/0709/` | 1 |
| `8a9b` | u-01 | `0709` | Taxes | taxes | `/0709/8a9b/` | 2 |

Read `Path` as "the ids you walk through to get here, ending with me". `Depth` is just the
segment count — derivable, stored so the cap check and sorting are cheap.

### 8.3 `keepr."MediaFiles"` (folder-relevant columns only)

| Id | OwnerId | **FolderId** | OriginalName | StorageKey | Status |
|---|---|---|---|---|---|
| `m101` | u-01 | `e5f6` | beach.jpg | `u-01/9f3c…a1.jpg` | Ready |
| `m102` | u-01 | `e5f6` | rome.png | `u-01/44de…07.png` | Ready |
| `m103` | u-01 | `8a9b` | w2.pdf | `u-01/b810…22.pdf` | Ready |
| `m104` | u-01 | `0709` | resume.pdf | `u-01/7c05…9e.pdf` | Ready |
| `m105` | u-01 | *null* | scratch.txt | `u-01/1a2b…33.txt` | Ready |

Note the `StorageKey` column: **flat, owner-prefixed, no folder anywhere in it** (FD1). Nothing
below changes a single key.

### 8.4 Create "Venice" inside Italy

Load parent `e5f6` (owner-scoped), check `Depth 3 + 1 <= 32`, then:

| Id | ParentId | Name | Path | Depth |
|---|---|---|---|---|
| `bb01` | `e5f6` | Venice | `/a1b2/c3d4/e5f6/bb01/` | 4 |

Path = parent's path + own id + `/`. One INSERT, nothing else touched.

### 8.5 Rename "2026" → "2026 Trips"

| Id | Name | NameLower | Path | Depth |
|---|---|---|---|---|
| `c3d4` | ~~2026~~ **2026 Trips** | **2026 trips** | `/a1b2/c3d4/` *(unchanged)* | 2 *(unchanged)* |

**One row, two columns.** Descendants are untouched because the path stores *ids, not names* —
this is the whole reason for that choice. Zero R2 calls.

### 8.6 Move "Italy" (`e5f6`) from `2026` into `Documents` (`0709`)

Checks first:
- **Cycle (INV3):** does the target live inside the thing being moved?
  `"/0709/".StartsWith("/a1b2/c3d4/e5f6/")` → **false** → allowed. Pure string compare, no query.
- **Depth (INV4):** subtree height is 2 (Italy + Venice); `0709.Depth 1 + 2 = 3 <= 32` → ok.
- **Collision (INV1):** no folder named `italy` under `0709` → ok.

Then `oldPrefix = /a1b2/c3d4/e5f6/`, `newPrefix = /0709/e5f6/`,
`delta = (1 + 1) - 3 = -1`, and the single UPDATE from §4.2 matches every row where
`Path LIKE '/a1b2/c3d4/e5f6/%'` — which includes Italy itself, since `%` matches the empty
string:

| Id | ParentId | Path before | Path after | Depth |
|---|---|---|---|---|
| `e5f6` | ~~`c3d4`~~ **`0709`** | `/a1b2/c3d4/e5f6/` | **`/0709/e5f6/`** | 3 → **2** |
| `bb01` | `e5f6` *(unchanged)* | `/a1b2/c3d4/e5f6/bb01/` | **`/0709/e5f6/bb01/`** | 4 → **3** |

**`MediaFiles`: zero rows touched. R2: zero calls.** `beach.jpg` and `rome.png` still point at
`e5f6`; the folder moved out from under them and they came along for free. That property holds
whether the subtree has 2 files or 200,000 — the write cost scales with the number of *folders*
moved, never the files or the bytes.

The rejected case, for contrast — moving `Photos` into `Italy`:
`"/a1b2/c3d4/e5f6/".StartsWith("/a1b2/")` → **true** → 409, a folder can't become its own
descendant.

### 8.7 The queries this makes cheap

| Question | Query | Index used |
|---|---|---|
| Children of Italy | `ParentId = 'e5f6'` (folders) + `FolderId = 'e5f6' AND Status='Ready'` (files) | `(OwnerId, ParentId)`, `(OwnerId, FolderId, Status)` |
| Breadcrumbs for Venice | split `/a1b2/c3d4/e5f6/bb01/` → `Id = ANY('{a1b2,c3d4,e5f6,bb01}')`, order by `Depth` | PK — **no recursion** |
| Everything under Photos | `Path LIKE '/a1b2/%'` | `(OwnerId, Path)` prefix |
| Bytes under Photos | `SUM(SizeBytes)` joining `MediaFiles` → `Folders` on `FolderId` where `Path LIKE '/a1b2/%'` | same |
| Is Italy empty? | `EXISTS` child folder `OR EXISTS` file | both above |

Breadcrumbs are the clearest win: the path *is* the ancestor list, so rendering
`Photos / 2026 Trips / Italy / Venice` is one indexed lookup by primary key instead of a
recursive CTE walking up four levels.

### 8.8 What the API returns

`GET /api/folders/e5f6/children`:

```json
{
  "folder": { "id": "e5f6", "name": "Italy", "parentId": "0709" },
  "breadcrumbs": [
    { "id": "0709", "name": "Documents" },
    { "id": "e5f6", "name": "Italy" }
  ],
  "folders": [ { "id": "bb01", "name": "Venice", "childCount": 0 } ],
  "files": [
    { "id": "m101", "originalName": "beach.jpg", "contentType": "image/jpeg", "sizeBytes": 2411984 },
    { "id": "m102", "originalName": "rome.png",  "contentType": "image/png",  "sizeBytes": 881230 }
  ]
}
```

`files[]` is the existing `MediaListItem` shape — the folder feature adds a filter, it does not
change how a file is represented.

### 8.9 Name collisions (Q-A + Q-B in action)

Italy already holds `beach.jpg` and `rome.png`. Three collisions, three outcomes:

| Action | Requested | Stored | Why |
|---|---|---|---|
| Upload into Italy | `beach.jpg` | **`beach (2).jpg`** | INV7; suffix goes before the extension |
| Upload into Italy again | `beach.jpg` | **`beach (3).jpg`** | `(2)` is taken, probe continues |
| Upload into Documents | `beach.jpg` | `beach.jpg` | different folder, different namespace |
| Move `Taxes/` into Documents when a `taxes` folder exists | `Taxes` | **`Taxes (2)`** | INV1, Q-A |
| Upload `BEACH.JPG` into Italy | `BEACH.JPG` | **`BEACH (2).JPG`** | `OriginalNameLower` makes it case-insensitive; display case is preserved |

The rows after the first upload:

| Id | FolderId | OriginalName | OriginalNameLower | Status |
|---|---|---|---|---|
| `m101` | `e5f6` | beach.jpg | `beach.jpg` | Ready |
| `m106` | `e5f6` | **beach (2).jpg** | `beach (2).jpg` | Pending → Ready |

`m106` claims its name at `init`, while still `Pending` — which is why the unique index has to
cover `Pending`, not just `Ready` (§3.3). If that upload is later abandoned and swept to
`Failed`, it drops out of the partial index and `beach (2).jpg` becomes available again.
