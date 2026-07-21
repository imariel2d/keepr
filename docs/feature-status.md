# Feature Status

Tracking the planned feature set against what is actually implemented in the codebase.

Keepr is currently an **MVP personal media store**: flat (no folders), single-owner (no
sharing), hard-delete only. Of the 25 planned features, **3 are fully done**, 2 are partial,
and 20 are not started.

**Legend:** ✅ Done · 🟡 Partial · ❌ Not started

---

## Tier 1 — Core (nothing works without these)

| # | Feature | Status | Evidence / gap |
|---|---------|--------|----------------|
| 1 | Upload/download files | ✅ | Presigned S3 multipart upload (`src/Api/Features/Uploads/UploadsController.cs`) + presigned GET download (`src/Api/Features/Media/MediaController.cs`) |
| 2 | Folder hierarchy (create, nested, move) | ❌ | No `Folder` entity; storage is flat `{ownerId}/{uuid}` keys |
| 3 | Authentication | ✅ | JWT register/login (`src/Api/Features/Auth/AuthController.cs`) |
| 4 | File/folder metadata storage | 🟡 | File metadata done (name, size, type, owner, timestamps in `src/Api/Domain/MediaFile.cs`); folder metadata absent |
| 5 | Rename/delete | 🟡 | Hard delete done (`MediaController.cs`); rename has no endpoint |

## Tier 2 — Makes it usable as a product

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 6 | Sharing with specific users (view/edit) | ❌ | No share/permission model; everything is owner-scoped |
| 7 | Shareable links | ❌ | Download URLs are short-TTL internal presigns, not user-facing links |
| 8 | Trash / soft delete with restore | ❌ | Delete is hard-only; `MediaStatus.Failed` exists but no trash/restore |
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
- **Partial (2):** metadata (files ✅ / folders ❌), rename-delete (delete ✅ / rename ❌).
- **Not started (20):** everything else.

### Suggested next steps to finish Tier 1

1. Add **rename** (`PATCH /api/media/{id}`) — closes #5.
2. Add a **`Folder` entity** + parent-folder FK on `MediaFile`, with create/move endpoints —
   closes #2 and the #4 folder-metadata gap.
