# UI Tokens and Theming

This app uses CSS variables (tokens) plus Tailwind utilities for consistent theming (Light/Dark/AMOLED).

## Where tokens live

- File: `apps/web/app/styles/tokens.css`
- Loaded via `@import` in `apps/web/app/globals.css`
- Modes:
  - Light: default `:root`
  - Dark: `:root.dark` overrides
  - AMOLED: `:root.dark[data-theme='amoled']` overrides

## Key variables

- Colors: `--color-primary-600`, `--color-primary-700`, `--color-accent-600`, `--color-amber-600`
- Text: `--color-ink` (headings), `--color-body` (body), `--color-muted`
- Surfaces: `--color-line`, `--color-surface` (app bg), `--color-canvas` (card bg)
- Effects: `--shadow-1|2|3`, `--radius`, `--radius-lg`

## Tailwind mapping

- Defined in `apps/web/tailwind.config.ts` under `theme.extend.colors` and other sections.
- Examples:
  - `bg-surface` → `var(--color-surface)`
  - `text-body` → `var(--color-body)`
  - `border-line` → `var(--color-line)`
  - `shadow-mdx` → `var(--shadow-2)`
  - `rounded-lg` → `var(--radius-lg)`

## Arbitrary values with vars

You can also use Tailwind’s arbitrary values to reference tokens directly:

- `bg-[var(--color-canvas)]`
- `border-[var(--color-line)]`
- `bg-[var(--color-surface)]/80` (with opacity)

## Theme switching

- Class `dark` is applied to `<html>` for dark mode; `data-theme="amoled"` for AMOLED.
- Provider: `apps/web/src/theme/ColorSchemeContext.tsx`
- Toggle: `apps/web/src/components/ThemeToggle.tsx`

## Inspecting in DevTools

Open the Elements panel, select `<html>` or `<body>`, and view computed styles to see token values at `:root`, `:root.dark`, or AMOLED. Toggle the theme using the header button to watch variables update live.
