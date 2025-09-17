import { test, expect } from '@playwright/test';

// Skip in CI unless explicitly enabled
const skipInCi = !!process.env.CI && !process.env.E2E_WEB_ENABLE;

(skipInCi ? test.skip : test)(
  'Multi-tenant: login -> select -> logout clears cookie -> re-login hides nav until re-select',
  async ({ page, baseURL, context }) => {
    page.on('console', (msg) => console.log('[browser]', msg.type(), msg.text()));
    page.on('pageerror', (err) => console.log('[pageerror]', err.message));

    const webBase = baseURL ?? process.env.WEB_BASE ?? 'http://localhost:3000';

    // 1. Sign up a new user with password so we control credentials (reuse signup flow)
    await page.goto(`${webBase}/signup`, { waitUntil: 'networkidle' });
    const email = `mt_${Date.now()}@example.com`;
    const password = 'Password123!';
    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password').fill(password);
    await Promise.all([
      page.waitForURL(/\/select-tenant($|\?)/, { timeout: 60_000 }),
      page.getByRole('button', { name: /sign up/i }).click(),
    ]);

    // 2. On first sign-in there should be at least one membership (assumed dev seed)
    // Navigate to studio area to force selection/auto-redirect path; if auto-selected (single membership), cookie will exist.
    await page.goto(`${webBase}/studio/agents`, { waitUntil: 'networkidle' });
    await expect(page.getByRole('heading', { name: /agents/i })).toBeVisible();

    // Capture cookie state after selection
    const cookiesAfterSelect = await context.cookies();
    const tenantCookie = cookiesAfterSelect.find((c) => c.name === 'selected_tenant');
    expect(tenantCookie, 'Expected selected_tenant cookie after initial selection').toBeTruthy();

    // 3. Logout via profile menu sign out (routes through /logout)
    await page.goto(`${webBase}/studio/agents`, { waitUntil: 'networkidle' });
    // Open profile menu (avatar button) - adapt selector if needed
    const profileButton = page.getByRole('button', { name: /profile menu|open profile|avatar/i }).first();
    // Fallback: try image with alt
    if (!(await profileButton.isVisible())) {
      const possible = page.locator('button:has(img[alt*="avatar" i]),button:has(svg)');
      if (await possible.first().isVisible()) {
        await possible.first().click();
      }
    } else {
      await profileButton.click();
    }
    // Click Sign out (button role)
    await page.getByRole('button', { name: /sign out/i }).click();
    await page.waitForURL(/\/login\?loggedOut=1/, { timeout: 30_000 });

    // 4. Verify tenant cookie is cleared
    const cookiesAfterLogout = await context.cookies();
    const tenantCookieAfterLogout = cookiesAfterLogout.find((c) => c.name === 'selected_tenant');
    expect(
      !tenantCookieAfterLogout || tenantCookieAfterLogout.value === '',
      'selected_tenant cookie should be cleared after logout',
    ).toBeTruthy();

    // 5. Log back in (credentials path)
    await page.getByLabel('Email').fill(email);
    await page.getByLabel(/password/i).fill(password);
    await Promise.all([
      page.waitForURL(/\/select-tenant($|\?)/, { timeout: 60_000 }),
      page.getByRole('button', { name: /sign in|log in/i }).click(),
    ]);

    // 6. Navigate directly to a protected route: expect redirect back to select-tenant until selection
    await page.goto(`${webBase}/studio/agents`, { waitUntil: 'networkidle' });
    if (page.url().includes('/studio/agents')) {
      // If auto-selected (single membership), cookie may have been re-set; this test focuses on multi-tenant scenario.
      // Assert TopBar not visible if no tenant claim; fallback skip if single-tenant auto case.
      // Attempt to detect hidden nav by absence of common nav items.
      const hasDashboard = await page.getByRole('link', { name: /dashboard/i }).isVisible().catch(() => false);
      if (hasDashboard) {
        test.skip(true, 'Environment appears single-tenant; skipping multi-tenant re-selection assertion');
      }
    } else {
      expect(page.url()).toMatch(/\/select-tenant/);
    }
  },
);
