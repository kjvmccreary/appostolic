import { test, expect } from '@playwright/test';

// Skip in CI unless explicitly enabled
const skipInCi = !!process.env.CI && !process.env.E2E_WEB_ENABLE;

(skipInCi ? test.skip : test)(
  'Magic Link: request → Mailhog → verify → signed in',
  async ({ page, baseURL }) => {
    page.on('console', (msg) => console.log('[browser]', msg.type(), msg.text()));
    page.on('pageerror', (err) => console.log('[pageerror]', err.message));

    const webBase = baseURL ?? process.env.WEB_BASE ?? 'http://localhost:3000';
    const mailhogBase = process.env.MAILHOG_BASE ?? 'http://localhost:8025';

    // Best-effort: if Mailhog is not up, skip gracefully
    try {
      const ping = await fetch(`${mailhogBase}/api/v2/messages?limit=1`, { method: 'GET' });
      if (!ping.ok) test.skip(true, 'Mailhog not reachable');
    } catch {
      test.skip(true, 'Mailhog not running on 8025');
    }

    // 1) Request a magic link
    await page.goto(`${webBase}/magic/request`, { waitUntil: 'networkidle' });
    const email = `ml_${Date.now()}@example.com`;
    await page.getByLabel(/email/i).fill(email);
    await Promise.all([
      page.waitForLoadState('networkidle'),
      page.getByRole('button', { name: /send link|request link|send/i }).click(),
    ]);

    // 2) Poll Mailhog for the message and extract the verify URL
    const verifyPath = await pollForVerifyPath({ mailhogBase, to: email, timeoutMs: 30000 });
    expect(verifyPath, 'Expected a magic verify link in Mailhog').toBeTruthy();

    const verifyUrl = verifyPath!.startsWith('http') ? verifyPath! : `${webBase}${verifyPath}`;

    // 3) Visit the verify URL which signs in; then visit select-tenant to trigger auto-select (single membership)
    await page.goto(verifyUrl, { waitUntil: 'networkidle' });
    await page.goto(`${webBase}/select-tenant?next=/studio/agents`, { waitUntil: 'networkidle' });

    // 4) Assert we can access Studio Agents (select-tenant auto-redirects via /api/tenant/select)
    await page.goto(`${webBase}/studio/agents`, { waitUntil: 'networkidle' });
    await expect(page.getByRole('heading', { name: /agents/i })).toBeVisible();
  },
);

async function pollForVerifyPath(opts: {
  mailhogBase: string;
  to: string;
  timeoutMs: number;
}): Promise<string | null> {
  const deadline = Date.now() + opts.timeoutMs;
  const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

  while (Date.now() < deadline) {
    try {
      const res = await fetch(`${opts.mailhogBase}/api/v2/messages`);
      if (res.ok) {
        const data = (await res.json()) as {
          items?: Array<{
            Content?: {
              Headers?: Record<string, string[] | string>;
              Body?: string;
            };
          }>;
        };
        const items = data?.items ?? [];
        for (const m of items) {
          const headers = m?.Content?.Headers ?? {};
          const toHeader = headers['To'];
          const toList = Array.isArray(toHeader)
            ? (toHeader as string[])
            : typeof toHeader === 'string'
              ? [toHeader]
              : [];
          const body = m?.Content?.Body ?? '';
          const addressed = toList.some((t) => t.toLowerCase().includes(opts.to.toLowerCase()));
          const match = body.match(/\/magic\/verify\?token=[A-Za-z0-9_-]+/);
          if (addressed && match) {
            return match[0];
          }
        }
      }
    } catch {
      // ignore and retry
    }
    await sleep(1000);
  }
  return null;
}
