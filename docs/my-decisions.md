# My Decisions — Media Upload Service

> Author: **Ariel** (the human). This file **overrides** anything in
> [ai-design-decisions.md](ai-design-decisions.md) where they conflict.
> Fill in the `Decision:` lines. Anything left as _TBD_ means the AI recommendation stands for now.

---

## Locked-in facts (given)
- Language/stack: **C# (ASP.NET Core) + Angular**
- Shape: **Monolith**
- Hosting: **DigitalOcean App Platform**, deployed **via Docker**

---

## Open questions to decide

### Q1 — Authentication / authorization ✅
- **Decision:** **Own JWT / session auth** built into the ASP.NET Core app. No external IdP.

### Q2 — File size cap & allowed media types ✅
- **Decision:** **Any media type allowed** (no type allow-list restriction). Instead of a per-file cap, enforce a **per-user storage quota, default 5 GB**. Every upload checks remaining space first; the UI always shows how much the user has left.
- Implication: quota accounting is a core feature (see Q7). A sane per-file ceiling may still be needed to bound multipart complexity — _revisit if abuse shows up_.

### Q3 — Upload path threshold
AI recommends **presigned direct-to-R2** for large files, optional **proxy** for small ones.
- **Decision:** presigned direct-to-R2 for everything (consistent with multipart choice in Q4). _Confirmed by implication._

### Q4 — Resumable / chunked uploads ✅
- **Decision:** **Yes — S3 multipart uploads.** Chunked + resumable. Client splits large files into parts, each part gets a presigned URL, then a `complete-multipart` call assembles them.

### Q5 — Virus scanning / content moderation ✅
- **Decision:** **No scanning for MVP.** Keep only cheap magic-byte structural validation (D6).
- ⚠️ **Future:** **users WILL be able to share files with each other.** Once sharing ships, malware scanning *and* content moderation (CSAM/illegal content) become important — legal exposure, not just security. Revisit before the sharing feature launches. Storage stays private-per-owner until then.
- 📐 **2026-07-24 — scoped exception for shareable links (#7).** [shareable-links-design.md](shareable-links-design.md) §2 accepts this risk *only* for single-owner link sharing (the owner shares their own self-uploaded files), bounded by mandatory expiry, revoke, and a global kill-switch. This exception is **scoped, not reusable**: scanning + moderation remain a hard prerequisite for #6 (multi-user sharing), where untrusted uploads enter.

### Q6 — Post-processing ✅
- **Decision:** **None for MVP.** Store files as-is. No thumbnails, transcoding, or EXIF stripping yet. Revisit after MVP (transcoding will need a separate worker since App Platform is stateless).

### Q7 — Multi-tenancy & quotas ✅
- **Decision:** **Per-user quotas, default 5 GB per user.** Single-tenant users (no orgs for now). Quota is reserved at upload start and reconciled to actual bytes on completion; freed on delete.

### Q8 — Delivery / CDN ✅
- **Decision:** **Private bucket + presigned GET.** No public objects. Downloads/views are served via short-TTL presigned GET URLs minted by the API for the authenticated owner.

### Q9 — Retention & deletion ✅ **superseded 2026-07-21**
- ~~**Decision:** **Hard delete.**~~ **Now: soft delete + 10-day trash.** Deleting moves the item
  to Trash (a `DeletedAt` stamp, no R2 call); a background sweeper permanently purges after
  10 days, freeing quota then. Trashed bytes still count against quota until purge.
  See [trash-soft-delete-design.md](trash-soft-delete-design.md).
- The original hard-delete note called this exact upgrade path — "a soft-delete/trash layer can
  be added later by introducing a `deleted_at` column, no painful migration" — and that held:
  the migration is two nullable columns per table.
- Orphan/abandoned-upload sweeper (D-cleanup) is unchanged and still separate; it handles
  `Pending` uploads, which is a different failure mode from user deletion.

---

## Decisions I've already made
_(Record confirmed choices here as we go.)_

- 2026-07-19 — **Object storage: Cloudflare R2** (S3-compatible, zero egress). Overrides the AI's DO Spaces suggestion. Access via AWS SDK for .NET against the R2 S3 endpoint; browser-direct presigned uploads; Cloudflare CDN for delivery.
- 2026-07-19 — **Auth: own JWT/session** (no external IdP).
- 2026-07-19 — **Any media type**, no per-file allow-list; enforce **per-user 5 GB quota** with live "space remaining" in the UI.
- 2026-07-19 — **Multipart/resumable uploads** (S3 multipart API).
- 2026-07-19 — **No post-processing for MVP** (files stored as-is).
- 2026-07-19 — **Delivery: private bucket + presigned GET** (Q8). No public objects.
- 2026-07-19 — **No virus/content scanning for MVP** (Q5); revisit before the future **file-sharing** feature ships.
- 2026-07-19 — **Hard delete** (Q9). No trash/restore for MVP.
- 2026-07-21 — **Folder name collisions auto-suffix** (`Photos (2)`), never 409. Applies to
  folder create/move and to file upload/rename. See
  [folder-hierarchy-design.md](folder-hierarchy-design.md) Q-A/§4.0.
- 2026-07-21 — **No duplicate file names within a folder** (Dropbox-style, not Drive-style).
  Enforced by a partial unique index; collisions auto-suffix per the decision above. Requires
  narrowing `OriginalName` to 255 chars and de-duplicating existing rows at migration time —
  see folder-hierarchy-design.md Q-B/§5.
- 2026-07-21 — **Soft delete with a 10-day trash, replacing hard delete (overrides Q9).**
  Delete → Trash → purged permanently after 10 days. Makes recursive folder delete safe, so it
  ships in the first folder release. See [trash-soft-delete-design.md](trash-soft-delete-design.md).
- 2026-07-21 — **Folder depth capped at 32** (`Path` = `varchar(1200)`).

<!--
Example:
- 2026-07-19 — Q1 Auth: JWT with our own login. Reason: no external IdP dependency for MVP.
-->

---

## Things I want that override the AI
_(Anywhere I disagree with ai-design-decisions.md, write it here.)_

- _(none yet)_
