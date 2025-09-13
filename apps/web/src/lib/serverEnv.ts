export const API_BASE = process.env.NEXT_PUBLIC_API_BASE as string;
export const DEV_USER = process.env.DEV_USER as string | undefined;
export const DEV_TENANT = process.env.DEV_TENANT as string | undefined;
export const DEFAULT_TENANT =
  (process.env.DEFAULT_TENANT as string | undefined) ?? 'kevin-personal';

const WEB_AUTH_ENABLED = (process.env.WEB_AUTH_ENABLED ?? 'false').toLowerCase() === 'true';

function requireEnv(name: string, value: string | undefined) {
  if (!value || value.trim() === '') {
    throw new Error(
      `Missing required env var ${name}. Add it to .env.local (see .env.local.example).`,
    );
  }
}

requireEnv('NEXT_PUBLIC_API_BASE', API_BASE);

// Only require DEV headers when WEB_AUTH_ENABLED is false.
if (!WEB_AUTH_ENABLED) {
  requireEnv('DEV_USER', DEV_USER);
  requireEnv('DEV_TENANT', DEV_TENANT);
}
