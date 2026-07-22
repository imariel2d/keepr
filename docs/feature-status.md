# Feature Status

Tracking the planned feature set against what is actually implemented in the codebase.

Keepr is a **personal media store** with a folder hierarchy, rename, and a 10-day trash:
single-owner (no sharing yet). Of the 25 planned features, **4 are complete**, **3 have a
finished backend awaiting frontend work** (folders, rename, trash), and 18 are not started.

**Legend:** ✅ Done · 🔵 Backend done, frontend pending · 🟡 Partial · 📐 Designed (not built) · ❌ Not started

---

## Tier 1 — Core (nothing works without these)

| # | Feature | Status | Evidence / gap |
|---|---------|--------|----------------|
| 1 | Upload/download files | ✅ | Presigned S3 multipart upload (`src/Api/Features/Uploads/UploadsController.cs`) + presigned GET download (`src/Api/Features/Media/MediaController.cs`) |
| 2 | Folder hierarchy (create, nested, move) | 🔵 | Backend done 2026-07-22: `Folder` entity (adjacency list), `FoldersController`, recursive-CTE subtree/breadcrumbs, cycle + depth guards, auto-suffix naming. Storage stays flat `{ownerId}/{uuid}` (FD1). **Frontend pending** — [api-changes-frontend.md](api-changes-frontend.md) |
| 3 | Authentication | ✅ | JWT register/login (`src/Api/Features/Auth/AuthController.cs`) |
| 4 | File/folder metadata storage | ✅ | File metadata (`src/Api/Domain/MediaFile.cs`) + folder metadata (`src/Api/Domain/Folder.cs`) |
| 5 | Rename/delete | 🔵 | Rename: `PATCH /api/media/{id}` + `PATCH /api/folders/{id}`. Delete is now soft (#8). **Frontend pending** |

## Tier 2 — Makes it usable as a product

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 6 | Sharing with specific users (view/edit) | ❌ | No share/permission model; everything is owner-scoped |
| 7 | Shareable links | ❌ | Download URLs are short-TTL internal presigns, not user-facing links |
| 8 | Trash / soft delete with restore | 🔵 | Backend done 2026-07-22: `DeletedAt`/`DeletedRootId`, EF global query filters, `TrashController` (list/restore/purge/empty), `TrashPurgeService` sweeper at 10 days. **Overrides Q9 hard delete.** **Frontend pending** |
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

- **Done (4):** file upload/download, auth, quota tracking, file+folder metadata.
- **Backend done, frontend pending (3):** folder hierarchy (#2), rename (#5), trash (#8).
- **Not started (18):** everything else.

### Next: the frontend for #2 / #5 / #8

The API contract and a ready-to-use implementation prompt are in
[api-changes-frontend.md](api-changes-frontend.md). Five endpoints changed shape — most
importantly `POST /api/uploads/init` (takes `folderId`, returns the *stored* name, which may be
auto-suffixed) and `DELETE /api/media/{id}` (now trashes rather than destroys).

### Then: Tier 2

With Tier 1 closed, the natural next targets are **#9 search by file name** (a flat
`OriginalNameLower LIKE` query — the column already exists and is indexed) and **#10 in-browser
preview**, both of which are small next to sharing (#6).

### Known follow-ups

- **Sweeper leasing (Q-F).** `UploadCleanupService` and `TrashPurgeService` are both
  single-instance-safe only. Two instances would double-release quota — add
  `pg_try_advisory_lock` before scaling past one instance.
- **Trashed-file downloads (Q-G).** Currently 404 via the global query filter, which is the
  recommended behaviour but was never explicitly decided.
- **Retention configurability (Q-E).** `Cleanup:TrashRetentionDays` defaults to 10; staging may
  want 1 for testing.
