# UI Sprint Plan — Theming, Layout, and Scaffolds (Sprint 02)

Date: 2025-09-14
Duration: ~2 weeks
Scope: Establish the visual foundation (tokens, themes), global layout/navigation, and scaffold key pages (Dashboard, Wizard, Editor) so we can iterate on polish and data wiring next.

Sprint Goal

- Ship a cohesive base UI that reflects the UI Spec (v2.0) in look-and-feel, with responsive layouts, accessible nav, and theming hooks in place. Pages render without backend coupling (mock data/state), preserving current functionality (Studio, Dev tools).

Out of Scope (this sprint)

- Real “Lessons” API and data wiring for Wizard/Editor
- Marketplace integration and analytics
- Rich text editor and slides/PDF generation

Assumptions

- We will add Tailwind CSS at apps/web only (scoped), coexisting with current MUI Material usage. We will not refactor existing Studio pages to Tailwind in this sprint.
- New UI primitives will live under apps/web/src/components/ui to align with current repo conventions (existing components live under apps/web/src/components and app/\*/components).
- We will use mock data/state for Wizard and Editor until backend endpoints exist.

Dependencies and Risks

- Add dev dependencies (tailwindcss, postcss, autoprefixer, lucide-react, classnames). Minimal risk to existing code.
- Coexistence of MUI Material with Tailwind (and optional MUI Joy later). Risk addressed by scoping CSS and avoiding global resets.
- layout.tsx has recent manual edits; TopBar integration will be additive and behind a small refactor.

---

Epic 1 — Theming Foundation (Tailwind + Tokens)

Story 1.1: Add Tailwind and PostCSS to web app

- Files/Changes:
  - apps/web/tailwind.config.ts (scoped to app/**/\*.{ts,tsx} and src/**/\*.{ts,tsx})
  - apps/web/postcss.config.js
  - apps/web/app/globals.css → prepend @tailwind base; @tailwind components; @tailwind utilities
  - package updates: tailwindcss, postcss, autoprefixer (dev deps)
- Acceptance Criteria
  - Tailwind builds in dev and prod; no style regressions in existing pages
  - Purge/content paths configured to avoid bloated CSS
  - globals.css retains custom rules (tenant-switcher, page-wrap)

Story 1.2: Introduce design tokens (tokens.css) and CSS variables

- Files/Changes:
  - apps/web/app/styles/tokens.css with variables from UI Spec 3. Design Tokens
  - Import tokens.css from app/layout.tsx or globals.css
  - Document tokens mapping in devInfo/Ui/README.md (optional)
- Acceptance Criteria
  - CSS variables are available at :root and can be inspected in DevTools
  - Example utility classes read vars (e.g., bg-[color:var(--color-canvas)])

Story 1.3: Theme provider and system theme support (Light/Dark/AMOLED)

- Files/Changes:
  - apps/web/src/components/ui/ThemeProvider.tsx (context + prefers-color-scheme)
  - apps/web/src/components/ui/ThemeToggle.tsx (button)
  - Tailwind config: darkMode: 'class' and AMOLED variant via data-theme='amoled'
- Acceptance Criteria
  - Theme toggling updates document root class/data-theme
  - Respects system theme on first render; persists user choice in localStorage

---

Epic 2 — Layout & Navigation

Story 2.1: TopBar scaffold with navigation and Tenant pill

- Files/Changes:
  - apps/web/src/components/TopBar.tsx (uses tokens + Tailwind; lucide icons)
  - Reuse existing <TenantSwitcher/> inside TopBar as a tenant pill
  - Nav buttons: Dashboard (/), Wizard (/wizard/step1), Editor (/editor)
- Acceptance Criteria
  - TopBar is sticky, responsive, and keyboard accessible
  - Active nav shows aria-current and visible state

Story 2.2: Integrate TopBar in global layout

- Files/Changes:
  - apps/web/app/layout.tsx — replace inline TenantSwitcher row with <TopBar/>
- Acceptance Criteria
  - Protected pages continue to render TenantSwitcher within TopBar (only on protected paths)
  - No duplication on /select-tenant

Story 2.3: cn() utility and icon set

- Files/Changes:
  - apps/web/src/lib/cn.ts (simple classnames wrapper)
  - Add lucide-react and classnames to web package.json
- Acceptance Criteria
  - cn() is used in TopBar/NavButton and initial components

---

Epic 3 — UI Primitives

Story 3.1: Card and ActionTile

- Files/Changes:
  - apps/web/src/components/ui/Card.tsx
  - apps/web/src/components/ui/ActionTile.tsx
- Acceptance Criteria
  - Card supports title, description, and children; respects tokens (surface/canvas, shadow, radius)
  - ActionTile variant with hover and CTA affordance

Story 3.2: Chip and Stepper

- Files/Changes:
  - apps/web/src/components/ui/Chip.tsx
  - apps/web/src/components/ui/Stepper.tsx (numbers + labels; active vs inactive)
- Acceptance Criteria
  - Chip supports status variants (draft, slides, handout) using tokens
  - Stepper renders 5 steps and announces progress to screen readers

---

Epic 4 — Pages Scaffolding (mocked)

Story 4.1: Dashboard scaffold

- Files/Changes:
  - apps/web/app/page.tsx → implement Dashboard layout from spec: Quick Start, Recent, Plan & Usage, Templates, Guardrails, Marketplace
  - Use Card/ActionTile/Chip primitives; mock recent items with chips
- Acceptance Criteria
  - Grid responsive (mobile 1, tablet 2, desktop 3 columns)
  - Quick Start links to /wizard/step1

Story 4.2: Wizard scaffolding (5 steps)

- Files/Changes:
  - apps/web/app/wizard/step1/page.tsx
  - apps/web/app/wizard/step2/page.tsx
  - apps/web/app/wizard/step3/page.tsx
  - apps/web/app/wizard/step4/page.tsx
  - apps/web/app/wizard/step5/page.tsx (review)
  - Local state with URL param persistence; Stepper displayed on all steps
- Acceptance Criteria
  - Back/Next nav works client-side; steps count=5; duration slider ticks match spec
  - Review aggregates selected inputs

Story 4.3: Editor scaffold (mock)

- Files/Changes:
  - apps/web/app/editor/page.tsx (title, metadata, outline, scripture blockquote)
  - Sidebar with metadata cards; actions: Save, Generate Slides, Export PDF (no-ops)
- Acceptance Criteria
  - Responsive two-column layout; sidebar collapses on mobile
  - Back to Wizard Step 2 link present

---

Epic 5 — Responsiveness & Accessibility

Story 5.1: Responsive helpers and utilities

- Files/Changes:
  - Tailwind utilities: grid templates for grid3
  - Layouts use sticky TopBar and proper spacing tokens
- Acceptance Criteria
  - Verified at 375px, 768px, 1280px (manual)

Story 5.2: Accessibility pass

- Files/Changes:
  - ARIA attributes on nav/buttons/stepper; label/id hookups for inputs
  - Focus rings and keyboard navigation
- Acceptance Criteria
  - Focus visible; aria-current on nav; labels tied; Lighthouse a11y ≥ 95 on key pages

---

Epic 6 — Testing & Tooling

Story 6.1: Unit tests for primitives and pages

- Files/Changes:
  - Vitest tests for Stepper (5 steps), ThemeProvider (toggles), Dashboard tiles
- Acceptance Criteria
  - Tests pass in CI; act() warnings addressed

Story 6.2: E2E smoke flows (Playwright)

- Files/Changes:
  - Flows: visit Dashboard → Start Wizard → navigate steps → open Editor
- Acceptance Criteria
  - Headless pass locally; CI optional if time allows

---

Epic 7 — Integration Safety

Story 7.1: Non-breaking integration with existing app

- Files/Changes:
  - Verify middleware + protected route behavior with TopBar
  - Ensure Studio/Dev pages unaffected; adjust padding wrappers
- Acceptance Criteria
  - Studio pages render unchanged; TenantSwitcher behavior preserved

---

Backlog/Next Sprint Candidates

- Replace mock Editor actions with real endpoints
- Introduce MUI Joy components where they improve productivity (Menu, Sheet)
- Marketplace and analytics tiles backed by data
- Rich text editor for Draft

---

Sequencing & Estimate (T-Shirt)

1. Epic 1 (S/M), 2) Epic 2 (S/M), 3) Epic 3 (M), 4) Epic 4 (M/L), 5) Epic 5 (S), 6) Epic 6 (S/M), 7) Epic 7 (S)

Definition of Done

- Theming foundation in place with tokens and theme toggle
- TopBar integrated; navigation works; no protected route regressions
- Dashboard, Wizard (5 steps), and Editor render with mocked content
- Responsive layouts pass manual checks; basic a11y verified
- Unit tests green; at least one e2e smoke passes locally

Notes and File Map

- Tokens: apps/web/app/styles/tokens.css (imported in layout or globals)
- Primitives: apps/web/src/components/ui/{Card,Chip,Stepper,ActionTile,ThemeProvider,ThemeToggle}.tsx
- Pages: apps/web/app/{page.tsx,wizard/\*/page.tsx,editor/page.tsx}
- Utilities: apps/web/src/lib/cn.ts
- TopBar: apps/web/src/components/TopBar.tsx (uses TenantSwitcher)

Acceptance Criteria Trace to UI Spec v2.0

- 9.1 Dashboard sections present (Story 4.1)
- 9.2 Wizard 5 steps + back/next (Story 4.2)
- 9.3 Editor mock with actions (Story 4.3)
- 9.4 Responsive at 3 breakpoints (Stories 5.1/4.1/4.3)
- 9.5 Theme follows system + toggle (Story 1.3)
- 9.6 Accessibility basics (Story 5.2)

Risks/Mitigations

- Tailwind setup conflicts with existing CSS → Scope content paths; keep globals minimal
- TopBar integration conflicts with layout edits → Gate behind same protected path logic; test /select-tenant
- Timebox Wizard polish; focus on scaffolding this sprint
