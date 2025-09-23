# Copilot Instructions for this Repository

These instructions complement `AGENTS.md` and nested `AGENTS.md` files.

## Core rules (root)

- Keep `SnapshotArchitecture.md` up to date at the end of each story. Do not add information about the story, however. Only update that file if any of the architectural elements of the system have changed.
- For .NET migrations, include the `.Designer.cs` file and run `make migrate`.
- Append `devInfo/storyLog.md` with the same summary used in the assistant output.
- Mark stories as 🚧 IN PROGRESS once you begin work on them.
- If a single story requires multiple phases, mark each phase ✅ DONE when completed
- Commit with a clear message and sync after each story.
- Mark sprint stories ✅ DONE when completed in the document serving as the SprintPlan.
- Add code comments above new methods/controllers/functions (especially complex blocks).

## Sprint Plans

- If using a Sprint Plan for a sprint (e.g., `rdhSprintPlan.md`), be sure to mark stories as ✅ DONE when completed. If a story has multiple line items, use checkboxes to track progress. The sprint plans will follow this naming convention: `^[a-zA-Z0-9]{3}SprintPlan\.md$`

### Living Checklist

- Maintain the live 1.0 readiness checklist at `devInfo/LivingChecklist.md`.
- On story/sprint closure, update the checklist (tick items, add notes/links, or move to Post‑1.0), update `SnapshotArchitecture.md`, append to `devInfo/storyLog.md`, then commit and sync.

## Nested scopes

- API (`apps/api/AGENTS.md`): migrations, auditing & guards, EF compatibility, quality gates.
- Web (`apps/web/AGENTS.md`): Next.js tasks, navigation accessibility, coverage gates, docs.

## Conventions

- Prefer server-first authorization; UI mirrors session flags.
- Favor deterministic tests; keep coverage thresholds satisfied.
- Avoid environment-gated route registration that hides endpoints in tests.

For detailed, scoped guidance, see `AGENTS.md` at the repo root and within the relevant subfolders.
