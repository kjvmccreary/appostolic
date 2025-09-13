import type { NextAuthOptions, Session, User as NextAuthUser } from 'next-auth';
import Credentials from 'next-auth/providers/credentials';
import { API_BASE } from './serverEnv';
import type { JWT } from 'next-auth/jwt';

type MembershipDto = { tenantId: string; tenantSlug: string; role: string };
type AppToken = JWT & { memberships?: MembershipDto[]; tenant?: string };
type AppSession = Session & { memberships?: MembershipDto[]; tenant?: string };

const AUTH_SECRET = process.env.AUTH_SECRET as string | undefined;

if (!AUTH_SECRET) {
  console.warn('Warning: AUTH_SECRET is not set. Set it in .env.local for secure sessions.');
}

export const authOptions: NextAuthOptions & {
  authorize?: (credentials: Record<string, string> | undefined) => Promise<NextAuthUser | null>;
} = {
  secret: AUTH_SECRET,
  session: { strategy: 'jwt', maxAge: 60 * 60 * 24 * 7 },
  pages: { signIn: '/login' },
  providers: [
    Credentials({
      name: 'Credentials',
      credentials: {
        email: { label: 'Email', type: 'email' },
        password: { label: 'Password', type: 'password' },
      },
      authorize: async (credentials) => authOptions.authorize?.(credentials) ?? null,
    }),
  ],
  callbacks: {
    async jwt({ token, user }) {
      const t = token as AppToken;
      if (user && (user as unknown as { memberships?: MembershipDto[] }).memberships) {
        t.memberships = (user as unknown as { memberships?: MembershipDto[] }).memberships;
      }
      if (user?.email) t.email = user.email;
      return t;
    },
    async session({ session, token }) {
      const s = session as AppSession;
      const t = token as AppToken;
      if (t?.email && s.user) s.user.email = t.email as string;
      s.memberships = t.memberships ?? [];
      s.tenant = t.tenant;
      return s;
    },
  },
  authorize: async (credentials) => {
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
