import type { Config } from 'tailwindcss';

export default {
  content: ['./app/**/*.{ts,tsx}', './src/**/*.{ts,tsx}', './components/**/*.{ts,tsx}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        primary: {
          600: 'var(--color-primary-600)',
          700: 'var(--color-primary-700)',
        },
        accent: {
          600: 'var(--color-accent-600)',
        },
        amber: {
          600: 'var(--color-amber-600)',
        },
        ink: 'var(--color-ink)',
        body: 'var(--color-body)',
        muted: 'var(--color-muted)',
        line: 'var(--color-line)',
        surface: 'var(--color-surface)',
        canvas: 'var(--color-canvas)',
      },
      boxShadow: {
        smx: 'var(--shadow-1)',
        mdx: 'var(--shadow-2)',
        lgx: 'var(--shadow-3)',
      },
      borderRadius: {
        DEFAULT: 'var(--radius)',
        lg: 'var(--radius-lg)',
      },
    },
  },
  plugins: [],
} satisfies Config;
