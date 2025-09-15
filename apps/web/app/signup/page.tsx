import { Suspense } from 'react';
import SignupClient from './SignupClient';

export default function SignupPage() {
  return (
    <Suspense
      fallback={
        <main className="mx-auto max-w-md p-6">
          <h1 className="text-2xl font-semibold mb-4">Create your account</h1>
        </main>
      }
    >
      <SignupClient />
    </Suspense>
  );
}
