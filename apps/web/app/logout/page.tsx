'use client';
import { useEffect } from 'react';
import { signOut } from 'next-auth/react';
import { useRouter } from 'next/navigation';

export default function LogoutPage() {
  const router = useRouter();
  useEffect(() => {
    (async () => {
      await signOut({ redirect: false });
      // Proactively clear the tenant selection cookie so a subsequent multi-tenant login
      // does not render the TopBar before an explicit selection.
      try {
        document.cookie = 'selected_tenant=; Path=/; Max-Age=0; SameSite=Lax';
      } catch {
        // ignore cookie clear errors
      }
      // Include a flag so middleware doesn't bounce back to the app while cookies settle
      router.replace('/login?loggedOut=1');
    })();
  }, [router]);
  return null;
}
