import { getServerSession } from 'next-auth';
import { redirect } from 'next/navigation';
import { authOptions } from '../src/lib/auth';
import React from 'react';

/**
 * Root Dashboard Page
 * --------------------
 * Replaces the earlier redirect-only root with a real authenticated dashboard surface
 * per UI Spec (§Dashboard) so the "Dashboard" nav item reveals meaningful content
 * instead of bouncing users into /studio/agents.
 *
 * Behavior:
 *  - Unauthenticated: server-side redirect → /login (no dashboard HTML leaks)
 *  - Authenticated: render dashboard sections (Quick Start, Recent Lessons, Plan & Usage,
 *    Templates, Guardrails, Marketplace) using semantic <section> regions each
 *    labeled by an <h2>. Content is placeholder/mocked until data wiring stories land.
 *
 * Accessibility:
 *  - Main landmark id="main"; each section has aria-labelledby tying to its heading.
 *  - Headings follow a consistent scale (h1 2xl, h2 lg) mirroring /profile styling.
 *
 * Future data wiring (deferred):
 *  - Recent Lessons: fetch last N lessons for tenant
 *  - Plan & Usage: surface metered usage / billing plan summary
 *  - Templates: curated starter templates (link to /templates)
 *  - Guardrails: quick status summary (link to /guardrails)
 *  - Marketplace: placeholder for future extensions/add-ons
 */
export default async function RootPage() {
  const session = await getServerSession(authOptions).catch(() => null);
  if (!session?.user?.email) {
    redirect('/login');
  }

  return (
    <main
      id="main"
      className="mx-auto max-w-5xl p-6 space-y-10"
      aria-labelledby="dashboard-heading"
    >
      <header className="space-y-3">
        <h1 id="dashboard-heading" className="text-2xl font-semibold">
          Dashboard
        </h1>
        <p className="text-sm text-muted max-w-prose">
          Central hub for getting started, tracking progress, and exploring tools. Sections below
          will populate with your recent activity and tenant usage metrics.
        </p>
      </header>

      {/* Quick Start */}
      <section
        aria-labelledby="quick-start-heading"
        className="rounded-lg border border-line bg-[var(--color-canvas)] p-6 shadow-sm space-y-4"
      >
        <h2 id="quick-start-heading" className="text-lg font-medium">
          Quick Start
        </h2>
        <ul className="list-disc pl-5 text-sm space-y-1">
          <li>
            <a className="text-accent hover:underline" href="/shepherd/step1">
              Run the Shepherd (guided setup)
            </a>
          </li>
          <li>
            <a className="text-accent hover:underline" href="/editor">
              Create a new Lesson
            </a>
          </li>
          <li>
            <a className="text-accent hover:underline" href="/studio/agents">
              Manage your Agents
            </a>
          </li>
        </ul>
      </section>

      {/* Recent Lessons */}
      <section
        aria-labelledby="recent-lessons-heading"
        className="rounded-lg border border-line bg-[var(--color-canvas)] p-6 shadow-sm space-y-4"
      >
        <h2 id="recent-lessons-heading" className="text-lg font-medium">
          Recent Lessons
        </h2>
        <p className="text-sm text-muted">
          No recent lessons yet. Your latest work will appear here.
        </p>
      </section>

      {/* Plan & Usage */}
      <section
        aria-labelledby="plan-usage-heading"
        className="rounded-lg border border-line bg-[var(--color-canvas)] p-6 shadow-sm space-y-4"
      >
        <h2 id="plan-usage-heading" className="text-lg font-medium">
          Plan &amp; Usage
        </h2>
        <p className="text-sm text-muted">Billing plan and usage metrics coming soon.</p>
      </section>

      {/* Templates */}
      <section
        aria-labelledby="templates-heading"
        className="rounded-lg border border-line bg-[var(--color-canvas)] p-6 shadow-sm space-y-4"
      >
        <h2 id="templates-heading" className="text-lg font-medium">
          Templates
        </h2>
        <p className="text-sm text-muted">
          Explore starter lesson &amp; agent templates.{' '}
          <a href="/templates" className="text-accent hover:underline">
            View all templates
          </a>
          .
        </p>
      </section>

      {/* Guardrails */}
      <section
        aria-labelledby="guardrails-heading"
        className="rounded-lg border border-line bg-[var(--color-canvas)] p-6 shadow-sm space-y-4"
      >
        <h2 id="guardrails-heading" className="text-lg font-medium">
          Guardrails
        </h2>
        <p className="text-sm text-muted">
          Define doctrinal alignment, authors/books allowlists, and safe generation boundaries.{' '}
          <a href="/guardrails" className="text-accent hover:underline">
            Manage guardrails
          </a>
          .
        </p>
      </section>

      {/* Marketplace */}
      <section
        aria-labelledby="marketplace-heading"
        className="rounded-lg border border-line bg-[var(--color-canvas)] p-6 shadow-sm space-y-4"
      >
        <h2 id="marketplace-heading" className="text-lg font-medium">
          Marketplace
        </h2>
        <p className="text-sm text-muted">
          Extensions &amp; integrations will appear here. Roadmap item — stay tuned.
        </p>
      </section>
    </main>
  );
}
