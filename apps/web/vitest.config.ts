import { defineConfig } from 'vitest/config';
import tsconfigPaths from 'vite-tsconfig-paths';

export default defineConfig({
  plugins: [tsconfigPaths()],
  test: {
    environment: 'jsdom',
    setupFiles: ['./test/setup.ts'],
    globals: true,
    testTimeout: 15000,
    hookTimeout: 15000,
    environmentOptions: {
      jsdom: {
        url: 'http://localhost/',
      },
    },
    css: true,
    coverage: {
      reporter: ['text', 'html'],
    },
  },
});
