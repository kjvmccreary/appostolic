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
