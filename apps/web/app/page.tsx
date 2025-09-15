import React from 'react';
import Link from 'next/link';
import { Card } from '../src/components/ui/Card';
import { ActionTile } from '../src/components/ui/ActionTile';
import { Chip } from '../src/components/ui/Chip';

export default function DashboardPage() {
  const recent = [
    { id: 'l1', title: 'Sermon on the Mount', status: 'draft' as const },
    { id: 'l2', title: 'Paul’s Letters Overview', status: 'slides' as const },
    { id: 'l3', title: 'Acts 2: Pentecost', status: 'handout' as const },
  ];

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <header className="mb-4">
        <h1 className="text-ink text-xl font-semibold">Dashboard</h1>
        <p className="text-muted text-sm">Quick start and recent activity</p>
      </header>

      <section aria-label="Quick Start" className="mb-6">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          <ActionTile
            href="/wizard/step1"
            title="Start Wizard"
            description="Create a new lesson with guided steps"
            cta="Create Lesson"
          />
          <ActionTile
            href="/editor"
            title="Open Editor"
            description="Tweak your latest draft directly"
            cta="Open Editor"
          />
          <ActionTile
            href="/studio/agents"
            title="Studio"
            description="Developer tools and agents"
            cta="Open Studio"
          />
        </div>
      </section>

      <section aria-label="Recent Lessons" className="mb-6">
        <Card title="Recent Lessons" description="Your most recent drafts and outputs">
          <ul className="mt-2 space-y-2">
            {recent.map((r) => (
              <li key={r.id} className="flex items-center justify-between">
                <Link
                  href={`/editor?id=${r.id}`}
                  className="text-sm text-ink underline-offset-2 hover:underline"
                >
                  {r.title}
                </Link>
                <Chip variant={r.status}>{r.status}</Chip>
              </li>
            ))}
          </ul>
        </Card>
      </section>

      <section aria-label="Plan & Usage" className="mb-6">
        <Card title="Plan & Usage" description="Current plan and usage details">
          <div className="h-2 bg-[var(--color-line)] rounded-full overflow-hidden">
            <div className="h-full w-1/3 bg-primary-600" aria-label="Usage" />
          </div>
          <p className="text-xs text-muted mt-2">Plan: Pro — 33% of monthly credits used</p>
          <Link href="/billing" className="mt-2 inline-block text-sm underline underline-offset-2">
            Manage Billing
          </Link>
        </Card>
      </section>

      <section
        aria-label="Explore"
        className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4"
      >
        <Card title="Templates" description="Jump-start with ready-made lesson templates">
          <Link href="/templates" className="text-sm underline underline-offset-2">
            Browse Templates
          </Link>
        </Card>
        <Card title="Guardrails" description="Set tone, denomination, and scripture preferences">
          <Link href="/guardrails" className="text-sm underline underline-offset-2">
            Configure Guardrails
          </Link>
        </Card>
        <Card title="Marketplace" description="Discover community-created packs and add-ons">
          <Link href="/marketplace" className="text-sm underline underline-offset-2">
            Visit Marketplace
          </Link>
        </Card>
      </section>
    </main>
  );
}
