const isProduction = process.env.NODE_ENV === 'production';
const NEXTAUTH_URL = process.env.NEXTAUTH_URL ?? '';
const IS_LOCAL = !isProduction || NEXTAUTH_URL.startsWith('http://localhost');

// In development, provide a safe default API base if unset to avoid crashing pages that import auth.
export const API_BASE = (() => {
  const fromEnv = process.env.NEXT_PUBLIC_API_BASE as string | undefined;
  if (fromEnv && fromEnv.trim() !== '') return fromEnv;
  if (IS_LOCAL) return 'http://localhost:5198';
  // In true production without an explicit API base, throw a clear error
  throw new Error(
    'Missing NEXT_PUBLIC_API_BASE in production. Set it in the environment or .env.local.',
  );
})();
export const DEV_USER = process.env.DEV_USER as string | undefined;
export const DEV_TENANT = process.env.DEV_TENANT as string | undefined;
export const DEFAULT_TENANT =
  (process.env.DEFAULT_TENANT as string | undefined) ?? 'kevin-personal';

const WEB_AUTH_ENABLED = (process.env.WEB_AUTH_ENABLED ?? 'false').toLowerCase() === 'true';
const IS_SERVER = typeof window === 'undefined';

function requireEnv(name: string, value: string | undefined) {
  if (!value || value.trim() === '') {
    throw new Error(
      `Missing required env var ${name}. Add it to .env.local (see .env.local.example).`,
    );
  }
}

// We no longer hard-require API base when running locally; production path throws above.

// Only require DEV headers when WEB_AUTH_ENABLED is false AND on the server.
// Client bundles (e.g., /signup) shouldn't force DEV_* to be present.
// Only require DEV_* headers in production when WEB auth is disabled. In dev/local, allow running without these.
if (isProduction && !WEB_AUTH_ENABLED && IS_SERVER && !IS_LOCAL) {
  requireEnv('DEV_USER', DEV_USER);
  requireEnv('DEV_TENANT', DEV_TENANT);
}
