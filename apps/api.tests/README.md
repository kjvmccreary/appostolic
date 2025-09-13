# Appostolic.Api.Tests

Run all tests from repo root:

```
dotnet test
```

Or just this project:

```
dotnet test apps/api.tests/Appostolic.Api.Tests.csproj
```

## Coverage notes

- Includes end-to-end tests for Agents and AgentTasks.
- Email pipeline tests (queue, dispatcher, providers).
- New: Invites lifecycle tests under `Api/InvitesEndpointsTests.cs` covering:
  - Create invite: `POST /api/tenants/{tenantId}/invites`
  - List invites: `GET /api/tenants/{tenantId}/invites`
  - Resend invite: `POST /api/tenants/{tenantId}/invites/{email}/resend`
  - Accept via signup: `POST /api/auth/signup` with `inviteToken`
  - Revoke invite: `DELETE /api/tenants/{tenantId}/invites/{email}`
