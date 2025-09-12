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
    include: [
      'src/**/*.{test,spec}.{ts,tsx}',
      'app/**/components/**/*.{test,spec}.{ts,tsx}',
      'app/**/hooks/**/*.{test,spec}.{ts,tsx}',
    ],
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
        'app/**/page.tsx',
        'app/**/layout.tsx',
        'app/api-proxy/**/*.ts',
        'test/**/*',
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
