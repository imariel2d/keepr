# API Changes — Backend → Frontend Handoff

> Backend for **#2 folder hierarchy**, **#5 rename**, and **#8 trash/soft delete** is implemented
> and verified end-to-end. The Angular client has **not** been touched yet. This file is the
> complete contract plus, at the bottom, a ready-to-use prompt for building the UI.
>
> All JSON is camelCase. All endpoints require `Authorization: Bearer <token>` unless noted.
> Errors are RFC7807 problem+json — the human-readable message is in **`detail`**.

---

## 1. Behavioural rules the UI must respect

These are not optional details; the UI will show wrong data if they're ignored.

1. **The server may rename what you send.** A name that collides inside its destination is
   auto-suffixed (`Photos` → `Photos (2)`, `report.pdf` → `report (2).pdf`). This applies to
   folder create/rename/move, file rename/move, **and upload**. Always render the name from the
   response, never the one you sent.
2. **Delete means "move to trash".** `DELETE /api/folders/{id}` and `DELETE /api/media/{id}` are
   instant and reversible for 10 days. Label them "Delete" but make Trash discoverable — nothing
   is actually destroyed until purge.
3. **Trashed items still consume quota.** `usage.trashedBytes` explains why "used" doesn't drop
   after deleting. Surface it next to the meter with an Empty Trash affordance.
4. **Deleting a folder deletes everything inside it**, recursively, in one call. Worth a
   confirmation dialog that names the folder.
5. **Names are unique per folder, case-insensitively.** `beach.jpg` and `BEACH.JPG` collide in
   the same folder; the same name in a different folder does not.
6. **`folderId: null` means the user's root.** It is not an error or a missing value.

---

## 2. New endpoints — Folders

### `GET /api/folders/contents?folderId={guid?}`
The main browse call. Omit `folderId` for the root. Returns the folder, its breadcrumb trail,
its subfolders, and its files in **one** request.

```jsonc
{
  "folder": { "id": "…", "name": "Italy", "parentId": "…",
              "createdAt": "2026-07-22T06:43:47Z", "updatedAt": "…" },   // null at root
  "breadcrumbs": [ { "id": "…", "name": "Photos" },                      // root-first,
                   { "id": "…", "name": "2026" },                        // includes self
                   { "id": "…", "name": "Italy" } ],                     // [] at root
  "folders": [ { "id": "…", "name": "Venice", "parentId": "…", "createdAt": "…", "updatedAt": "…" } ],
  "files":   [ { "id": "…", "originalName": "beach.jpg", "contentType": "image/jpeg",
                 "sizeBytes": 2004, "folderId": "…", "createdAt": "…" } ]
}
```
`404` if the folder doesn't exist, isn't yours, or is in the trash.

### `POST /api/folders`
```jsonc
// request
{ "name": "Photos", "parentId": null }     // parentId omitted/null = create at root
// 201 → FolderItem  (name may be suffixed)
{ "id": "…", "name": "Photos (2)", "parentId": null, "createdAt": "…", "updatedAt": "…" }
```
`404` unknown parent · `409` nesting deeper than 32 levels · `400` invalid name (empty, >255
chars, contains `/` `\` or control characters, or is `.` / `..`).

### `PATCH /api/folders/{id}` — rename
```jsonc
{ "name": "2026 Trips" }        // → FolderItem, name possibly suffixed
```

### `POST /api/folders/{id}/move`
```jsonc
{ "parentId": "…" }             // null = move to root  → FolderItem
```
`409` moving a folder into itself or into one of its own descendants · `409` exceeding depth 32
· `404` unknown destination.

### `DELETE /api/folders/{id}` — move to trash, recursively
`204`. Everything inside goes with it. No confirmation is enforced server-side.

---

## 3. New endpoints — Trash

### `GET /api/trash`
Only items deleted **directly** — a trashed folder appears once, not once per file inside it.

```jsonc
[ { "id": "…", "kind": "folder",      // "folder" | "file"
    "name": "Italy",
    "sizeBytes": 4008,                // for a folder: total bytes held inside it
    "deletedAt": "2026-07-22T06:44:00Z",
    "purgesAt":  "2026-08-01T06:44:00Z" } ]   // server-computed; don't derive it client-side
```
Sorted newest-deleted first.

### `POST /api/trash/{id}/restore`
```jsonc
// → { "id": "…", "name": "beach (2).jpg" }
```
Two things the UI should communicate: the name **may come back suffixed** if something took it
meanwhile, and an item whose original folder was purged **returns to the root**. Show the
returned name.

`409` if the item was deleted as part of a parent — restore the parent instead. `404` if not in
the trash.

### `DELETE /api/trash/{id}` — purge one item permanently
`204`. Irreversible; frees quota immediately. Needs a confirmation dialog.

### `DELETE /api/trash` — empty the trash
`204`. Irreversible.

---

## 4. Changed endpoints

### ⚠️ `POST /api/uploads/init` — request and response both changed
```jsonc
// request — folderId is NEW
{ "originalName": "beach.jpg", "sizeBytes": 2004,
  "contentType": "image/jpeg", "folderId": "…" }      // null = upload to root

// response — originalName and folderId are NEW
{ "mediaId": "…", "key": "…", "uploadId": "…", "partSize": 16777216,
  "originalName": "beach (2).jpg",                    // ← what was ACTUALLY stored
  "folderId": "…" }
```
The upload UI must display `response.originalName`, not the local `File.name` — otherwise a
suffixed upload shows the wrong name for its entire progress bar and afterwards.

Also now returns `404` if `folderId` doesn't exist, and `400` for an invalid filename.
The rest of the multipart flow (`part-url`, `complete`, `abort`) is unchanged.

### ⚠️ `GET /api/media` — new query params, new response field
```
GET /api/media                          → every file, anywhere (flat list; use for search/all-files)
GET /api/media?scoped=true              → files at the ROOT only
GET /api/media?scoped=true&folderId=…   → files in that folder
```
`scoped` exists because "root" and "everywhere" both send no `folderId`. Each item gained
**`folderId`**. Trashed files are excluded automatically.

### ⚠️ `DELETE /api/media/{id}` — semantics changed
Was a permanent delete; is now **move to trash**. Same `204`. Quota is *not* freed until purge.

### ⚠️ `GET /api/me/usage` — new field
```jsonc
{ "quotaBytes": 5368709120, "usedBytes": 6012,
  "remainingBytes": 5368703108, "trashedBytes": 4008 }   // ← NEW
```

### New: `PATCH /api/media/{id}` — rename a file (closes #5)
```jsonc
{ "originalName": "vacation.jpg" }   // → MediaListItem, name possibly suffixed
```

### New: `POST /api/media/{id}/move`
```jsonc
{ "folderId": "…" }                  // null = move to root → MediaListItem
```

---

## 5. Endpoint summary

| Method | Path | Status |
|---|---|---|
| GET | `/api/folders/contents?folderId=` | new |
| POST | `/api/folders` | new |
| PATCH | `/api/folders/{id}` | new |
| POST | `/api/folders/{id}/move` | new |
| DELETE | `/api/folders/{id}` | new (trash, recursive) |
| GET | `/api/trash` | new |
| POST | `/api/trash/{id}/restore` | new |
| DELETE | `/api/trash/{id}` | new (purge) |
| DELETE | `/api/trash` | new (empty) |
| PATCH | `/api/media/{id}` | new (rename) |
| POST | `/api/media/{id}/move` | new |
| GET | `/api/media` | **changed** — `folderId`/`scoped` params, `folderId` in items |
| DELETE | `/api/media/{id}` | **changed** — now trashes instead of destroying |
| POST | `/api/uploads/init` | **changed** — `folderId` in, `originalName`/`folderId` out |
| GET | `/api/me/usage` | **changed** — `trashedBytes` added |
| GET | `/api/media/{id}/download-url` | unchanged |
| POST | `/api/uploads/{id}/part-url\|complete\|abort` | unchanged |

---

## 6. Prompt for the frontend work

> Copy from here down when starting the Angular task.

---

Implement the frontend for folder hierarchy and trash in the Keepr Angular app
(`src/ClientApp`), using the Cove design system already in place. The backend is done and
verified; the full contract is in `docs/api-changes-frontend.md` — read it first, especially
§1, whose rules are the ones most likely to be missed.

**Build:**

1. **Folder browsing.** A file-manager view driven by `GET /api/folders/contents?folderId=`.
   Show subfolders and files together, with a breadcrumb bar from `breadcrumbs` (root-first,
   includes the current folder). Clicking a folder navigates into it; the breadcrumb navigates
   back. Put `folderId` in the route so a folder is linkable and the back button works.
2. **Create folder** — `POST /api/folders` with the current folder as `parentId`. Render the
   name from the response: the server may have suffixed it.
3. **Rename** — `PATCH /api/folders/{id}` and `PATCH /api/media/{id}`. Again, take the name from
   the response.
4. **Move** — `POST /api/folders/{id}/move` and `POST /api/media/{id}/move`, `null` = root.
   Drag-and-drop if it fits the design system; a "Move to…" folder picker is fine otherwise.
   Handle `409` (moving a folder into its own descendant) with the message from `detail`.
5. **Upload into the current folder** — pass `folderId` to `POST /api/uploads/init`, and display
   `response.originalName` for the whole upload, not the local filename.
6. **Trash view** — `GET /api/trash`, listing folders and files together with `kind`, the size,
   and a "purges in N days" derived from `purgesAt`. Actions: Restore
   (`POST /api/trash/{id}/restore`, tell the user the returned name if it differs from what they
   deleted), Delete forever (`DELETE /api/trash/{id}`, confirm first), and Empty trash
   (`DELETE /api/trash`, confirm first).
7. **Quota meter** — extend the existing meter with `trashedBytes`, e.g. "4.2 GB used · 800 MB in
   Trash", linking to the Trash view. This is what answers "I deleted everything and I'm still
   full".
8. **Delete confirmation** for a non-empty folder that names it and says it also deletes the
   contents, plus a note that it's recoverable for 10 days.

**Watch out for:**

- Every write can return a different name than requested — never optimistically render the
  requested name as final.
- `folderId: null` means root, not "missing".
- `GET /api/media` without `scoped=true` returns files from every folder — use it for an
  all-files/search view, not for folder contents.
- Errors are problem+json; show `detail`, which carries a message written for end users.
- `DELETE` on a file or folder is a soft delete; don't word it as permanent.
