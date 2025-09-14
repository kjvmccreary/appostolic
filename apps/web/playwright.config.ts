import { defineConfig, devices } from '@playwright/test';

const useDevWeb = true; // flip to false later for prod builds

export default defineConfig({
  testDir: './tests/e2e',
  timeout: 120_000,
  expect: { timeout: 30_000 },
  fullyParallel: false,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL: process.env.WEB_BASE ?? 'http://localhost:3000',
    headless: true,
    trace: 'on', // capture trace
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  webServer: [
    {
      command: 'dotnet run --project ../api',
      url: process.env.API_BASE ?? 'http://localhost:5198',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: process.env.API_BASE ?? 'http://localhost:5198',
        DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER: '1',
      },
    },
    useDevWeb
      ? {
          // Next dev: reads NEXT_PUBLIC_* at runtime
          command: 'pnpm dev -p 3000',
          url: process.env.WEB_BASE ?? 'http://localhost:3000',
          reuseExistingServer: !process.env.CI,
          timeout: 120_000,
          env: {
            NEXT_PUBLIC_API_BASE: process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:5198',
            NODE_ENV: 'development',
            WEB_AUTH_ENABLED: 'true',
            AUTH_SECRET: process.env.AUTH_SECRET ?? 'test-secret-please-change',
          },
        }
      : {
          // Next start: ensure NEXT_PUBLIC_API_BASE is set for build and start
          command:
            'NEXT_PUBLIC_API_BASE=' +
            (process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:5198') +
            ' pnpm build && pnpm start -p 3000',
          url: process.env.WEB_BASE ?? 'http://localhost:3000',
          reuseExistingServer: !process.env.CI,
          timeout: 120_000,
          env: {
            NEXT_PUBLIC_API_BASE: process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:5198',
            NODE_ENV: 'production',
          },
        },
  ],
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
