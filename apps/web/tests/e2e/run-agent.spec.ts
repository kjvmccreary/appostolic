import { test, expect } from '@playwright/test';

// Skip in CI unless explicitly enabled
const skipInCi = !!process.env.CI && !process.env.E2E_WEB_ENABLE;

(skipInCi ? test.skip : test)(
  'Run Agent page shows tokens/cost and traces after completion',
  async ({ page, baseURL }) => {
    page.on('console', (msg) => console.log('[browser]', msg.type(), msg.text()));
    page.on('pageerror', (err) => console.log('[pageerror]', err.message));
    page.on('requestfailed', (req) =>
      console.log('[requestfailed]', req.url(), req.failure()?.errorText),
    );

    const webBase = baseURL ?? process.env.WEB_BASE ?? 'http://localhost:3000';

    // Navigate to the Run Agent page
    await page.goto(`${webBase}/dev/agents`, { waitUntil: 'networkidle' });
    await expect(page.getByRole('heading', { name: /Run Agent \(Dev\)/i })).toBeVisible();

    // Select ResearchAgent in the dropdown (assumes it exists in dev seed)
    const agentSelect = page.locator('#agentSelect');
    await expect(agentSelect).toBeVisible();
    // Try by visible label first, falling back to deterministic ID
    try {
      await agentSelect.selectOption({ label: 'ResearchAgent' });
    } catch {
      await agentSelect.selectOption({ value: '11111111-1111-1111-1111-111111111111' });
    }

    // Capture any alert dialogs for easier debugging of failures
    let alertMessage: string | null = null;
    page.on('dialog', async (dialog) => {
      alertMessage = dialog.message();
      await dialog.accept();
    });

    // Submit default input
    const runButton = page.getByRole('button', { name: /run/i });
    await expect(runButton).toBeEnabled();
    const [postRes] = await Promise.all([
      page.waitForResponse(
        (res) => res.request().method() === 'POST' && res.url().includes('/api-proxy/agent-tasks'),
        { timeout: 30_000 },
      ),
      runButton.click(),
    ]);
    if (postRes.status() !== 201) {
      const body = await postRes.text();
      throw new Error(
        `POST /api-proxy/agent-tasks -> ${postRes.status()} body=${body} alert=${alertMessage ?? ''}`,
      );
    }

    // Traces section appears once a taskId is set
    await expect(page.getByRole('heading', { name: 'Traces' })).toBeVisible({ timeout: 30_000 });

    // Wait for status badge to reach a terminal state, preferring Succeeded
    const statusSucceeded = page.locator('span', { hasText: 'Succeeded' });
    const statusFailed = page.locator('span', { hasText: 'Failed' });
    await Promise.race([
      statusSucceeded.waitFor({ state: 'visible', timeout: 120_000 }),
      statusFailed.waitFor({ state: 'visible', timeout: 120_000 }),
    ]);

    // Allow up to 60s for completion in CI/dev; badge is a span with the final status text
    const terminalBadge = page.locator('span', { hasText: /Succeeded|Failed/ }).first();
    await expect(terminalBadge).toBeVisible({ timeout: 60_000 });

    // Token badges should render with totals > 0
    const totalTokensBadge = page.locator('text=Total tokens:');
    await expect(totalTokensBadge).toBeVisible();
    const totalTokensText = await totalTokensBadge.textContent();
    expect(totalTokensText).toBeTruthy();
    const totalTokenValue = Number(totalTokensText!.replace(/[^0-9.]/g, ''));
    expect(totalTokenValue).toBeGreaterThan(0);

    // If backend pricing is enabled, Est. cost badge should be visible and > $0
    const costLine = page.getByText(/^Est\. cost:/).first();
    if (await costLine.isVisible()) {
      const raw = (await costLine.innerText())?.trim() ?? '';
      // Extract numeric value like $0.0552
      const match = raw.match(/Est\. cost:\s*\$([0-9]+(?:\.[0-9]+)?)/);
      expect(match).toBeTruthy();
      const costValue = Number(match![1]);
      expect(costValue).toBeGreaterThan(0);
    }

    // Traces table should have at least one Model and one Tool row with non-zero durationMs
    const modelCell = page.getByRole('cell', { name: 'Model', exact: true }).first();
    await expect(modelCell).toBeVisible();
    const toolCell = page.getByRole('cell', { name: 'Tool', exact: true }).first();
    await expect(toolCell).toBeVisible();

    // Check at least one duration cell has a non-zero number
    const durationCells = page.locator('table td:nth-child(4)');
    const durations = await durationCells.allTextContents();
    const numericDurations = durations.map((d) => Number(d.trim())).filter((n) => !Number.isNaN(n));
    expect(numericDurations.some((n) => n > 0)).toBeTruthy();
  },
);
