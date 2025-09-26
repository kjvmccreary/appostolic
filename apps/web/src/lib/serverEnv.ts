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
export const DEFAULT_TENANT =
  (process.env.DEFAULT_TENANT as string | undefined) ?? 'kevin-personal';

// We no longer hard-require API base when running locally; production path throws above.

// No additional runtime assertions required; development headers have been removed.
