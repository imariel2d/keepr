# Trash / Soft Delete — Design

> Feature #8 in [feature-status.md](feature-status.md). Status: **implemented** (backend).
> Code: `src/Api/Services/TrashService.cs`, `TrashPurgeService.cs`,
> `src/Api/Features/Trash/TrashController.cs`. Shipped API:
> [api-changes-frontend.md](api-changes-frontend.md).
>
> One deviation from this doc: the subtree cascade uses a recursive CTE over `ParentId` rather
> than a `Path LIKE` prefix, because Q-H dropped the materialized path. Same semantics.
>
> **This overrides Q9 (hard delete)** in [my-decisions.md](my-decisions.md). Decided by Ariel,
> 2026-07-21: deleting moves an item to Trash; after **10 days** it is permanently purged.
>
> Companion to [folder-hierarchy-design.md](folder-hierarchy-design.md) — the two features are
> entangled (trashing a folder means trashing a subtree), and this doc resolves that doc's Q-C.

---

## 1. Short answer: yes, and it makes folder delete *easier*

The original Q9 hard-delete decision anticipated this exact upgrade:

> *"Upgrade path: a soft-delete/trash layer can be added later by introducing a `deleted_at`
> column — no painful migration."* — my-decisions.md, Q9

That holds. The migration is two nullable columns per table. What changes more substantially is
the **delete path**, which splits into three distinct operations that used to be one:

| Operation | Touches R2? | Transactional? | Reversible? |
|---|---|---|---|
| **Trash** (user deletes) | no | yes, fully | yes — restore |
| **Restore** (user undoes) | no | yes, fully | yes — trash again |
| **Purge** (10 days later, background) | yes | no (two systems) | **no** |

The win: the risky, non-transactional, cross-system step is now the *only* one users never
trigger directly, it runs in the background, and it can retry a failed item on the next tick
instead of leaving a half-deleted folder in someone's face. That is exactly why
[folder-hierarchy-design.md Q-C](folder-hierarchy-design.md#7-questions-and-decisions) argued
for waiting — with trash in place, **recursive folder delete is safe to ship immediately**, so
the "empty folders only" restriction is no longer needed.

---

## 2. Schema

### 2.1 New columns — on **both** `MediaFiles` and `Folders`

| Column | Type | Meaning |
|---|---|---|
| `DeletedAt` | timestamptz **null** | null = live. Set = in trash, and the start of the 10-day clock. |
| `DeletedRootId` | uuid **null** | The item whose deletion caused this row to be trashed. Equals the row's own `Id` when the user trashed it directly. |

`DeletedRootId` is the column people leave out and regret. Without it, restore is ambiguous:

> You trash `Italy/` on Monday. On Tuesday you trash its parent `Photos/`, which cascades over
> `Italy/` too. On Wednesday you restore `Photos/`. **Should `Italy/` come back?**

No — you deliberately threw it away on Monday, and it should stay in the trash with its own
Monday clock. `DeletedRootId` encodes that: Monday's cascade stamped `Italy.DeletedRootId =
Italy.Id`, Tuesday's cascade only stamped rows that were still live, so restoring `Photos`
touches `WHERE DeletedRootId = Photos.Id` and correctly leaves `Italy` behind.

It also gives the Trash view for free: **list rows where `DeletedRootId = Id`**. Trashing a
folder with 400 files shows *one* row in Trash, not 401.

### 2.2 Index changes

Both unique indexes from the folder design must now **exclude trashed rows**:

```
-- Folders
UNIQUE (OwnerId, ParentId, NameLower)          NULLS NOT DISTINCT
       WHERE "DeletedAt" IS NULL

-- MediaFiles
UNIQUE (OwnerId, FolderId, OriginalNameLower)  NULLS NOT DISTINCT
       WHERE "Status" <> 'Failed' AND "DeletedAt" IS NULL
```

This is not cosmetic. Without the `DeletedAt IS NULL` filter, trashing `invoice.pdf` and
uploading a fresh `invoice.pdf` would store `invoice (2).pdf` — the user deleted the old one,
so the name must be free. Q-B's no-duplicates rule applies to **live** files only.

Plus a partial index for the purge job, which scans by age across all users:

```
INDEX (DeletedAt) WHERE "DeletedAt" IS NOT NULL
```

### 2.3 Why not a `MediaStatus.Trashed`?

`Status` is the *upload* lifecycle (`Pending → Ready | Failed`). Deletion is orthogonal: a
`Pending` upload can be abandoned, and a `Ready` file can be trashed, and squashing both into
one enum makes every query ask "which kind of not-here is this?". Separate nullable column, and
`Status` keeps its current meaning.

---

## 3. The one thing that will bite: every existing query must filter trashed rows

`MediaController.List`, `OwnedReady`, `OwnedPending`, the quota reconciler — all of them
currently assume a row's existence means it's live. Adding `DeletedAt` invalidates that
everywhere at once, and a missed filter means **deleted files reappearing in someone's list**.

Use an EF Core **global query filter** so the default is correct and the exception is explicit:

```csharp
b.Entity<MediaFile>().HasQueryFilter(m => m.DeletedAt == null);
b.Entity<Folder>()   .HasQueryFilter(f => f.DeletedAt == null);
```

Then exactly three places opt out with `.IgnoreQueryFilters()`: the Trash listing, restore, and
the purge sweeper. Everything else — including code not yet written — is safe by construction.

⚠️ Two known sharp edges with global filters, worth knowing before they surprise you:
- **Required navigations to a filtered entity.** A live `MediaFile` whose `Folder` is trashed
  will load with `Folder == null`. That's the correct semantic here (see §4.3), but don't write
  code that assumes a non-null folder for a file with a non-null `FolderId`.
- **`IgnoreQueryFilters()` is per-query, not per-entity** — it disables filters for the whole
  query tree, including joins. In the purge job that's what you want.

---

## 4. Operations

### 4.1 Trash a folder (recursive, one transaction, zero R2 calls)

```sql
-- Folders in the subtree, including the target itself
UPDATE keepr."Folders"
   SET "DeletedAt" = now(), "DeletedRootId" = @id, "UpdatedAt" = now()
 WHERE "OwnerId" = @owner AND "Path" LIKE @path || '%' AND "DeletedAt" IS NULL;

-- Files anywhere in that subtree
UPDATE keepr."MediaFiles" m
   SET "DeletedAt" = now(), "DeletedRootId" = @id, "UpdatedAt" = now()
  FROM keepr."Folders" f
 WHERE m."FolderId" = f."Id" AND m."OwnerId" = @owner
   AND f."Path" LIKE @path || '%' AND m."DeletedAt" IS NULL;
```

Two statements, one transaction, no object storage involved. `AND "DeletedAt" IS NULL` is what
protects the Monday/Tuesday case in §2.1 — already-trashed rows keep their original
`DeletedRootId` and their original clock.

Trashing a single file is the degenerate case: stamp one row, `DeletedRootId = own Id`.

### 4.2 Restore

Restore `WHERE DeletedRootId = @id` (ignoring query filters), clearing both columns. Two
conflicts have to be handled, and both reuse machinery that already exists:

- **Name taken.** Something new now occupies `invoice.pdf`. Run the restored item through
  [§4.0 name allocation](folder-hierarchy-design.md#40-name-allocation-q-a--q-b--used-by-create-move-rename-and-upload)
  → it comes back as `invoice (2).pdf`. Consistent with Q-A; never fails the restore.
- **Parent is gone or still trashed.** Restoring `Italy/` whose parent `Photos/` was purged
  three days ago. Rule: **restore to the owner's root** (`ParentId = null` / `FolderId = null`)
  and recompute `Path`/`Depth` for the subtree. Alternative — refusing, or resurrecting the
  ancestor chain — is worse: refusing strands the user's data with no recovery path.

### 4.3 A trashed folder holding live files

Can't happen via the API: trashing a folder cascades to its contents (§4.1). But a *live* file
can end up pointing at a trashed folder if the file was restored while its folder stayed
trashed. The restore rule in §4.2 covers this by re-parenting to root, so the invariant is:

> **INV8** — a live row never has a trashed ancestor.

### 4.4 Purge (the 10-day sweep)

Extends the existing `UploadCleanupService` pattern in
[src/Api/Services/UploadCleanupService.cs](src/Api/Services/UploadCleanupService.cs) — same
`BackgroundService` + `PeriodicTimer` + scoped-DI shape, same single-instance caveat. Add to
`CleanupOptions`:

```csharp
public int TrashRetentionDays { get; set; } = 10;
```

Per tick, for rows where `DeletedAt < now - 10 days`:

1. Take files in batches (say 500) — do **not** load a user's entire trash at once.
2. `DeleteObjects` to R2, 1000 keys per call.
3. Per batch, in one transaction: `quota.ReleaseAsync(owner, sum)` then delete the rows.
4. After the files are gone, delete trashed folder rows **ordered by `Depth` descending** —
   deepest first, so the `Restrict` FK from child to parent is never violated.

**Ordering is objects-first, rows-second, deliberately.** If the process dies between 2 and 3,
the row survives pointing at an object that no longer exists — visible, retryable, and the next
tick finishes the job. The reverse order would delete the row first and leave an object nothing
references: invisible, and billed forever. Same reasoning as the Q-C table, but now it runs
unattended with automatic retry instead of in a user's request.

Purge must be **idempotent**: an R2 delete of an already-deleted key succeeds, so a retried
batch is harmless.

---

## 5. Quota: does trash count against the user's 5 GB?

**Recommendation: yes — trashed bytes stay counted until purge.**

- It matches Drive, and it's the honest number: those bytes really are still in R2 and really
  are still costing money.
- The alternative (release on trash, re-reserve on restore) creates a failure mode with no good
  answer: the user is at 4.9/5 GB, restores a 500 MB file, and gets *"restore failed — out of
  space"* for data they never intended to lose.

The cost is a support-question generator: *"I deleted everything and I'm still full."* Mitigate
in the API rather than in documentation — extend `GET /api/me/usage`
([MeController.cs](src/Api/Features/Me/MeController.cs)) with `trashedBytes`, so the UI can show
"4.2 GB used — 800 MB in Trash" next to an **Empty Trash** button. With the 5 GB default quota
this matters more than it would at 1 TB.

`QuotaService` needs no changes at all: trash doesn't touch usage, and purge calls the existing
`ReleaseAsync`.

---

## 6. API surface

```
DELETE /api/folders/{id}          → trash, recursive (no longer 409-if-non-empty)
DELETE /api/media/{id}            → trash

GET    /api/trash                 → items where DeletedRootId = Id, with PurgesAt
POST   /api/trash/{id}/restore    → restore subtree; returns the final name (may be suffixed)
DELETE /api/trash/{id}            → purge this item now, skipping the 10 days
DELETE /api/trash                 → empty trash
```

`GET /api/trash` should return `deletedAt` **and a computed `purgesAt`** (`deletedAt + 10 days`)
rather than making the client re-derive the retention window — the client would then have to be
redeployed if the retention setting ever changes.

`DELETE /api/trash/...` (immediate purge) runs the §4.4 logic inline. It is the one place a user
can trigger the non-transactional path directly; it's acceptable because they explicitly asked
for permanent deletion and a partial failure is retryable by re-running it.

---

## 7. Migration

Additive, and unlike the Q-B work it cannot fail on existing data:

1. `AddColumn DeletedAt` (timestamptz null) + `DeletedRootId` (uuid null) on `MediaFiles` and
   `Folders`. Existing rows get `null` = live, which is correct with no backfill.
2. Add `INDEX (DeletedAt) WHERE DeletedAt IS NOT NULL` to both.
3. **Recreate** the two unique indexes with the added `DeletedAt IS NULL` filter (§2.2).
4. Add the global query filters (§3) — a model change, not a schema change.

If this ships in the same release as folders, fold it into that migration and define the unique
indexes with the `DeletedAt IS NULL` filter from the start rather than creating and recreating
them.

---

## 8. Open questions

- **Q-E — Is 10 days configurable per environment, or fixed?** Recommend a config value
  (`Cleanup:TrashRetentionDays`, default 10) so staging can use 1 day for testing. Note the
  purge is by absolute `DeletedAt`, so lowering the setting immediately purges items that were
  already past the new threshold.
- **Q-F — Does the sweeper need a lease if the app scales past one instance?** Same caveat that
  already applies to `UploadCleanupService`; two instances purging concurrently would double-
  release quota. Worth an advisory lock (`pg_try_advisory_lock`) before either sweeper ships to
  more than one instance.
- **Q-G — Should trashed items be downloadable?** Drive says no, preview only. Simplest is for
  `download-url` to keep using the global filter and thus 404 on trashed files. Recommend that.
