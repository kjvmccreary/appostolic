import type { CookieSetterOptions } from './cookieUtils';

type RotationBridgeEntry = {
  cookie: ProxyCookie;
  expiresAt: number;
};

// Allow refresh rotations to stay bridged long enough for users to navigate between pages or
// trigger follow-up actions (e.g., switching tenants again) without tripping reuse detection.
// Extend the bridge window so long-lived pages (e.g., admin settings left open for a while)
// continue to reuse the rotated refresh cookie until the browser receives a fresh one via a
// proxied response. One hour keeps memory bounded while surviving long idle periods.
const ROTATION_BRIDGE_TTL_MS = 60 * 60 * 1000;

const globalState = globalThis as typeof globalThis & {
  __appRefreshRotationBridge?: Map<string, RotationBridgeEntry>;
};

const rotationBridge =
  globalState.__appRefreshRotationBridge ??
  (globalState.__appRefreshRotationBridge = new Map<string, RotationBridgeEntry>());

export type ProxyCookie = {
  name: string;
  value: string;
  options: CookieSetterOptions;
};

export function pruneRotationBridge(now = Date.now()) {
  for (const [key, entry] of rotationBridge) {
    if (entry.expiresAt <= now) {
      rotationBridge.delete(key);
    }
  }
}

// Records the mapping from a previous refresh cookie value to its rotated successor so
// concurrent requests can reuse the new value without triggering reuse detection upstream.
export function registerRotation(previousValue: string, cookie: ProxyCookie) {
  pruneRotationBridge();
  rotationBridge.set(previousValue, {
    cookie,
    expiresAt: Date.now() + ROTATION_BRIDGE_TTL_MS,
  });
}

// Looks up the rotated refresh cookie for the provided previous value, if one has been
// registered within the rotation bridge window.
export function getRotation(previousValue: string | null): ProxyCookie | null {
  pruneRotationBridge();
  if (!previousValue) return null;
  const entry = rotationBridge.get(previousValue);
  if (!entry) return null;
  entry.expiresAt = Date.now() + ROTATION_BRIDGE_TTL_MS;
  rotationBridge.set(previousValue, entry);
  return entry.cookie;
}
