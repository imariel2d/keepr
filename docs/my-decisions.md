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

### Q6 — Post-processing ✅
- **Decision:** **None for MVP.** Store files as-is. No thumbnails, transcoding, or EXIF stripping yet. Revisit after MVP (transcoding will need a separate worker since App Platform is stateless).

### Q7 — Multi-tenancy & quotas ✅
- **Decision:** **Per-user quotas, default 5 GB per user.** Single-tenant users (no orgs for now). Quota is reserved at upload start and reconciled to actual bytes on completion; freed on delete.

### Q8 — Delivery / CDN ✅
- **Decision:** **Private bucket + presigned GET.** No public objects. Downloads/views are served via short-TTL presigned GET URLs minted by the API for the authenticated owner.

### Q9 — Retention & deletion ✅
- **Decision:** **Hard delete.** Deleting a file immediately `DeleteObject`s from R2, removes the DB row, and frees the quota. No trash/restore for MVP. Orphan/abandoned-upload sweeper still applies (D-cleanup).
- Upgrade path: a soft-delete/trash layer can be added later by introducing a `deleted_at` column — no painful migration.

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

<!--
Example:
- 2026-07-19 — Q1 Auth: JWT with our own login. Reason: no external IdP dependency for MVP.
-->

---

## Things I want that override the AI
_(Anywhere I disagree with ai-design-decisions.md, write it here.)_

- _(none yet)_
