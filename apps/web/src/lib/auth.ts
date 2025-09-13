import type { NextAuthOptions, User as NextAuthUser } from 'next-auth';
import Credentials from 'next-auth/providers/credentials';
import { API_BASE } from './serverEnv';

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
      if (user?.email) token.email = user.email;
      return token;
    },
    async session({ session, token }) {
      if (token?.email && session.user) session.user.email = token.email as string;
      return session;
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
      const data = (await res.json()) as { id: string; email: string };
      return { id: data.id, email: data.email, name: data.email } as NextAuthUser;
    } catch (err) {
      console.error('Login error', err);
      return null;
    }
  },
};
