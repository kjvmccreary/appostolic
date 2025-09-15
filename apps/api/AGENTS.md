# AGENTS.md — API (dotnet)

This supplements the root AGENTS.md for `apps/api`.

## Migrations

- Always generate the `.Designer.cs` alongside each migration.
- After creating a migration, update the database:
  - From repo root: `make migrate`
  - Ensure the migration and its Designer are checked in.

## Auditing and Guards

- Role changes must go through `Membership.ApplyRoleChange(...)` and persist an `Audit` on change.
- Developer endpoints requiring keys must honor `x-dev-grant-key` when `Dev:GrantRolesKey` is set.
- Keep endpoints tenant-scoped and avoid environment-gated route registration that breaks tests.

## Quality Gates

- Build: `dotnet build appostolic.sln` must be clean.
- Tests: run API tests (`apps/api.tests`) and keep them green.
- Prefer tracked EF mutations over provider-specific update APIs to remain compatible with InMemory provider.

## Documentation

- Update `SnapshotArchitecture.md` when endpoints, DB schema, or security behavior changes.
- At the end of each story, append `devInfo/storyLog.md` with the same summary used in the assistant message.
