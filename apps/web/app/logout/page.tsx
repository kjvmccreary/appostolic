'use client';
import { useEffect } from 'react';
import { signOut } from 'next-auth/react';
import { useRouter } from 'next/navigation';

export default function LogoutPage() {
  const router = useRouter();
  useEffect(() => {
    (async () => {
      await signOut({ redirect: false });
      // Include a flag so middleware doesn't bounce back to the app while cookies settle
      router.replace('/login?loggedOut=1');
    })();
  }, [router]);
  return null;
}
