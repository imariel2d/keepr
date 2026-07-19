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
    media.service.ts    list / download-url / delete / usage
    bytes.pipe.ts       human-readable sizes
  features/
    login/              register + sign in
    home/               quota meter, uploader, file library
```

## How uploads work (matches the API)
1. `POST /api/uploads/init { originalName, sizeBytes, contentType }` → `{ mediaId, uploadId, partSize }`
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
