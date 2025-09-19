import type { NextAuthOptions, Session, User as NextAuthUser } from 'next-auth';
import Credentials from 'next-auth/providers/credentials';
import { API_BASE } from './serverEnv';
import type { JWT } from 'next-auth/jwt';
import {
  computeBooleansForTenant,
  type FlagRole,
  type Membership as RolesMembership,
} from './roles';

type MembershipDto = { tenantId: string; tenantSlug: string; role: string; roles?: FlagRole[] };
type AppToken = JWT & {
  memberships?: MembershipDto[];
  tenant?: string;
  // Derived booleans for convenience (current tenant only)
  isAdmin?: boolean;
  canApprove?: boolean;
  canCreate?: boolean;
  isLearner?: boolean;
  rolesForTenant?: FlagRole[];
};
type AppSession = Session & {
  memberships?: MembershipDto[];
  tenant?: string;
  isAdmin?: boolean;
  canApprove?: boolean;
  canCreate?: boolean;
  isLearner?: boolean;
  rolesForTenant?: FlagRole[];
};

const AUTH_SECRET = process.env.AUTH_SECRET as string | undefined;
const ALLOW_INSECURE_COOKIES =
  (process.env.ALLOW_INSECURE_COOKIES ?? 'false').toLowerCase() === 'true';
const NEXTAUTH_URL = process.env.NEXTAUTH_URL ?? '';
const NEXTAUTH_SECRET = process.env.NEXTAUTH_SECRET as string | undefined;
const IS_LOCAL =
  NEXTAUTH_URL.startsWith('http://localhost') ||
  process.env.NODE_ENV !== 'production' ||
  ALLOW_INSECURE_COOKIES;
const RESOLVED_SECRET = AUTH_SECRET || NEXTAUTH_SECRET || (IS_LOCAL ? 'dev-secret' : undefined);
// Decide whether cookies must be Secure. In local prod runs over http, set ALLOW_INSECURE_COOKIES=true or use http NEXTAUTH_URL.
const COOKIE_SECURE =
  !ALLOW_INSECURE_COOKIES &&
  (NEXTAUTH_URL.startsWith('https://') || process.env.NODE_ENV === 'production');

if (!RESOLVED_SECRET) {
  console.warn(
    'Warning: NextAuth secret is not set. Set AUTH_SECRET or NEXTAUTH_SECRET in .env.local.',
  );
}

export const authOptions: NextAuthOptions & {
  authorize?: (credentials: Record<string, string> | undefined) => Promise<NextAuthUser | null>;
} = {
  secret: RESOLVED_SECRET,
  session: { strategy: 'jwt', maxAge: 60 * 60 * 24 * 7 },
  pages: { signIn: '/login' },
  cookies: {
    sessionToken: {
      name: `next-auth.session-token`,
      options: {
        httpOnly: true,
        sameSite: 'lax',
        path: '/',
        secure: COOKIE_SECURE,
      },
    },
    csrfToken: {
      name: `next-auth.csrf-token`,
      options: {
        httpOnly: false,
        sameSite: 'lax',
        path: '/',
        secure: COOKIE_SECURE,
      },
    },
  },
  providers: [
    Credentials({
      name: 'Credentials',
      credentials: {
        email: { label: 'Email', type: 'email' },
        password: { label: 'Password', type: 'password' },
        magicToken: { label: 'Magic Token', type: 'text' },
      },
      authorize: async (credentials) => authOptions.authorize?.(credentials) ?? null,
    }),
  ],
  callbacks: {
    async jwt({ token, user, trigger, session }) {
      const t = token as AppToken;
      const TRACE = (process.env.DEBUG_ROLE_TRACE ?? '').toLowerCase() === 'true';
      const srvTrace = (...args: unknown[]) => {
        if (TRACE) console.log('[auth][jwt][trace]', ...args);
      };
      if (user && (user as unknown as { memberships?: MembershipDto[] }).memberships) {
        t.memberships = (user as unknown as { memberships?: MembershipDto[] }).memberships;
        srvTrace('user.memberships(set)', t.memberships);
      }
      if (user?.email) t.email = user.email;
      // Fresh sign-in (user present) with multiple memberships: ensure we do NOT carry over any stale
      // tenant claim from a prior session (e.g., stale JWT survived signOut race) so that explicit
      // selection is always required. Single membership auto-select still handled below.
      if (user && t.memberships && t.memberships.length > 1) {
        delete t.tenant;
        srvTrace('multi-tenant sign-in -> clearing tenant claim');
      }
      // Auto-selection (LOW IMPACT RESTRICTION):
      // Only auto-select when there is exactly one membership. For multi-tenant users
      // we now require an explicit selection (session.update({ tenant }) or /select-tenant flow)
      // to avoid premature nav exposure before user intent.
      if (!t.tenant && t.memberships && t.memberships.length === 1) {
        t.tenant = t.memberships[0].tenantSlug;
      }
      // Respect session.update({ tenant }) calls from the client switcher
      if (trigger === 'update' && session && (session as unknown as { tenant?: string }).tenant) {
        t.tenant = (session as unknown as { tenant?: string }).tenant;
      }
      // Derive booleans on each JWT calc to keep them consistent with tenant selection
      const currentTenant = t.tenant ?? null;
      const { isAdmin, canApprove, canCreate, isLearner, roles } = computeBooleansForTenant(
        (t.memberships as unknown as RolesMembership[]) ?? [],
        currentTenant,
      );
      t.isAdmin = isAdmin;
      t.canApprove = canApprove;
      t.canCreate = canCreate;
      t.isLearner = isLearner;
      t.rolesForTenant = roles;
      srvTrace('derived', { currentTenant, roles, isAdmin, canApprove, canCreate, isLearner });
      return t;
    },
    async session({ session, token }) {
      const s = session as AppSession;
      const t = token as AppToken;
      const TRACE = (process.env.DEBUG_ROLE_TRACE ?? '').toLowerCase() === 'true';
      const srvTrace = (...args: unknown[]) => {
        if (TRACE) console.log('[auth][session][trace]', ...args);
      };
      if (t?.email && s.user) s.user.email = t.email as string;
      s.memberships = t.memberships ?? [];
      s.tenant = t.tenant;
      s.isAdmin = t.isAdmin;
      s.canApprove = t.canApprove;
      s.canCreate = t.canCreate;
      s.isLearner = t.isLearner;
      s.rolesForTenant = t.rolesForTenant;
      srvTrace('session-out', {
        tenant: s.tenant,
        memberships: s.memberships?.map((m) => ({
          tenantSlug: m.tenantSlug,
          tenantId: m.tenantId,
          role: m.role,
          rolesType: Array.isArray(m.roles) ? 'array' : typeof m.roles,
          rolesValue: m.roles,
        })),
        rolesForTenant: s.rolesForTenant,
        isAdmin: s.isAdmin,
      });
      return s;
    },
  },
  authorize: async (credentials) => {
    const magicToken = credentials?.magicToken?.toString().trim();
    // Magic Link mode: when magicToken is provided, skip password branch
    if (magicToken) {
      try {
        const res = await fetch(`${API_BASE}/api/auth/magic/consume`, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ token: magicToken }),
        });
        if (!res.ok) return null;
        const data = (await res.json()) as {
          user?: { id: string; email: string };
          memberships?: MembershipDto[];
          id?: string; // backward-compat if API ever returned flat shape
          email?: string;
        };
        const id = data.user?.id ?? data.id;
        const email = data.user?.email ?? data.email;
        if (!id || !email) return null;
        const u: NextAuthUser & { memberships?: MembershipDto[] } = {
          id,
          email,
          name: email,
          ...(data.memberships ? { memberships: data.memberships } : {}),
        } as NextAuthUser & { memberships?: MembershipDto[] };
        return u;
      } catch (err) {
        console.error('Magic login error', err);
        return null;
      }
    }

    // Password mode
    const email = credentials?.email?.toLowerCase().trim();
    const password = credentials?.password ?? '';
    if (!email || !password) return null;
    try {
      const res = await fetch(`${API_BASE}/api/auth/login`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ email, password }),
      });
      if (!res.ok) return null;
      const data = (await res.json()) as {
        id: string;
        email: string;
        memberships?: MembershipDto[];
      };
      const u: NextAuthUser & { memberships?: MembershipDto[] } = {
        id: data.id,
        email: data.email,
        name: data.email,
        ...(data.memberships ? { memberships: data.memberships } : {}),
      } as NextAuthUser & { memberships?: MembershipDto[] };
      return u;
    } catch (err) {
      console.error('Login error', err);
      return null;
    }
  },
};
