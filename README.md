# Keepr

Media upload service — **ASP.NET Core + Angular monolith**, deployed on **DigitalOcean App
Platform** via Docker, with files in **Cloudflare R2** (S3-compatible, zero egress).

Design rationale lives in [docs/ai-design-decisions.md](docs/ai-design-decisions.md); the
human decisions that govern it are in [docs/my-decisions.md](docs/my-decisions.md).

## Feature summary (MVP)
- Own JWT auth (register / login)
- Any media type, gated by a **per-user 5 GB quota** with a live "space remaining" figure
- **Presigned S3 multipart** uploads: bytes go browser → R2 directly, resumable
- Private storage; downloads via short-TTL presigned GET
- Hard delete; no scanning or post-processing yet (both deferred — see the docs)

## Layout
```
src/Api/         ASP.NET Core API (also serves the Angular SPA from wwwroot)
  Domain/        User, MediaFile, MediaStatus
  Data/          AppDbContext + Migrations
  Storage/       IObjectStorage + R2/S3 implementation
  Features/      Auth, Uploads, Media, Me controllers
  Services/      Jwt, QuotaService
src/ClientApp/   Angular SPA (to be generated — see its README)
Dockerfile       Multi-stage: Angular -> .NET -> runtime
docker-compose.yml  Local Postgres + MinIO
.do/app.yaml     App Platform spec
```

## Run locally
```bash
# 1. Start Postgres + MinIO (S3 stand-in) and create the bucket
docker compose up -d

# 2. Run the API (applies EF migrations on startup)
dotnet run --project src/Api
#    Dev config (appsettings.Development.json) points storage at MinIO on :9000.

# health check
curl http://localhost:5xxx/health   # port shown in console output
```

MinIO console: http://localhost:9001 (minioadmin / minioadmin).

## API quick reference
| Method | Route | Purpose |
|--------|-------|---------|
| POST | `/api/auth/register` · `/api/auth/login` | get a JWT |
| GET  | `/api/me/usage` | quota / used / remaining bytes |
| POST | `/api/uploads/init` | reserve quota + open multipart upload |
| GET  | `/api/uploads/{id}/part-url?partNumber=N` | presigned PUT for a part |
| POST | `/api/uploads/{id}/complete` | assemble parts, reconcile quota |
| POST | `/api/uploads/{id}/abort` | cancel, release quota |
| GET  | `/api/media` | list your files |
| GET  | `/api/media/{id}/download-url` | presigned GET |
| DELETE | `/api/media/{id}` | hard delete |

## Deploy (DigitalOcean App Platform)
1. Push to a GitHub repo; set `github.repo` in [.do/app.yaml](.do/app.yaml).
2. Set secrets (JWT key, R2 credentials, DB connection) as App Platform SECRET env vars.
3. `doctl apps create --spec .do/app.yaml` (or connect the repo in the UI).
4. **Set R2 bucket CORS** using [docs/r2-cors.json](docs/r2-cors.json) (replace `YOUR-APP-DOMAIN`).
   It allows `PUT`/`GET` from your origin and — critically — exposes the `ETag` header the
   multipart client must read. Without this, uploads fail. (Dev/MinIO CORS is already wired in
   `docker-compose.yml`.)

## Not built yet (tracked in the docs)
- Resumable-across-reloads: the client re-uploads from scratch on failure (the API already
  supports resuming, since `uploadId` + part ETags persist)
- Future: file sharing → then malware scan + content moderation become required (Q5)

## Frontend
The Angular SPA lives in [src/ClientApp](src/ClientApp) (Angular 21, standalone, signals).
See its README for the dev proxy setup and the client-side multipart flow.
