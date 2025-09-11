import '@testing-library/jest-dom/vitest';

import { afterAll, afterEach, beforeAll } from 'vitest';
import { setupServer } from 'msw/node';

// You can add default handlers here or import from a central handlers file later
export const server = setupServer();

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }));
afterEach(() => server.resetHandlers());
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
