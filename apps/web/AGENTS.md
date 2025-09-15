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

## Documentation

- Update `devInfo/navDesign/navSprintPlan.md` as stories progress; mark stories ✅ DONE when completed.
- Append `devInfo/storyLog.md` with the same summary used in the assistant message at the end of each story.
