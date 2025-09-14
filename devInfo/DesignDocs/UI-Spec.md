# Appostolic UI Implementation Spec (v0.1)

> Purpose: a single design+build spec you can drop into VS Code so GitHub Copilot (and I) can start coding the UI consistently across web and mobile. This document defines tokens, theming, components, pages, routes, and acceptance criteria.

---

## 0) Outcomes & Guardrails

**Outcomes**

- Ship a cohesive, vibrant-but-calm theme and initial surfaces (Dashboard, Lesson Wizard, Editor shell) in Next.js.
- Centralize design tokens (CSS variables) shared by Tailwind and MUI.
- Keep layout responsive, accessible (WCAG AA), and multi-tenant aware.

**Non-goals (for now)**

- Full marketplace flows, billing UI, or advanced analytics.

**Constraints**

- Stack: Next.js 14 (App Router), Tailwind, Material UI (MUI) Pro, TypeScript.
- Multi-tenant: header tenant switcher is present; pages assume a selected tenant.

---

## 1) File/Folder Plan (web)

```
apps/web/
├─ app/
│  ├─ layout.tsx                      # mounts ThemeProvider + tokens + global styles
│  ├─ page.tsx                        # Dashboard (tenant-aware)
│  ├─ wizard/
│  │  ├─ page.tsx                     # Wizard step router (steps as child routes)
│  │  ├─ step-1/page.tsx              # Topic / Scripture
│  │  └─ step-2/page.tsx              # Audience & Duration
│  ├─ editor/
│  │  └─ page.tsx                     # Editor shell
│  └─ api-proxy/                      # existing server routes (unchanged)
├─ src/
│  ├─ theme/
│  │  ├─ tokens.css                   # CSS variables (source of truth)
│  │  ├─ muiTheme.ts                  # MUI theme generated from tokens
│  │  └─ index.ts                     # export helpers (getCssVar, token types)
│  ├─ components/
│  │  ├─ layout/
│  │  │  ├─ AppTopBar.tsx             # logo, tenant switcher, nav
│  │  │  └─ AppShell.tsx              # top bar + optional sidebar + container
│  │  ├─ cards/
│  │  │  ├─ StatCard.tsx
│  │  │  └─ ActionCard.tsx
│  │  ├─ wizard/
│  │  │  ├─ Stepper.tsx
│  │  │  ├─ AudienceCards.tsx
│  │  │  └─ DurationSlider.tsx
│  │  └─ editor/
│  │     └─ EditorToolbar.tsx         # Save/Slides/PDF CTAs
│  ├─ lib/
│  │  └─ ui.ts                        # classnames helpers, screen containers
│  └─ styles/
│     └─ globals.css                  # imports tokens.css, Tailwind base/utilities
└─ tailwind.config.ts                 # reads CSS variable palette
```

**Optional**

- `packages/ui/` (later): extract shared components.

---

## 2) Design Tokens (CSS Variables)

Create `apps/web/src/theme/tokens.css`:

```css
:root {
  /* Primary (softened) */
  --color-primary-600: #4c669f;
  --color-primary-700: #3b5283;
  /* Accents */
  --color-accent-600: #d16666; /* coral */
  --color-amber-600: #b5892e; /* amber */
  /* Neutrals */
  --color-ink: #1a202c; /* titles */
  --color-body: #2d3748; /* body text */
  --color-muted: #718096; /* meta */
  --color-line: #e2e8f0; /* borders */
  --color-surface: #fdfdfd; /* app bg */
  --color-canvas: #ffffff; /* card bg */

  /* Radii & Elevation */
  --radius: 12px;
  --radius-lg: 16px;
  --shadow-1: 0 1px 2px rgba(0, 0, 0, 0.04);
  --shadow-2: 0 3px 6px rgba(0, 0, 0, 0.06);
  --shadow-3: 0 8px 16px rgba(0, 0, 0, 0.1);

  /* Typography scale (example) */
  --font-family: Inter, system-ui, -apple-system, Segoe UI, Roboto, sans-serif;
}

@media (prefers-color-scheme: dark) {
  :root {
    --color-surface: #0e1320;
    --color-canvas: #111827;
    --color-ink: #e5e7eb;
    --color-body: #cbd5e1;
    --color-muted: #94a3b8;
    --color-line: #1f2937;
  }
}
```

Import in `globals.css` **before** Tailwind utilities:

```css
@import '../theme/tokens.css';
@tailwind base;
@tailwind components;
@tailwind utilities;
```

---

## 3) Tailwind config (palette from tokens)

`tailwind.config.ts` (excerpt):

```ts
import type { Config } from 'tailwindcss';

export default {
  content: ['./app/**/*.{ts,tsx}', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        primary: {
          600: 'var(--color-primary-600)',
          700: 'var(--color-primary-700)',
        },
        accent: { 600: 'var(--color-accent-600)' },
        amber: { 600: 'var(--color-amber-600)' },
        ink: 'var(--color-ink)',
        body: 'var(--color-body)',
        muted: 'var(--color-muted)',
        line: 'var(--color-line)',
        surface: 'var(--color-surface)',
        canvas: 'var(--color-canvas)',
      },
      borderRadius: { xl: '12px', '2xl': '16px' },
      boxShadow: {
        elev1: 'var(--shadow-1)',
        elev2: 'var(--shadow-2)',
        elev3: 'var(--shadow-3)',
      },
    },
  },
  plugins: [],
} satisfies Config;
```

---

## 4) MUI Theme from tokens

`src/theme/muiTheme.ts`:

```ts
'use client';
import { createTheme } from '@mui/material/styles';

export const muiTheme = createTheme({
  palette: {
    primary: { main: 'var(--color-primary-600)' as any },
    secondary: { main: 'var(--color-accent-600)' as any },
    background: { default: 'var(--color-surface)' as any, paper: 'var(--color-canvas)' as any },
    text: { primary: 'var(--color-body)' as any, secondary: 'var(--color-muted)' as any },
    error: { main: '#EF4444' },
    warning: { main: '#F59E0B' },
    success: { main: '#22C55E' },
  },
  shape: { borderRadius: 12 },
  typography: {
    fontFamily: 'var(--font-family)',
    h1: { fontWeight: 700, fontSize: 32, lineHeight: 1.25 },
    h2: { fontWeight: 700, fontSize: 24, lineHeight: 1.33 },
    h3: { fontWeight: 600, fontSize: 20, lineHeight: 1.4 },
    body1: { fontSize: 16, lineHeight: 1.5 },
    body2: { fontSize: 14, lineHeight: 1.43 },
  },
  components: {
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: { root: { textTransform: 'none', borderRadius: 12 } },
    },
    MuiPaper: { styleOverrides: { rounded: { borderRadius: 12 } } },
  },
});
```

Use it in `app/layout.tsx`:

```tsx
import './styles/globals.css';
import { ThemeProvider, CssBaseline } from '@mui/material';
import { muiTheme } from '@/src/theme/muiTheme';

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <ThemeProvider theme={muiTheme}>
          <CssBaseline />
          {children}
        </ThemeProvider>
      </body>
    </html>
  );
}
```

---

## 5) Primitive classes (utility CSS)

Add to `globals.css` for common primitives (mirrors the preview):

```css
.btn {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  height: 40px;
  padding: 0 16px;
  border-radius: var(--radius);
  font-weight: 600;
}
.btn-primary {
  background: var(--color-primary-600);
  color: #fff;
}
.btn-primary:hover {
  background: var(--color-primary-700);
}
.btn-ghost {
  color: var(--color-primary-600);
}
.btn-ghost:hover {
  background: color-mix(in sRGB, var(--color-primary-600) 8%, transparent);
}
.card {
  background: var(--color-canvas);
  border: 1px solid var(--color-line);
  border-radius: var(--radius);
  box-shadow: var(--shadow-1);
}
.chip {
  border-radius: 999px;
  padding: 0.125rem 0.5rem;
  font-size: 0.75rem;
  border: 1px solid var(--color-line);
  color: var(--color-muted);
}
.input {
  background: #fff;
  border: 1px solid var(--color-line);
  border-radius: var(--radius);
  padding: 0.625rem 0.75rem;
  outline: none;
}
.input:focus {
  border-color: var(--color-primary-600);
  box-shadow: 0 0 0 3px color-mix(in sRGB, var(--color-primary-600) 15%, transparent);
}
.kicker {
  letter-spacing: 0.08em;
  text-transform: uppercase;
  font-weight: 700;
  font-size: 0.75rem;
  color: var(--color-muted);
}
```

---

## 6) Layout Components

### 6.1 AppTopBar

Props: `{ active?: 'dashboard'|'wizard'|'editor'; onCreateLesson?: () => void }`

Content:

- Logo (A monogram in primary-600)
- Product name “Appostolic” (ink)
- Tenant pill (current tenant slug)
- Nav buttons: Dashboard / Wizard / Editor
- CTA: Create Lesson (primary)

Accessibility:

- Semantic `<nav>` with `aria-current` on active link
- Visible focus styles

### 6.2 AppShell

- Wraps pages with top bar and a centered container (`max-w-6xl mx-auto px-6`).
- Accepts optional `sidebar` slot.

---

## 7) Pages

### 7.1 Dashboard (`app/page.tsx`)

- **Quick Start** card: title, description, `Start Wizard` button.
- **Recent** card: list 3 items with status chips.
- **Plan & Usage** card: plan name, progress bar, `Manage Billing` (ghost).
- Second row: **Templates**, **Guardrails**, **Marketplace** ActionCards.

**Acceptance**

- Responsive 1→3 columns.
- Keyboard reachable buttons; all interactive elements have focus ring.

### 7.2 Wizard (`app/wizard/step-1`, `step-2`)

- **Stepper** (5 steps): Topic → Audience & Duration → Tone & Denomination → Deliverables → Review.
- **Step 1**: Topic or Scripture (input), Objectives (textarea), Tips card.
- **Step 2**: AudienceCards (Kids/Jr/Sr), DurationSlider (30–90 in 15-min increments), Tone chips, Denomination pill.

**Acceptance**

- Stepper shows steps 1..N as active.
- “Next” disabled until minimal inputs present (e.g., audience set in Step 2).

### 7.3 Editor (`app/editor/page.tsx`)

- Document title, meta (`Draft · N words`).
- Toolbar CTA: Save (primary), Generate Slides (amber), Export PDF (accent).
- Right sidebar: Metadata (Audience, Duration, Denomination) — sticky.

**Acceptance**

- Sidebar sticky with `top: 72px` (under top bar).
- Buttons announce action via `aria-label`.

---

## 8) Components (details)

### 8.1 Stepper

Props: `{ step: 1|2|3|4|5, labels?: string[] }`

- Renders numbered circles; active ≤ step uses primary-600 background + white text.

### 8.2 AudienceCards

Props: `{ value?: 'kids'|'jr'|'sr', onChange: (v) => void }`

- Three selectable cards; selected has 3px outline (primary-600 @ 25%).

### 8.3 DurationSlider

Props: `{ value: number, onChange: (n:number)=>void }`

- Input range 30–90 step 15; label shows `Duration: {value} minutes`.

### 8.4 EditorToolbar

Props: `{ onSave, onSlides, onExport }`

- Three buttons with prescribed colors from tokens.

---

## 9) Accessibility

- Color contrast AA for all text/buttons (tokens picked accordingly).
- Minimum target size 40×40.
- Visible `:focus` with 3px ring in primary.
- `prefers-reduced-motion`: reduce transitions/animations.

---

## 10) Dark Mode (preview)

- Dark tokens in `@media (prefers-color-scheme: dark)`; no separate theme file.
- Buttons keep white text; chips invert to higher contrast foreground.

---

## 11) Developer Notes

**Server Components vs Client**

- Pages under `app/*` default to Server Components. Mark interactive components with `'use client'` at file top.

**Tenant Context**

- Read current tenant from existing session/cookie helper. Pages should gracefully render “Select tenant” when missing.

**Testing**

- Vitest + RTL for Stepper/AudienceCards interactions.
- Axe checks for a11y on key pages.

---

## 12) Copilot Prompts (paste into VS Code)

1. _Create tokens and theme:_

> Implement `apps/web/src/theme/tokens.css` and import in `apps/web/src/styles/globals.css`. Then create `src/theme/muiTheme.ts` that reads the CSS variables into a MUI theme as in the spec. Update `app/layout.tsx` to mount ThemeProvider.

2. _Build layout components:_

> Scaffold `src/components/layout/AppTopBar.tsx` and `src/components/layout/AppShell.tsx` per the spec. AppTopBar should render logo, product name, tenant pill, nav buttons, and Create Lesson CTA.

3. _Dashboard and cards:_

> Implement `app/page.tsx` that composes three cards (Quick Start, Recent, Plan & Usage) and a second row of ActionCards (Templates, Guardrails, Marketplace). Use Tailwind utilities and the `.card`, `.btn`, `.chip` primitives.

4. _Wizard steps:_

> Create `app/wizard/step-1/page.tsx` and `step-2/page.tsx` with Stepper, AudienceCards, and DurationSlider. Disable Next until required inputs are provided.

5. _Editor shell:_

> Create `app/editor/page.tsx` with a document header, EditorToolbar, and a sticky right metadata sidebar.

---

## 13) Definition of Done

- Tokens file and MUI theme compiled with no type errors.
- Pages render and are navigable with keyboard only.
- Lighthouse a11y score ≥ 95 on the three surfaces.
- All colors come from CSS variables; no hard-coded hex in components.
- Responsive layout verified at 360px, 768px, 1280px widths.

---

## 14) Next Iterations

- Pick **coral** vs **amber** as the primary CTA accent and propagate.
- Add `@appostolic/ui` package to share Stepper/AudienceCards across web/mobile.
- Integrate Syncfusion editor in the Editor surface.

---

_End of spec._
