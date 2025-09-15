# Copilot Instructions for this Repository

These instructions complement `AGENTS.md` and nested `AGENTS.md` files.

## Core rules (root)

- Keep `SnapshotArchitecture.md` up to date at the end of each story.
- For .NET migrations, include the `.Designer.cs` file and run `make migrate`.
- Append `devInfo/storyLog.md` with the same summary used in the assistant output.
- Commit with a clear message and sync after each story.
- Mark sprint stories ✅ DONE when completed.
- Add code comments above new methods/controllers/functions (especially complex blocks).

## Nested scopes

- API (`apps/api/AGENTS.md`): migrations, auditing & guards, EF compatibility, quality gates.
- Web (`apps/web/AGENTS.md`): Next.js tasks, navigation accessibility, coverage gates, docs.

## Conventions

- Prefer server-first authorization; UI mirrors session flags.
- Favor deterministic tests; keep coverage thresholds satisfied.
- Avoid environment-gated route registration that hides endpoints in tests.

For detailed, scoped guidance, see `AGENTS.md` at the repo root and within the relevant subfolders.
