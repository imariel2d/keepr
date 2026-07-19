# AI Design Decisions — Media Upload Service

> Author: Claude (AI). These are **recommendations with rationale**, not final law.
> The human's overriding choices live in [my-decisions.md](my-decisions.md).
> Last updated: 2026-07-19

## 1. Context & constraints

| Fact | Implication |
|------|-------------|
| **Monolith** | One deployable unit. Angular is built and served by (or alongside) the ASP.NET Core app. |
| **DigitalOcean App Platform** | Containers have an **ephemeral, read-only-ish filesystem**. Local disk is wiped on every deploy/restart/scale event. → **Files must NOT be stored on the container disk.** |
| **Docker** | We ship one image. Multi-stage build: Angular → .NET publish → runtime. |
| **C# + Angular** | ASP.NET Core (LTS) backend, Angular SPA frontend. |
| **Media files** | Potentially large, binary, need validation, and are the core domain. |

The single most important consequence: **App Platform is stateless**, so persistent storage is an external concern (object storage + a database for metadata).

## 2. High-level architecture

```
┌─────────────────────────────────────────────────────────┐
│  DigitalOcean App Platform (one container image)         │
│                                                          │
│   Angular SPA  ──(served as static files)──►  Browser    │
│        │                                                 │
│        │  REST/JSON (same origin)                        │
│        ▼                                                 │
│   ASP.NET Core API  ──► metadata ──►  Managed Postgres   │
│        │                                                 │
│        │  presigned PUT/GET (bytes bypass the app)       │
│        ▼                                                 │
└────────┼─────────────────────────────────────────────────┘
         ▼
   Cloudflare R2 (S3-compatible, zero egress)  ◄── browser uploads bytes directly
         ▲
         └── Cloudflare CDN / custom domain for delivery
```

## 3. Key decisions

### D1 — Object storage: **Cloudflare R2 (S3-compatible)** ✅ decided by human
- **Why:** App Platform disks are ephemeral, so storage must be external. R2 is S3-compatible and its headline feature is **zero egress fees** — decisive for media delivery, where bytes-out is the dominant cost on most providers. Cloudflare's CDN sits in front natively.
- **How:** Use the AWS SDK for .NET (`AWSSDK.S3`) pointed at the R2 S3 endpoint (`https://<accountid>.r2.cloudflarestorage.com`), region `auto`. R2 supports **presigned PUT/GET** via the S3 API, so the direct-to-storage upload flow (D2) works unchanged.
- **Cross-cloud note:** app runs on DO App Platform, storage is on Cloudflare. Uploads go **browser → R2 directly** (presigned), so app-to-R2 traffic is minimal. Configure **R2 bucket CORS** to allow the App Platform origin.
- **Delivery:** private bucket + presigned GET, or a **public bucket bound to a custom domain** through Cloudflare (with signed URLs / Access if needed). Zero egress makes CDN delivery cheap.
- **Alternative rejected:** DO Spaces — charges egress; R2 does not.

### D2 — Upload path: **presigned S3 multipart (direct-to-R2)** ✅ decided by human
- **Why:** Streaming large media *through* the .NET app burns App Platform CPU/memory/bandwidth and risks request-timeout limits. Presigned URLs let the browser PUT bytes straight to R2. **Multipart** adds resumability and handles large files on flaky networks.
- **Flow (multipart):**
  1. Client asks API: "I want to upload `video.mp4`, 200 MB." API **checks the user's remaining quota** (see D9), and if OK creates a `pending` `media_files` row + calls R2 `CreateMultipartUpload`, returning the `uploadId` and object key.
  2. Client splits the file into parts (e.g. 8–16 MB each). For each part it asks the API for a **presigned `UploadPart` URL** (or a batch of them) and PUTs the bytes directly to R2. Each successful part returns an `ETag`.
  3. Client calls `POST /uploads/{id}/complete` with the ordered list of `{partNumber, eTag}`. API calls R2 `CompleteMultipartUpload`, HEADs the object to get the **actual size**, sniffs magic bytes, **reconciles the user's quota to real bytes**, and flips status to `ready`.
  4. On abandonment/failure: `AbortMultipartUpload` releases the parts; the reserved quota is freed. A sweeper (D-cleanup) mops up stragglers.
- **Small files:** the same multipart machinery works, or a single presigned `PutObject` may be used under a threshold — an implementation detail, not a separate architecture.
- **Resumability:** because `uploadId` + completed part ETags are known, an interrupted upload can resume by re-requesting presigned URLs only for the missing parts.

### D3 — Metadata store: **DigitalOcean Managed PostgreSQL**
- **Why:** We need durable, queryable records (owner, key, size, mime, status, timestamps, checksum). Managed = automated backups + connection pooling. Postgres over MySQL for JSONB, strong constraints, and general ecosystem fit.
- **Core table sketch:**
  ```
  users(
    id uuid pk,
    email text unique,
    password_hash text,
    quota_bytes bigint default 5368709120,   -- 5 GB
    used_bytes  bigint default 0,             -- maintained transactionally
    created_at timestamptz
  )

  media_files(
    id uuid pk,
    owner_id uuid references users(id),
    storage_key text unique,
    original_name text,
    content_type text,
    size_bytes bigint,          -- reserved estimate while pending, actual once ready
    checksum_sha256 text,
    multipart_upload_id text,   -- R2 uploadId while in flight, null once complete
    status text check (status in ('pending','ready','failed','deleted')),
    created_at timestamptz,
    updated_at timestamptz
  )
  ```
- `used_bytes` on `users` is the fast path for the "space remaining" UI; it's kept in sync with `media_files` inside the same transaction that flips status (see D9).

### D4 — Frontend delivery: **Angular built into the same image, served as static assets**
- **Why:** True monolith, single origin → no CORS between SPA and API (CORS is still needed for the *Spaces* PUT — configure Spaces CORS to allow the app origin).
- **How:** Multi-stage Docker build. Angular `dist/` is copied into `wwwroot/`; ASP.NET Core serves it with SPA fallback routing (`MapFallbackToFile("index.html")`).

### D5 — Backend shape: **layered monolith, upload as a bounded module**
- Controllers → Application services → Infrastructure (Storage + Persistence).
- Keep storage behind an `IObjectStorage` interface so Spaces can be swapped for MinIO in local dev.
- Not microservices — the whole point is a monolith. But keep the upload concern cohesive so it *could* be extracted later.

### D6 — Validation & safety (defense in depth)
- **Quota, not per-file cap:** the primary gate is the user's remaining quota (D9), checked *before* presigning. A generous per-file ceiling may still be wise to bound multipart part counts — revisit if abused.
- **Type:** **any media type is allowed** (human decision — no MIME allow-list). The sniffed content-type is recorded via **magic-byte inspection** on `complete` (implemented in `Services/ContentTypeSniffer.cs`; the controller reads the object's first bytes from storage and overrides the declared type) for correct serving and to detect obviously-mislabeled payloads; the client's declared content-type/extension is never trusted, only used as a fallback when the signature is unknown.
- **Naming:** never use the user's filename as the storage key. Generate keys like `{ownerId}/{uuid}{ext}`. Keep original name only as metadata.
- **Access:** private bucket by default; serve downloads via **presigned GET** (or Cloudflare CDN with signed URLs). No public-by-default objects.
- **Virus/content scanning:** open — see [my-decisions.md](my-decisions.md) Q5. Note "any type + no scanning" is a real risk surface if uploads are ever shared between users.

### D7 — Local development: **MinIO + Postgres via docker-compose**
- **Why:** MinIO is S3-compatible, so the same `IObjectStorage` code runs locally without touching real R2. No cloud credentials needed to develop.

### D8 — Config & secrets
- All secrets (R2 access key/secret + account ID, DB connection string) via **environment variables / App Platform secrets** — never committed. Local dev uses `.env` / user-secrets.

### D9 — Quota accounting: **reserve-on-start, reconcile-on-complete** ✅ driven by human decision
- **Default quota:** 5 GB per user (`users.quota_bytes`).
- **Reserve on start:** when an upload begins, add the *declared* size to `used_bytes` (or a separate `reserved_bytes` column) inside a transaction that also asserts `used_bytes + declared <= quota_bytes`. Reject with `413`/`409` if it would exceed.
- **Reconcile on complete:** after `CompleteMultipartUpload`, HEAD the object for the true size and adjust `used_bytes` by `(actual − declared)`. This stops clients lying about size to dodge the quota.
- **Release on abort/delete:** aborting or deleting subtracts the bytes back.
- **Source of truth vs. cache:** `media_files` (rows in `ready`) is the ledger; `users.used_bytes` is a maintained running total for O(1) "space remaining". A periodic job can recompute `used_bytes = SUM(size_bytes)` to correct any drift.
- **Concurrency:** all quota mutations happen in a DB transaction with row locking on the `users` row to avoid two parallel uploads both "fitting".
- **UI:** `GET /me/usage` returns `{ quota_bytes, used_bytes, remaining_bytes }` for the always-visible meter.

### D-cleanup — Orphan / abandoned-upload sweeper ✅ implemented
- Multipart makes abandonment more likely (client closes mid-upload). A scheduled task finds `pending` `media_files` older than N hours, calls R2 `AbortMultipartUpload`, frees reserved quota, and marks them `failed`.
- **Implemented** as an in-process `BackgroundService` (`Services/UploadCleanupService.cs`) on a `PeriodicTimer`, configured by the `Cleanup` section (`IntervalMinutes`, `PendingMaxAgeHours`).
- Single-instance safe; if scaled out, move to a dedicated worker or add a lease/lock so only one instance sweeps.
- Still TODO: also list-and-abort dangling R2 multipart uploads that have *no* matching row (belt-and-suspenders).

## 4. Proposed repository layout

```
keepr/
├─ docs/
│  ├─ ai-design-decisions.md   ← this file
│  └─ my-decisions.md
├─ src/
│  ├─ Api/                      ← ASP.NET Core (also serves Angular)
│  └─ ClientApp/               ← Angular SPA
├─ Dockerfile                  ← multi-stage
├─ docker-compose.yml          ← local: api + postgres + minio
└─ .do/app.yaml                ← App Platform spec (optional, IaC)
```

## 5. Recommended stack versions (verify latest LTS before locking)
- **.NET:** current LTS (e.g. .NET 8/9 LTS) — pin in `global.json`.
- **Angular:** current stable major.
- **Postgres:** 16.x on DO Managed DB.
- **Node:** LTS for the Angular build stage only.

> ⚠️ I did not fetch live version numbers. Confirm the newest LTS at build time.

## 6. Decisions status (see [my-decisions.md](my-decisions.md))
- ✅ Q1 Auth — **own JWT/session**
- ✅ Q2 Types/size — **any type, per-user 5 GB quota** (no per-file allow-list)
- ✅ Q3 Upload path — **presigned direct-to-R2**
- ✅ Q4 Resumable — **yes, S3 multipart**
- ✅ Q5 Virus/content scanning — **none for MVP** (revisit before sharing ships)
- ✅ Q6 Post-processing — **none for MVP**
- ✅ Q7 Quotas — **per-user, 5 GB default**
- ✅ Q8 Delivery — **private bucket + presigned GET** (no public objects)
- ✅ Q9 Retention/deletion — **hard delete** (orphan sweeper still applies, D-cleanup)

> **Design is fully specified for MVP.** All nine questions resolved. Next step is scaffolding.

### Future (post-MVP) — planned, not built
- **File sharing between users.** This is the trigger that will require revisiting Q5: once a file can reach someone other than its uploader, **malware scanning + content moderation (incl. CSAM detection)** become required, and delivery/authorization logic grows (per-file grants, share links). Keep storage keyed by owner and access checks centralized so this can be added cleanly.

## 7. Risks & watch-items
- **AWS SDK v4 presign gotchas (found in end-to-end testing, fixed):**
  1. `AWSSDK.S3` **4.0.0** throws `NullReferenceException` in the endpoint resolver on *any*
     presign call. Fixed by pinning **4.0.101.3+**. Don't downgrade.
  2. The v4 endpoint resolver emits **https** presigned URLs for a custom endpoint even when
     `UseHttp` is set, which breaks against plain-HTTP MinIO in dev. `R2ObjectStorage.MatchScheme`
     rewrites the scheme to match the configured `ServiceUrl`. R2 (https) is unaffected.
- **App Platform request timeout** — reinforces the direct-to-Spaces decision.
- **Orphaned objects** — an upload that reaches Spaces but never calls `complete`. Mitigate with a lifecycle rule or a sweeper job that deletes `pending` rows/objects older than N hours.
- **Cost** — Spaces egress + CDN; large media adds up. Track early.
- **CORS on Spaces** — the browser-direct PUT will fail silently-ish until Spaces CORS allows the app origin, method, and headers.
