export type CookieSetterOptions = {
  path?: string;
  httpOnly?: boolean;
  secure?: boolean;
  sameSite?: 'strict' | 'lax' | 'none';
  expires?: Date;
  maxAge?: number;
  domain?: string;
};

type ParsedCookie = {
  name: string;
  value: string;
  options: CookieSetterOptions;
};

function safeSplitCookies(header: string): string[] {
  const normalized = header.replace(/\r?\n/g, ',');
  const parts = normalized
    .split(/,(?=[^;,]+=)/)
    .map((part) => part.trim())
    .filter(Boolean);
  return parts.length > 0 ? parts : [header.trim()];
}

export function extractSetCookieValues(headers: Headers): string[] {
  const getSetCookie = (headers as unknown as { getSetCookie?: () => string[] }).getSetCookie;
  if (typeof getSetCookie === 'function') {
    const values = getSetCookie.call(headers);
    if (Array.isArray(values) && values.length > 0) return values;
  }
  const fallback = headers.get('set-cookie');
  if (!fallback) return [];
  return safeSplitCookies(fallback);
}

export function parseSetCookie(header: string): ParsedCookie | null {
  const segments = header
    .split(';')
    .map((part) => part.trim())
    .filter(Boolean);
  if (segments.length === 0) return null;
  const [nameValue, ...attrs] = segments;
  const eqIndex = nameValue.indexOf('=');
  if (eqIndex <= 0) return null;
  const name = nameValue.slice(0, eqIndex).trim();
  const value = nameValue.slice(eqIndex + 1);
  const options: CookieSetterOptions = {};
  for (const attr of attrs) {
    const [rawKey, ...rawValParts] = attr.split('=');
    const key = rawKey.trim().toLowerCase();
    const val = rawValParts.join('=');
    switch (key) {
      case 'path':
        options.path = val || '/';
        break;
      case 'expires':
        if (val) {
          const date = new Date(val);
          if (!Number.isNaN(date.valueOf())) options.expires = date;
        }
        break;
      case 'max-age':
        options.maxAge = val ? Number(val) : undefined;
        break;
      case 'samesite':
        if (val) {
          const lower = val.toLowerCase();
          if (lower === 'lax' || lower === 'strict' || lower === 'none') {
            options.sameSite = lower as CookieSetterOptions['sameSite'];
          }
        }
        break;
      case 'secure':
        options.secure = true;
        break;
      case 'httponly':
        options.httpOnly = true;
        break;
      case 'domain':
        if (val) options.domain = val;
        break;
      default:
        break;
    }
  }
  return { name, value, options };
}
