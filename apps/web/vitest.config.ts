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
