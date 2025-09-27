import { NextRequest, NextResponse } from 'next/server';
import { getServerSession } from 'next-auth';
import { cookies } from 'next/headers';
import { authOptions } from '../../../../src/lib/auth';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { extractSetCookieValues, parseSetCookie } from '../../../../src/lib/cookieUtils';
import {
  registerRotation,
  getRotation,
  type ProxyCookie as RotationBridgeCookie,
} from '../../../../src/lib/refreshRotationBridge';

type ParsedCookie = NonNullable<ReturnType<typeof parseSetCookie>>;

type CookieInstruction = {
  name: string;
  value: string;
  options?: ParsedCookie['options'];
};

type RotationSuccess = {
  cookies: CookieInstruction[];
};

type RotationFailure = {
  status: number;
  body: Record<string, unknown>;
  cookies: CookieInstruction[];
};

function replaceCookieValue(header: string, name: string, value: string): string {
  const parts = header
    .split(';')
    .map((part) => part.trim())
    .filter(Boolean);
  let replaced = false;
  const nextParts = parts.map((part) => {
    if (part.startsWith(`${name}=`)) {
      replaced = true;
      return `${name}=${value}`;
    }
    return part;
  });
  if (!replaced) {
    nextParts.push(`${name}=${value}`);
  }
  return nextParts.join('; ');
}

async function rotateRefreshCookie(
  req: NextRequest,
  tenantSlug: string,
): Promise<RotationSuccess | { failure: RotationFailure }> {
  const refreshCookie = req.cookies.get('rt')?.value;
  if (!refreshCookie) {
    return {
      failure: {
        status: 400,
        body: { code: 'missing_refresh' },
        cookies: [
          {
            name: 'rt',
            value: '',
            options: { path: '/', maxAge: 0 },
          },
        ],
      },
    };
  }

  const cookieHeader = req.headers.get('cookie');
  if (!cookieHeader) {
    return {
      failure: {
        status: 400,
        body: { code: 'missing_refresh' },
        cookies: [
          {
            name: 'rt',
            value: '',
            options: { path: '/', maxAge: 0 },
          },
        ],
      },
    };
  }

  const nextCookies = cookies();
  const refreshUrl = new URL('/api/auth/refresh', API_BASE);
  if (tenantSlug) {
    refreshUrl.searchParams.set('tenant', tenantSlug);
  }

  const forwardedProto = req.headers.get('x-forwarded-proto');
  const forwardedFor = req.headers.get('x-forwarded-for');
  const sessionFp = req.headers.get('x-session-fp');
  const sessionDevice = req.headers.get('x-session-device');

  const collectCookies = (headerList: string[]): CookieInstruction[] => {
    const instructions: CookieInstruction[] = [];
    for (const entry of headerList) {
      const parsed = parseSetCookie(entry);
      if (parsed) {
        nextCookies.set(parsed.name, parsed.value, parsed.options);
        instructions.push({ name: parsed.name, value: parsed.value, options: parsed.options });
      }
    }
    return instructions;
  };

  const attemptRefresh = async (
    bridge: RotationBridgeCookie | null,
    allowRetry: boolean,
  ): Promise<RotationSuccess | { failure: RotationFailure }> => {
    const activeBridge = bridge ?? null;
    if (activeBridge) {
      try {
        nextCookies.set(activeBridge.name, activeBridge.value, activeBridge.options ?? {});
      } catch {
        // Ignore inability to mutate cookies in restricted contexts; retry will rely on headers.
      }
    }

    const effectiveCookieHeader = activeBridge
      ? replaceCookieValue(cookieHeader, activeBridge.name, activeBridge.value)
      : cookieHeader;
    const effectiveRefresh = activeBridge?.value ?? refreshCookie;

    const outgoingHeaders: Record<string, string> = {
      cookie: effectiveCookieHeader,
    };
    if (forwardedProto) outgoingHeaders['x-forwarded-proto'] = forwardedProto;
    if (forwardedFor) outgoingHeaders['x-forwarded-for'] = forwardedFor;
    if (sessionFp) outgoingHeaders['x-session-fp'] = sessionFp;
    if (sessionDevice) outgoingHeaders['x-session-device'] = sessionDevice;

    let response: Response;
    try {
      response = await fetch(refreshUrl.toString(), {
        method: 'POST',
        headers: outgoingHeaders,
        cache: 'no-store',
      });
    } catch (err) {
      return {
        failure: {
          status: 502,
          body: {
            code: 'refresh_proxy_error',
            message: err instanceof Error ? err.message : String(err),
          },
          cookies: [],
        },
      };
    }

    if (!response.ok) {
      let parsed: Record<string, unknown> | null = null;
      try {
        const text = await response.text();
        if (text) parsed = JSON.parse(text) as Record<string, unknown>;
      } catch {
        parsed = null;
      }
      const body = parsed ?? { code: 'refresh_failed' };
      const code = typeof body.code === 'string' ? body.code : null;

      if (allowRetry && code === 'refresh_reuse') {
        const retryCandidates = [activeBridge?.value ?? null, refreshCookie].filter(
          (candidate): candidate is string => Boolean(candidate),
        );
        for (const candidate of retryCandidates) {
          const retryBridge = getRotation(candidate);
          if (retryBridge && retryBridge.value !== activeBridge?.value) {
            return attemptRefresh(retryBridge, false);
          }
        }
      }

      const clearCookies: CookieInstruction[] = [];
      if (code === 'refresh_reuse' || code === 'refresh_invalid' || code === 'missing_refresh') {
        clearCookies.push({ name: 'rt', value: '', options: { path: '/', maxAge: 0 } });
        const sessionCookieNames = [
          authOptions.cookies?.sessionToken?.name,
          '__Secure-next-auth.session-token',
          'next-auth.session-token',
        ].filter(Boolean) as string[];
        for (const name of sessionCookieNames) {
          clearCookies.push({ name, value: '', options: { path: '/', maxAge: 0 } });
        }
      }
      if (clearCookies.length > 0) {
        for (const cookie of clearCookies) {
          nextCookies.set(cookie.name, cookie.value, cookie.options ?? {});
        }
      }
      const setCookies = collectCookies(extractSetCookieValues(response.headers));
      return {
        failure: {
          status: response.status,
          body,
          cookies: setCookies.length > 0 ? setCookies : clearCookies,
        },
      };
    }

    const setCookies = collectCookies(extractSetCookieValues(response.headers));
    const rotated = setCookies.find((cookie) => cookie.name === 'rt');
    if (effectiveRefresh && rotated) {
      const bridgeCookie: RotationBridgeCookie = {
        name: rotated.name,
        value: rotated.value,
        options: rotated.options ?? {},
      };
      registerRotation(effectiveRefresh, bridgeCookie);
    }

    try {
      await response.arrayBuffer();
    } catch {
      // ignore drain errors
    }

    return { cookies: setCookies };
  };

  return attemptRefresh(getRotation(refreshCookie), true);
}

export const runtime = 'nodejs';

// Minimal server route to set a cookie for selected tenant.
export async function POST(req: NextRequest) {
  const body = (await req.json().catch(() => ({}))) as { tenant?: string; slug?: string };
  const candidate = (body.tenant || body.slug || '').trim();
  if (!candidate) {
    return NextResponse.json({ error: 'Invalid tenant' }, { status: 400 });
  }
  // Fetch session to validate that the requested tenant slug/id exists in memberships.
  const session = (await getServerSession(authOptions).catch(() => null)) as null | {
    memberships?: { tenantId: string; tenantSlug: string }[];
  };
  const memberships = session?.memberships || [];
  const match = memberships.find((m) => m.tenantSlug === candidate || m.tenantId === candidate);
  if (!match) {
    return NextResponse.json({ error: 'Invalid tenant' }, { status: 400 });
  }
  const value = match.tenantSlug; // always store canonical slug in cookie
  const rotation = await rotateRefreshCookie(req, value);
  if ('failure' in rotation) {
    const failureResponse = NextResponse.json(rotation.failure.body, {
      status: rotation.failure.status,
    });
    for (const cookie of rotation.failure.cookies) {
      failureResponse.cookies.set(cookie.name, cookie.value, cookie.options ?? {});
    }
    return failureResponse;
  }

  const res = NextResponse.json({ ok: true, tenant: value });
  const isHttps =
    req.headers.get('x-forwarded-proto') === 'https' || req.nextUrl.protocol === 'https:';
  res.cookies.set('selected_tenant', value, {
    path: '/',
    httpOnly: true,
    sameSite: 'lax',
    maxAge: 60 * 60 * 24 * 7,
    secure: isHttps,
  });
  for (const cookie of rotation.cookies) {
    res.cookies.set(cookie.name, cookie.value, cookie.options);
  }
  return res;
}

// Support GET for convenience: /api/tenant/select?tenant=slug&next=/studio
export async function GET(req: NextRequest) {
  const url = new URL(req.url);
  const candidate = url.searchParams.get('tenant')?.trim() || '';
  const DEFAULT_NEXT = '/studio/agents';
  const rawNext = url.searchParams.get('next') || DEFAULT_NEXT;
  const next = rawNext.startsWith('/') && !rawNext.startsWith('//') ? rawNext : DEFAULT_NEXT;
  if (!candidate) {
    return NextResponse.json({ error: 'Invalid tenant' }, { status: 400 });
  }
  const session = (await getServerSession(authOptions).catch(() => null)) as null | {
    memberships?: { tenantId: string; tenantSlug: string }[];
  };
  const memberships = session?.memberships || [];
  const match = memberships.find((m) => m.tenantSlug === candidate || m.tenantId === candidate);
  if (!match) {
    return NextResponse.json({ error: 'Invalid tenant' }, { status: 400 });
  }
  const value = match.tenantSlug;
  const res = NextResponse.redirect(new URL(next, url.origin));
  const isHttps =
    req.headers.get('x-forwarded-proto') === 'https' || req.nextUrl.protocol === 'https:';
  res.cookies.set('selected_tenant', value, {
    path: '/',
    httpOnly: true,
    sameSite: 'lax',
    maxAge: 60 * 60 * 24 * 7,
    secure: isHttps,
  });
  return res;
}
