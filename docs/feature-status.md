# Feature Status

Tracking the planned feature set against what is actually implemented in the codebase.

Keepr is a **personal media store** with a folder hierarchy, rename, and a 10-day trash:
single-owner (no sharing yet). Of the 25 planned features, **7 are complete** (backend + UI)
and 18 are not started.

**Legend:** ✅ Done · 🟡 Partial · 📐 Designed (not built) · ❌ Not started

---

## Tier 1 — Core (nothing works without these)

| # | Feature | Status | Evidence / gap |
|---|---------|--------|----------------|
| 1 | Upload/download files | ✅ | Presigned S3 multipart upload (`src/Api/Features/Uploads/UploadsController.cs`) + presigned GET download (`src/Api/Features/Media/MediaController.cs`) |
| 2 | Folder hierarchy (create, nested, move) | ✅ | Backend: `Folder` entity (adjacency list), recursive-CTE subtree/breadcrumbs, cycle + depth guards, auto-suffix naming. UI: `src/ClientApp/src/app/features/files/` — breadcrumb nav, card grid, drag-and-drop + "Move to…" picker. Storage stays flat `{ownerId}/{uuid}` (FD1) |
| 3 | Authentication | ✅ | JWT register/login (`src/Api/Features/Auth/AuthController.cs`) |
| 4 | File/folder metadata storage | ✅ | File metadata (`src/Api/Domain/MediaFile.cs`) + folder metadata (`src/Api/Domain/Folder.cs`) |
| 5 | Rename/delete | ✅ | Rename via `PATCH /api/media/{id}` + `PATCH /api/folders/{id}`, exposed in the card context menu. Delete is now soft (#8) |

## Tier 2 — Makes it usable as a product

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 6 | Sharing with specific users (view/edit) | ❌ | No share/permission model; everything is owner-scoped |
| 7 | Shareable links | ❌ | Download URLs are short-TTL internal presigns, not user-facing links |
| 8 | Trash / soft delete with restore | ✅ | `DeletedAt`/`DeletedRootId`, EF global query filters, `TrashController`, `TrashPurgeService` sweeper at 10 days. UI: `features/trash/` with restore, purge, empty, and a "in Trash" line on the quota meter. **Overrides Q9 hard delete** |
| 9 | Search by file name | ❌ | List endpoint has no search/filter |
| 10 | In-browser preview (images, PDFs) | ✅ | Full-screen overlay with prev/next + keyboard (`features/files/preview-overlay.ts`). Server-side allowlist (`PreviewPolicy`) decides what may render; images/SVG via `<img>`, PDFs via `<iframe>` with a forced content type, plus video/audio. Lazy size-capped grid thumbnails |

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

- **Done (8):** upload/download, auth, quota tracking, file+folder metadata, folder hierarchy,
  rename/delete, trash, in-browser preview.
- **Not started (17):** everything else. **Tier 1 is complete.**

### Next: Tier 2

The cheapest next wins, in order:

1. **#9 search by file name** — a flat `OriginalNameLower LIKE` query; the column already exists
   and is indexed, and `GET /api/media` (unscoped) is already the all-files endpoint a search
   view would filter.
2. **#14 starred** — one boolean on `MediaFile` plus a sidebar view.
3. **#16 thumbnails** — grid thumbnails are currently capped at 500 KB and reuse the original
   image; generating real derivatives would lift that cap and cut the bytes ~200×.

**#6 sharing** is the big one, and per [my-decisions.md](my-decisions.md) Q5 it is the trigger
for revisiting malware scanning and content moderation — those become required before sharing
ships, not after.

### Known follow-ups

- **Sweeper leasing (Q-F).** `UploadCleanupService` and `TrashPurgeService` are both
  single-instance-safe only. Two instances would double-release quota — add
  `pg_try_advisory_lock` before scaling past one instance.
- ~~Browser uploads against the dockerized API don't work.~~ **Fixed 2026-07-22** by splitting
  `Storage:ServiceUrl` (what the API calls) from `Storage:PublicUrl` (the host baked into
  presigned URLs). The dockerised stack now uses `minio:9000` internally and `localhost:9000`
  for the browser.
- **Trashed-file downloads (Q-G).** Currently 404 via the global query filter — the recommended
  behaviour, but never explicitly decided.
- **Retention configurability (Q-E).** `Cleanup:TrashRetentionDays` defaults to 10.
