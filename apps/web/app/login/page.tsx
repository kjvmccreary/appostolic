import { Suspense } from 'react';
import LoginClient from './LoginClient';

export default function LoginPage() {
  return (
    <Suspense
      fallback={
        <main className="p-6">
          <h1>Sign in</h1>
        </main>
      }
    >
      <LoginClient />
    </Suspense>
  );
}
