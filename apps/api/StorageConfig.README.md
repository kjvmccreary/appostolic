# Storage Configuration (Development & Production)

This file documents how to configure object storage for avatars/logos (and future lesson artifacts) using the `IObjectStorageService` seam.

## Modes

- `local` (default): Uses `LocalFileStorageService` writing files under a media folder (defaults to `apps/web/web.out/media` relative to the application base directory). URLs returned are relative like `/media/users/<id>/avatar.png`.
- `s3`: Uses `S3ObjectStorageService` backed by AWS S3 or a MinIO-compatible endpoint.

## Minimal `appsettings.Development.json` (local)

```json
{
  "Storage": {
    "Mode": "local",
    "Local": {}
  }
}
```

## Example MinIO (S3-compatible) settings (switch Mode to `s3`)

```jsonc
{
  "Storage": {
    "Mode": "s3",
    "S3": {
      "Bucket": "appostolic-dev",
      "ServiceURL": "http://localhost:9000", // MinIO endpoint from docker compose
      "AccessKey": "minioadmin",
      "SecretKey": "minioadmin",
      "PathStyle": true,
      "PublicBaseUrl": "http://localhost:9000/appostolic-dev",
      "DefaultCacheControl": "public, max-age=31536000, immutable",
    },
  },
}
```

`PublicBaseUrl` is optional; if omitted, URLs fall back to `https://<bucket>.s3.<region>.amazonaws.com/<key>` using the configured region.

## Production (AWS S3) typical

Set credentials via environment (preferred) and keep them out of static JSON:

```jsonc
{
  "Storage": {
    "Mode": "s3",
    "S3": {
      "Bucket": "appostolic-prod-avatars",
      "RegionEndpoint": "us-east-1",
      "PathStyle": false,
      "DefaultCacheControl": "public, max-age=31536000, immutable",
    },
  },
}
```

Environment variables (example):

```
Storage__S3__AccessKey=AKIA...
Storage__S3__SecretKey=...
```

## Future extensions

- Signed URL generation for time-limited access (lesson artifacts / private assets)
- Delete lifecycle (`DeleteAsync`) with soft-delete or retention window
- Optional CDN front (CloudFront) via `PublicBaseUrl`

---

Last updated: 2025-09-16
