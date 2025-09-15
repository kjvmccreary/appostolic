import { Suspense } from 'react';
import MagicVerifyClient from './MagicVerifyClient';

export default function MagicVerifyPage() {
  return (
    <Suspense
      fallback={
        <main className="mx-auto max-w-md p-6">
          <h1 className="text-2xl font-semibold mb-4">Verifying magic link…</h1>
        </main>
      }
    >
      <MagicVerifyClient />
    </Suspense>
  );
}
