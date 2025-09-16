# AGENTS.md — Web (Next.js)

This supplements the root AGENTS.md for `apps/web`.

## Tasks

- Typecheck: `pnpm -C apps/web typecheck`
- Unit tests: `pnpm -C apps/web test`
- Dev server: `pnpm -C apps/web dev`

## Navigation & Accessibility

- Top Bar and Nav Drawer must:
  - Mark the active link with `aria-current="page"`.
  - Provide a mobile hamburger that opens an accessible drawer.
  - Drawer behavior: focus trap while open, close on ESC and backdrop click, auto-close on route change.
  - Preserve server-first guards; UI visibility must mirror session-derived flags.

## Quality Gates

- Keep unit tests passing; maintain existing coverage thresholds.
- Avoid regressions to TenantSwitcher behavior on public vs protected routes.

## Runtime & Testing Environment

- Always run vitest and Next.js dev commands with Node 20.x LTS. Using Node 19 can trigger a Corepack
  failure (`TypeError: URL.canParse is not a function` coming from `corepack.cjs`) when invoking
  `pnpm test` at the workspace root.
- Recommended local workflow with `nvm`:
  1. `nvm install 20 && nvm use 20`
  2. Verify: `node -v` should show `v20.*`.
  3. Run tests from workspace root or package:
     - Workspace: `PATH="$(nvm which 20 | xargs dirname):$PATH" pnpm -w -s test -w --filter @appostolic/web`
     - Package: `pnpm -C apps/web test`
- CI should pin Node 20; if adding a new CI job, mirror this requirement.
- Symptom checklist if the wrong Node version is used:
  - Immediate crash before any test output with `URL.canParse` error
  - No individual test file results printed
  - Exit code 1 without coverage summary
    Resolve by switching to Node 20 and re-running.

## Documentation

- Update `devInfo/navDesign/navSprintPlan.md` as stories progress; mark stories ✅ DONE when completed.
- Append `devInfo/storyLog.md` with the same summary used in the assistant message at the end of each story.
