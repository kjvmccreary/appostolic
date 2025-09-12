import type { NextAuthOptions, User as NextAuthUser } from 'next-auth';
import Credentials from 'next-auth/providers/credentials';
import { verifyPassword } from './hash';

const AUTH_SECRET = process.env.AUTH_SECRET as string | undefined;
const SEED_EMAIL = process.env.AUTH_SEED_EMAIL as string | undefined;
const SEED_PASSWORD = process.env.AUTH_SEED_PASSWORD as string | undefined;
const SEED_PASSWORD_HASH = process.env.AUTH_SEED_PASSWORD_HASH as string | undefined;

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
    if (!SEED_EMAIL) return null;
    if (email !== SEED_EMAIL.toLowerCase()) return null;

    // Prefer verifying against a provided hash; otherwise verify plain match as dev fallback
    if (SEED_PASSWORD_HASH) {
      const ok = await verifyPassword(password, SEED_PASSWORD_HASH);
      if (!ok) return null;
    } else if (SEED_PASSWORD) {
      if (password !== SEED_PASSWORD) return null;
    } else {
      return null;
    }

    return { id: email, name: email, email } as NextAuthUser;
  },
};
