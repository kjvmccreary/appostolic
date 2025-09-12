'use client';
import { useEffect } from 'react';
import { signOut } from 'next-auth/react';
import { useRouter } from 'next/navigation';

export default function LogoutPage() {
  const router = useRouter();
  useEffect(() => {
    (async () => {
      await signOut({ redirect: false });
      router.replace('/login');
    })();
  }, [router]);
  return null;
}
