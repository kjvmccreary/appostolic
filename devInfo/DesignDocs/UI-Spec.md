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

- Tenant pill, app title, nav buttons (Dashboard, Wizard, Editor)
- CTA: **Create Lesson** (accent button)

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

---

## 13. Navigation Design (Expanded)

### 13.1 Information Architecture (Web)

**Primary TopBar (always visible):**

- **Logo + Tenant Pill** (click → Tenant Switcher / Tenant Home)
- **Primary Nav:** Dashboard · Wizard · Editor
- **Overflow Menus (right side):**
  - **Notifications** (bell)
  - **Help** (docs, keyboard shortcuts, contact)
  - **Profile** (avatar) — profile & account actions
  - **Admin** (only for Tenant Admin/Owner) — org-wide configuration

**Mobile Web behavior:**

- Collapse primary nav into a single **Menu** button (hamburger). Drawer reveals: Dashboard, Wizard, Editor, Admin (if authorized), Help.
- Tenant pill remains visible; Notifications and Avatar remain as icons in the TopBar.

### 13.2 Route Map

```
/                       → Dashboard
/wizard                 → Wizard (step 1)
/wizard/step2           → Wizard (step 2)
/wizard/review          → Wizard (review)
/editor                 → Editor
/profile                → Profile & Preferences
/profile/security       → Security (password, 2FA*)
/profile/notifications  → Notification prefs
/profile/keys           → API Keys (app-scoped)*
/admin                  → Admin Landing (requires role: Admin|Owner)
/admin/guardrails       → Guardrails (doctrine/denomination presets)
/admin/roles            → Roles & Invites
/admin/billing          → Billing & Plans
/admin/usage            → Usage & Quotas
/admin/audit            → Audit Log*
/tenants                → Tenant Switcher
```

`*` = Post-MVP unless specifically pulled in.

### 13.3 Navigation States & Access Rules

- **Admin menu visibility**: only for `Owner` or `Admin` roles. Hidden (not disabled) for others.
- **Direct access protection**: server-side check on admin routes; unauthorized → 403 page with link to Home.
- **Active state**: TopBar buttons use `aria-current="page"` and primary bg.
- **Breadcrumbs**: Admin sub-pages show a lightweight breadcrumb: Admin ▸ Section.

### 13.4 Components (Web)

- **TopBar**: `TopBar.tsx` accepts `{ user, tenant, canAdmin }` and renders the Admin dropdown if `canAdmin`.
- **NavDrawer** (mobile): `NavDrawer.tsx` with sections: Primary, Admin (conditional), Help.
- **TenantSwitcher**: `TenantSwitcher.tsx` modal/panel with searchable tenant list, “Create new tenant”.

---

## 14. Tenant Admin UX (MVP scope)

> Goal: give Admin/Owner practical controls needed for Release 1.0 without overbuilding.

### 14.1 Guardrails (`/admin/guardrails`) — **In MVP**

- **Purpose**: Configure doctrinal boundaries & denomination profile used by generation.
- **Sections**:
  - **Denomination Profile**: preset picker (e.g., Mere Christianity) + editable notes.
  - **Content Filters**: toggles/sliders (e.g., political content: restrict/allow; miracles language: neutral/affirmative, etc.).
  - **Review Examples**: small panel that shows how prompts are adjusted.

- **Actions**: Save (primary), Reset to preset, Export snapshot.
- **Persistence**: saves a versioned policy snapshot.

### 14.2 Roles & Invites (`/admin/roles`) — **In MVP**

- **Table**: Member email, Role (Owner/Admin/Member), Status (Active/Pending), Last active.
- **Actions**: Invite user (email), change role, revoke invite, remove member.
- **Security**: role changes require Owner or Admin.

### 14.3 Billing & Plans (`/admin/billing`) — **In MVP**

- **Plan**: Free / Pro / Org; current cycle usage.
- **Payment**: link to Stripe portal; show invoices.
- **Quotas**: show jobs remaining; upgrade CTA.

### 14.4 Usage & Quotas (`/admin/usage`) — **In MVP**

- **Charts**: Jobs by day, costs by deliverable type.
- **Filters**: date range, user.
- **Export**: CSV.

### 14.5 Audit Log (`/admin/audit`) — **Post-MVP**

- **List**: action, actor, entity, timestamp, IP.

---

## 15. User Profile UX (All users)

### 15.1 Profile & Preferences (`/profile`) — **In MVP**

- **Profile**: Name, avatar, email (read-only if SSO later), time zone.
- **Display**: Theme (Light/Dark/AMOLED/System), **Prefer AMOLED in dark** toggle, reduced motion toggle.
- **Notifications**: global email toggles (product updates, job complete), per-tenant overrides (future).
- **Tenants**: list current membership with roles; link to `/tenants`.
- **Security (placeholder)**: link to `/profile/security` for password/2FA (post-MVP).

### 15.2 API Keys (`/profile/keys`) — **Post-MVP**

- Generate, revoke, and view limited-scope keys.

### 15.3 Notifications Menu (TopBar)

- **Bell panel**: list of latest job states (Succeeded/Failed), invites, billing warnings.
- **Actions**: mark as read, go to item, settings link → `/profile/notifications`.

---

## 16. Responsive Behavior (Admin & Profile)

- **Mobile web**: Admin & Profile pages use single-column flow; tables become stack cards; actions move to kebab menus.
- **Desktop**: Admin pages use two-pane layout: left nav (section list), right content; sticky section header.

---

## 17. Acceptance Criteria (additions)

1. **Admin menu** is visible only to Owner/Admin users; hidden otherwise.
2. `/admin/*` routes enforce server-side authorization; non-admins receive a 403.
3. **Guardrails page** saves a versioned snapshot and shows a confirmation toast.
4. **Roles & Invites** supports invite, role change, revoke, remove with proper toasts and error states.
5. **Billing** links to Stripe portal and displays current plan & invoices.
6. **Usage** shows jobs/time chart and exports CSV.
7. **Profile** allows theme selection + “Prefer AMOLED in dark”; persists across sessions.
8. **Mobile web**: Admin tables render as cards; all actions reachable via touch targets ≥44px.

---

## 18. Dev Notes

- **Authorization**: use middleware or route handlers to gate `/admin/*` on the server.
- **Data fetching**: RSC where possible; mutate via actions/route handlers.
- **Toasts**: headless (e.g., Radix or MUI Joy Snackbar) using tokens.
- **Icons**: lucide-react outline set only (no colored emojis).
- **Analytics**: page-level events for Admin actions (not PII).
