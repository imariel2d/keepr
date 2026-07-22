# Feature Status

Tracking the planned feature set against what is actually implemented in the codebase.

Keepr is currently an **MVP personal media store**: flat (no folders), single-owner (no
sharing), hard-delete only. Of the 25 planned features, **3 are fully done**, 2 are partial,
and 20 are not started.

**Legend:** ✅ Done · 🟡 Partial · 📐 Designed (not built) · ❌ Not started

---

## Tier 1 — Core (nothing works without these)

| # | Feature | Status | Evidence / gap |
|---|---------|--------|----------------|
| 1 | Upload/download files | ✅ | Presigned S3 multipart upload (`src/Api/Features/Uploads/UploadsController.cs`) + presigned GET download (`src/Api/Features/Media/MediaController.cs`) |
| 2 | Folder hierarchy (create, nested, move) | 📐 | Data model designed in [folder-hierarchy-design.md](folder-hierarchy-design.md) (2026-07-21); no `Folder` entity or migration yet. Storage stays flat `{ownerId}/{uuid}` by design (FD1) |
| 3 | Authentication | ✅ | JWT register/login (`src/Api/Features/Auth/AuthController.cs`) |
| 4 | File/folder metadata storage | 🟡 | File metadata done (name, size, type, owner, timestamps in `src/Api/Domain/MediaFile.cs`); folder metadata designed but not built (see #2) |
| 5 | Rename/delete | 🟡 | Delete done (`MediaController.cs`) but becomes soft delete per #8; rename has no endpoint (designed: `PATCH /api/media/{id}`) |

## Tier 2 — Makes it usable as a product

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 6 | Sharing with specific users (view/edit) | ❌ | No share/permission model; everything is owner-scoped |
| 7 | Shareable links | ❌ | Download URLs are short-TTL internal presigns, not user-facing links |
| 8 | Trash / soft delete with restore | 📐 | Designed in [trash-soft-delete-design.md](trash-soft-delete-design.md) (2026-07-21): delete → Trash → purge after 10 days. **Overrides Q9 hard delete.** Pulled forward because it makes recursive folder delete (#2) safe |
| 9 | Search by file name | ❌ | List endpoint has no search/filter |
| 10 | In-browser preview (images, PDFs) | ❌ | No preview UI (download-url could feed one, but nothing built) |

## Tier 3 — Expected by users who've used real Drive/Dropbox

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 11 | Version history + restore | ❌ | One row per file, overwritten in place |
| 12 | Storage quota tracking per user | ✅ | Full quota reserve/reconcile/release (`src/Api/Services/QuotaService.cs`), live meter via `src/Api/Features/Me/MeController.cs` |
| 13 | Activity log (who did what, when) | ❌ | No event/audit table |
| 14 | Starred/favorites | ❌ | No flag on `MediaFile` |
| 15 | "Recent" and "Shared with me" views | ❌ | List is sorted newest-first, but no dedicated views; sharing doesn't exist |
| 16 | Thumbnail generation | ❌ | Docs note post-processing is deferred |

## Tier 4 — Collaboration layer

| # | Feature | Status |
|---|---------|--------|
| 17 | Comments on files | ❌ |
| 18 | Notifications (shared/edited/commented) | ❌ |
| 19 | Real-time presence ("who's viewing this") | ❌ |

## Tier 5 — Scale/enterprise concerns

| # | Feature | Status |
|---|---------|--------|
| 20 | Sync client with offline support + conflict resolution | ❌ |
| 21 | Admin console / org-wide management | ❌ |
| 22 | Audit logs for compliance | ❌ |
| 23 | Virus/malware scanning on upload | ❌ (explicitly deferred in README) |
| 24 | Shared drives / team spaces (vs. personal-only) | ❌ |
| 25 | Retention/compliance policies | ❌ |

---

## Summary

- **Done (3):** file upload/download, auth, quota tracking.
- **Designed, not built (2):** folder hierarchy (#2), trash/soft delete (#8).
- **Partial (2):** metadata (files ✅ / folders 📐), rename-delete (delete ✅ / rename ❌).
- **Not started (18):** everything else.

### Next release: folders + rename + trash, together

These three interlock and are cheaper as one migration than three. Folder move and file rename
share an endpoint; trash changes the unique indexes that folders introduce, so defining them
once avoids creating and recreating them.

1. **One migration:** `Folders` table; `MediaFile.FolderId`; `OriginalNameLower` + backfill;
   `OriginalName` narrowed to 255; `DeletedAt`/`DeletedRootId` on both tables; unique indexes
   defined with their `DeletedAt IS NULL` filter from the start.
   ⚠️ Includes a **de-duplication pass over existing filenames** that can fail on real data —
   [folder design §5](folder-hierarchy-design.md#5-migration-plan).
2. **EF global query filters** for `DeletedAt == null` before any trash code — this is the step
   that makes every existing and future query safe by default
   ([trash design §3](trash-soft-delete-design.md#3-the-one-thing-that-will-bite-every-existing-query-must-filter-trashed-rows)).
3. **Folder endpoints:** create / list children / breadcrumbs / rename+move / recursive delete.
   Closes #2 and the #4 folder-metadata gap.
4. **`PATCH /api/media/{id}`** taking `originalName` and `folderId` — closes #5 (rename), adds
   file move.
5. **`folderId` on `POST /api/uploads/init`**, which must now also return the stored
   `originalName` (auto-suffix can change it).
6. **Trash endpoints + purge sweeper** on the existing `UploadCleanupService` pattern — closes
   #8. Extend `GET /api/me/usage` with `trashedBytes`.

Decided: [Q-A–Q-D](folder-hierarchy-design.md#7-questions-and-decisions) (auto-suffix names, no
duplicate filenames per folder, recursive delete via trash, depth cap 32).
Still open, none blocking: [Q-E–Q-G](trash-soft-delete-design.md#8-open-questions).
