# Appostolic Web UI Spec (v2.0)

This is the merged, authoritative spec for the **Appostolic Web UI**, combining:

- **v0.1 UI-Spec.md** (implementation guide, scaffolding, build notes)
- **v1.0 Web UI Spec** (design + requirements, acceptance criteria)

---

## 1. Tech Stack

- **Framework**: Next.js 14 (App Router)
- **Styling**: Tailwind CSS with custom CSS variables (tokens.css)
- **UI Primitives**: MUI Joy for components where helpful, wrapped in Tailwind utility classes
- **Icons**: lucide-react (outline style)
- **Theming**: CSS variables + Tailwind + MUI; Light, Dark, AMOLED (system-aware)
- **State**: React hooks + Context for theme & nav state
- **Tests**: Jest + Playwright for UI/e2e

---

## 2. File & Folder Structure

```
apps/web/
  app/
    layout.tsx
    page.tsx (Dashboard)
    wizard/
      step1/page.tsx
      step2/page.tsx
      review/page.tsx
    editor/page.tsx
  components/
    TopBar.tsx
    NavButton.tsx
    Card.tsx
    Chip.tsx
    Stepper.tsx
    ActionTile.tsx
  styles/
    tokens.css
  lib/
    cn.ts (classnames)
    smokeTests.ts
```

---

## 3. Design Tokens (tokens.css)

```css
:root {
  --color-primary-600: #4c669f;
  --color-primary-700: #3b5283;
  --color-accent-600: #d16666; /* coral */
  --color-amber-600: #b5892e; /* amber */
  --color-ink: #1a202c; /* headings */
  --color-body: #2d3748; /* body text */
  --color-muted: #718096; /* meta */
  --color-line: #e2e8f0; /* borders */
  --color-surface: #fdfdfd; /* app bg */
  --color-canvas: #ffffff; /* card bg */

  --shadow-1: 0 1px 2px rgba(0, 0, 0, 0.04);
  --shadow-2: 0 3px 6px rgba(0, 0, 0, 0.06);
  --shadow-3: 0 8px 16px rgba(0, 0, 0, 0.1);
  --radius: 12px;
  --radius-lg: 16px;
}
```

---

## 4. Layout & Navigation

### TopBar

- Composition (desktop ≥768px)
  - Left: Tenant pill (selected tenant) → click opens Tenant Switcher dialog
  - Center-left: App title + primary nav (Dashboard, Agents, Admin)
  - Right: Contextual actions + Profile
    - Primary CTA: **Create Lesson** (Creator only)
    - Secondary: quick links (Agents → New, if Creator)
    - Profile menu (avatar or initials)

- Composition (mobile <768px)
  - Left: Hamburger icon toggles a slide-out Nav Drawer
  - Center: App title (tap returns to Dashboard/Studio home)
  - Right: Tenant pill (compact) + Profile avatar

- Behaviors
  - Sticky TopBar; elevates on scroll
  - Tenant pill reflects `selected_tenant`; keyboard accessible; SR label includes current tenant name
  - CTA visibility depends on roles (see Role-based Visibility)

### Primary Navigation (desktop) / Nav Drawer (mobile)

- Items (grouped)
  - Home
    - Dashboard (/studio or /studio/agents)
    - Agents (/studio/agents)
  - Admin (TenantAdmin only)
    - Members (/studio/admin/members)
    - Invites (/studio/admin/invites)
    - Audits (/studio/admin/audits)
    - Notifications DLQ (/studio/admin/notifications/dlq)
  - Dev (Development only)
    - Agents (dev) (/dev/agents)
    - Health (/dev/health)

- Mobile Nav Drawer
  - Full-height sheet from left, focus trapped when open
  - Sections collapsible; Admin section shown only for TenantAdmin
  - Close on route change, ESC, or backdrop tap

### Profile Menu & Tenant Switcher

- Profile menu (avatar at TopBar right)
  - Profile (placeholder) — /studio/profile
  - Switch tenant — opens Tenant Switcher (lists available memberships)
  - Sign out
  - Superadmin (dev) — badge shown if `superadmin` claim present

- Tenant Switcher
  - Reads session memberships; highlights current tenant
  - On select: calls `/api/tenant/select` (server route) then reloads to landing page for tenant
  - Accessible listbox pattern with keyboard support

### Role-based Visibility

- TenantAdmin
  - Sees Admin menu (Members, Invites, Audits, Notifications DLQ)
  - Can access Members list and edit roles; can view audits
- Creator
  - Sees Create actions (Create Lesson, New Agent)
  - Agents menu visible
- Approver
  - Sees Approvals (future endpoints) when present
- Learner
  - Read-only; no create/admin actions; can access learning content only
- Superadmin (dev/test)
  - May see cross-tenant admin pages where implemented; marked with a "Superadmin" chip in Profile

Implementation notes

- Visibility is derived from session flags (isAdmin/canApprove/canCreate/isLearner) computed server-side.
- Do not rely on client-only gating; API remains source of truth and enforces authorization.
- Use `aria-current="page"` on the active nav item; add visually hidden labels for icon-only buttons.

### Dashboard

- Quick Start card → Start Wizard
- Recent Lessons → list with chips (Draft, Slides, Handout)
- Plan & Usage → plan name, quota bar, Manage Billing
- Secondary cards: Templates, Guardrails, Marketplace

### Wizard

- **5 steps**:
  1. Topic
  2. Audience & Duration
  3. Tone & Denomination
  4. Deliverables
  5. Review

- Stepper: numbered circles + labels (active vs inactive)
- Inputs: topic field, objectives textarea, audience cards, duration slider, tone chips, denomination profile chip

### Editor

### Acceptance (Navigation)

1. Desktop shows TopBar with Tenant pill, primary nav, role-gated actions, and Profile.
2. Mobile shows Hamburger → Nav Drawer; identical items as desktop, gated by roles.
3. TenantAdmin sees Admin section; non-admins do not see Admin.
4. Creator sees Create lesson CTA; non-creators do not.
5. Profile menu includes Switch tenant and Sign out; Switch tenant updates `selected_tenant` via `/api/tenant/select`.
6. Keyboard navigation and ARIA labels verified; focus trap in Nav Drawer.

- Draft: title, metadata, outline, scripture blockquote
- Actions: Save, Generate Slides, Export PDF
- Sidebar: metadata (Audience, Duration, Denomination)
- Back button → Wizard Step 2

---

## 5. Responsiveness

- Mobile-first (375px), Tablet (768px), Desktop (1280px)
- Grid utility (`grid3`): 1 col mobile, 2 col tablet, 3 col desktop
- Sticky TopBar
- Sidebar collapses below content on small screens
- Inputs and cards stretch full width on mobile

---

## 6. Accessibility

- Nav buttons: `aria-current` for active page
- Inputs: labels tied via `for`/`id`
- Color contrast AA+ in all themes
- Keyboard navigation supported; visible focus rings
- Screen-reader labels on all icons

---

## 7. Theming

- Light, Dark, AMOLED themes
- Dark/AMOLED respect `prefers-color-scheme`
- Theme tokens loaded via CSS vars at `:root`
- Tailwind + MUI Joy both consume tokens

---

## 8. Testing Hooks

- Wizard steps count = 5
- Duration slider ticks = 30, 45, 60, 75, 90
- CTA token = `--color-accent-600`
- `cn()` util works for conditional class merging

---

## 9. Acceptance Criteria (MVP)

1. Dashboard shows Quick Start, Recent, Plan & Usage, Templates, Guardrails, Marketplace
2. Wizard flows through 5 steps with back/next
3. Editor mock supports Save, Generate Slides, Export PDF
4. Responsive layouts verified at 375px, 768px, 1280px
5. Theme responds to system light/dark
6. Accessible: ARIA, labels, focus management

---

## 10. Nice-to-Haves (Post-MVP)

- Marketplace integration with live data
- Offline drafts & sync
- Analytics dashboards
- Rich text editor

---

## 11. Copilot Prompts (Dev Usage)

- _“Scaffold WizardStep2 with 3 AudienceCard components, pulling tokens from tokens.css.”_
- _“Generate a responsive Dashboard grid with Quick Start, Recent, Plan, Templates, Guardrails, Marketplace.”_
- _“Wire Tailwind dark: variants to tokens.css for dark/AMOLED.”_

---

## 12. References

- Derived from **Appostolic Ui Preview – Dashboard & Wizard (v0.4)**
- Synced with **SnapshotArchitecture.md** (Web app section)
- Supersedes **UI-Spec.md (v0.1)** and **Web UI Spec (v1.0)**
