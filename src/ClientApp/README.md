# ClientApp (Angular SPA)

Angular 21, standalone components, signals, TypeScript strict. Talks to the .NET API over
relative `/api` URLs (same-origin in prod; dev-proxied to the API).

## Run in dev
```bash
# terminal 1 — API on :5080
dotnet run --project ../Api

# terminal 2 — SPA on :4200, proxying /api -> :5080
npm start        # ng serve --proxy-config proxy.conf.json
```

## Structure
```
src/app/
  core/                 services + cross-cutting
    models.ts           API contract types
    auth.service.ts     register/login, JWT in localStorage (signals)
    auth.interceptor.ts attaches Bearer to /api calls only
    auth.guard.ts       route guard
    upload.service.ts   presigned S3 multipart engine
    media.service.ts    list / download-url / rename / move / trash / usage
    folder.service.ts   folder contents / create / rename / move / delete
    trash.service.ts    trash list / restore / purge / empty
    usage.store.ts      shared quota signal for the sidebar meter
    file-type.ts        MIME -> Cove file-type + date formatting
    bytes.pipe.ts       human-readable sizes
  features/
    login/              register + sign in
    files/              folder browser: breadcrumbs, cards, upload, rename/move/delete
      move-dialog.ts    "Move to..." destination picker
    trash/              trashed items, restore, purge, empty
```

## How uploads work (matches the API)
1. `POST /api/uploads/init { originalName, sizeBytes, contentType, folderId }` →
   `{ mediaId, uploadId, partSize, originalName }`
2. For each `partSize` slice: `GET /api/uploads/{id}/part-url?partNumber=N`, then **`fetch` PUT**
   the slice straight to storage and read the `ETag` response header. `fetch` (not HttpClient)
   is used so the auth interceptor never adds an `Authorization` header to the signed URL.
3. `POST /api/uploads/{id}/complete { parts: [{ partNumber, eTag }] }`
4. On error: `POST /api/uploads/{id}/abort` to release reserved quota.

> ⚠️ **Bucket CORS must expose the `ETag` header** (Access-Control-Expose-Headers) and allow
> `PUT` from the app origin, or step 2 can't read the ETag and completion fails.

## Build for production
`npm run build` → `dist/ClientApp/browser/`. The root [Dockerfile](../../Dockerfile) copies that
directory into the API's `wwwroot` so the SPA ships inside the same image.

## Three API behaviours the UI depends on

Full contract: [docs/api-changes-frontend.md](../../docs/api-changes-frontend.md).

1. **The server may store a different name than you sent.** Collisions inside a folder are
   auto-suffixed (`report.pdf` -> `report (2).pdf`) on create, rename, move, *and upload* —
   never rejected. Always render the name from the response. The upload progress row is patched
   from `init.originalName` for exactly this reason.
2. **Delete means "move to trash".** Items are recoverable for 10 days and keep consuming quota
   until purged, which is why the sidebar shows a separate "in Trash" line.
3. **`folderId: null` is the root**, not a missing value.

## Running against the dockerized API

`docker compose -f docker-compose.yml -f docker-compose.api.yml up -d` publishes the API on
:5080, which `proxy.conf.json` already targets — so `npm start` works against it.

⚠️ Browser uploads will **not** work in that configuration: the API mints presigned URLs for
`http://minio:9000`, a hostname that only resolves inside the Docker network. Run the API on the
host (`dotnet run --project ../Api`, which uses `Storage__ServiceUrl=http://localhost:9000`) when
testing uploads from the browser.
