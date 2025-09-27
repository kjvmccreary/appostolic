import type { CookieSetterOptions } from './cookieUtils';

type RotationBridgeEntry = {
  cookie: ProxyCookie;
  expiresAt: number;
};

// Allow refresh rotations to stay bridged long enough for users to navigate between pages or
// trigger follow-up actions (e.g., switching tenants again) without tripping reuse detection.
// 5 minutes keeps the mapping available for typical UX flows while still pruning automatically.
const ROTATION_BRIDGE_TTL_MS = 5 * 60 * 1000;

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
  return rotationBridge.get(previousValue)?.cookie ?? null;
}
