# API E2E Tests (HTTPS Secure Cookie)

Purpose: real Kestrel HTTPS end-to-end tests validating Secure/HttpOnly refresh cookie attributes (Story 5b).

Run prerequisites:

1. Trust dev cert (one time): `dotnet dev-certs https --trust`
2. Launch tests: `dotnet test apps/api.e2e`

Environment variables:

- PORT (optional): specify HTTPS port (default 5199) when using the Makefile target `api-https-test` manually.

Test strategy:

- Spin up full API via standard Program.cs under HTTPS.
- Execute auth flow (login) and inspect Set-Cookie headers.

Future enhancements:

- Browser-based Playwright verification.
- Additional flows (refresh, logout) under HTTPS.
