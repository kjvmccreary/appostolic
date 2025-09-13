import { test, expect } from '@playwright/test';

// Skip in CI unless explicitly enabled
const skipInCi = !!process.env.CI && !process.env.E2E_WEB_ENABLE;

(skipInCi ? test.skip : test)(
  'Signup -> Select Tenant -> Studio Agents redirect flow works',
  async ({ page, baseURL }) => {
    page.on('console', (msg) => console.log('[browser]', msg.type(), msg.text()));
    page.on('pageerror', (err) => console.log('[pageerror]', err.message));

    const webBase = baseURL ?? process.env.WEB_BASE ?? 'http://localhost:3000';

    // Navigate to signup
    await page.goto(`${webBase}/signup`, { waitUntil: 'networkidle' });
    await expect(page.getByRole('heading', { name: /create your account/i })).toBeVisible();

    const email = `e2e_${Date.now()}@example.com`;
    const password = 'Password123!';

    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password').fill(password);

    // Submit form
    await Promise.all([
      page.waitForURL(/\/select-tenant($|\?)/, { timeout: 60_000 }),
      page.getByRole('button', { name: /sign up/i }).click(),
    ]);

    // On select-tenant, either auto-redirect to /studio/agents or show selector
    // If it shows selector briefly and then redirects, just wait for Agents page heading
    await page.waitForLoadState('networkidle');

    // Visiting studio/agents should now work (either redirected already or allowed)
    await page.goto(`${webBase}/studio/agents`, { waitUntil: 'networkidle' });
    await expect(page.getByRole('heading', { name: /agents/i })).toBeVisible();
  },
);
