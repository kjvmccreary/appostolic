import { defineConfig } from 'vitest/config';
import tsconfigPaths from 'vite-tsconfig-paths';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [tsconfigPaths(), react()],
  test: {
    environment: 'jsdom',
    setupFiles: ['./test/setup.ts'],
    globals: true,
    testTimeout: 15000,
    hookTimeout: 15000,
    include: ['src/**/*.{test,spec}.{ts,tsx}', 'app/**/*.{test,spec}.{ts,tsx}'],
    exclude: [
      // Exclude Playwright suites from Vitest discovery
      'tests/e2e/**',
      'node_modules/**',
      'dist/**',
      'coverage/**',
      'playwright.config.*',
    ],
    environmentOptions: {
      jsdom: {
        url: 'http://localhost/',
      },
    },
    css: true,
    coverage: {
      provider: 'v8',
      enabled: true,
      reporter: ['text', 'html', 'lcov'],
      reportsDirectory: './coverage',
      include: ['src/**/*.{ts,tsx}', 'app/dev/agents/**/*.{ts,tsx}'],
      exclude: [
        '**/*.d.ts',
        'next.config.*',
        // Exclude Next.js App Router boilerplate files from coverage
        'app/**/page.tsx',
        'app/**/layout.tsx',
        'app/**/route.ts',
        'app/**/loading.tsx',
        'app/**/template.tsx',
        'src/app/**/page.tsx',
        'src/app/**/layout.tsx',
        'src/app/**/route.ts',
        'src/app/**/loading.tsx',
        'src/app/**/template.tsx',
        // Test helpers and API proxy code
        'app/api-proxy/**/*.ts',
        'test/**/*',
        // Them ing and server-only shims
        'src/theme/ThemeRegistry.tsx',
        'src/lib/serverEnv.ts',
        // Low-signal UI helpers excluded from coverage thresholds
        'app/dev/agents/components/TracesTable.tsx',
        'src/app/studio/tasks/components/TaskFilters.tsx',
      ],
      thresholds: {
        lines: 60,
        functions: 60,
        branches: 50,
        statements: 60,
      },
    },
  },
});
