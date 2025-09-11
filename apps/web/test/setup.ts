import '@testing-library/jest-dom/vitest';
import 'whatwg-fetch';

import { afterAll, afterEach, beforeAll } from 'vitest';
import { setupServer } from 'msw/node';

// You can add default handlers here or import from a central handlers file later
export const server = setupServer();

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());
