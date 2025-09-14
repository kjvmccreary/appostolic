# Notifications Worker

A standalone .NET worker that hosts the notifications runtime (outbox dispatcher, optional Redis subscriber) outside the API process.

## Why

- Decouple the dispatcher from the API so it can scale independently and be restarted without impacting HTTP traffic.
- Prepare for external transports/brokers while keeping a simple default.

## How it works

- Reuses the API's notifications DI via `AddNotificationsRuntime(...)`.
- Runs EF auto-migrations in Development/Test to keep the schema up-to-date locally.
- Honors the same configuration keys as the API:
  - `Notifications:Transport:Mode` = `channel` (default) or `redis`
  - `Notifications:Runtime:RunDispatcher` (default true)
  - `Email:*`, `SendGrid:*`, `Smtp:*`
  - Postgres env vars: `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`

## Run locally

This project references the API project for shared types and services.

- Ensure Postgres is up (e.g., `make up`), then run the worker:

```bash
# From repo root
dotnet run --project apps/notifications-worker/Appostolic.Notifications.Worker.csproj
```

To disable the API's internal dispatcher and let the worker own it, set:

```bash
export Notifications__Runtime__RunDispatcher=false
```

When using Redis transport:

```bash
export Notifications__Transport__Mode=redis
export Notifications__Transport__Redis__Host=127.0.0.1
export Notifications__Transport__Redis__Port=6380
# optional
# export Notifications__Transport__Redis__Password=...
```

## Notes

- The worker will not host HTTP endpoints; it only runs background services (dispatcher, purge, auto-resend). Use the API's dev diagnostics endpoints for health/ping.
