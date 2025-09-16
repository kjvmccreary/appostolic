import React from 'react';

// Route-level loading UI for /profile (Next.js convention)
export default function Loading() {
  return (
    <main
      id="main"
      className="container p-4 space-y-6"
      aria-busy="true"
      aria-labelledby="profile-heading"
    >
      <h1 id="profile-heading" className="text-xl font-semibold">
        Your Profile
      </h1>
      <section className="flex items-center gap-4">
        <div className="h-16 w-16 animate-pulse rounded-full border border-line bg-muted" />
        <div className="space-y-2">
          <div className="h-3 w-32 animate-pulse rounded bg-muted" />
          <div className="h-4 w-48 animate-pulse rounded bg-muted" />
        </div>
      </section>
    </main>
  );
}
