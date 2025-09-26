import '@testing-library/jest-dom/vitest';

import { afterAll, afterEach, beforeAll } from 'vitest';
import { setupServer } from 'msw/node';
import { authHandlers, resetAuthMocks } from './fixtures/mswAuthHandlers';

export const server = setupServer(...authHandlers);

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }));
afterEach(() => {
  server.resetHandlers();
  resetAuthMocks();
});
afterAll(() => server.close());

// Expose for tests without importing from outside src
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(globalThis as any).__mswServer = server;

// Set a default base URL so relative fetch('/api-proxy/...') resolves
// JSDOM requires explicit URL to construct absolute URLs
if (!('location' in globalThis)) {
  // @ts-expect-error set jsdom location for tests
  globalThis.location = new URL('http://localhost');
}

// Ensure server-only fetch helper builds absolute URLs matching MSW handlers
process.env.NEXT_PUBLIC_WEB_BASE = 'http://localhost';

// jsdom does not implement URL.createObjectURL; stub for tests that generate previews
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (!(URL as any).createObjectURL) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (URL as any).createObjectURL = () => 'blob:mock';
}
