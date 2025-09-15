import React from 'react';
import Link from 'next/link';
import { Card } from '../../src/components/ui/Card';

export default function EditorPage() {
  return (
    <main className="mx-auto max-w-screen-2xl px-3 py-4">
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-semibold text-ink">Lesson Editor</h1>
        <Link href="/shepherd/step2" className="text-sm text-accent-700 hover:underline">
          Back to Shepherd Step 2
        </Link>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <section className="lg:col-span-2 space-y-4">
          <Card title="Title" description="Working title for your lesson">
            <p className="text-sm text-muted">The Parable of the Lost Sheep</p>
          </Card>

          <Card title="Scripture" description="Primary passage">
            <blockquote className="text-sm text-ink bg-canvas/60 border-l-2 border-accent-600 pl-3 py-2">
              Luke 15:1–7 — Rejoice with me; I have found my lost sheep.
            </blockquote>
          </Card>

          <Card title="Outline" description="Auto-generated; editable">
            <ol className="list-decimal pl-5 text-sm text-body space-y-1">
              <li>Context and audience</li>
              <li>Shepherd's pursuit</li>
              <li>Joy of restoration</li>
              <li>Application for today</li>
            </ol>
          </Card>

          <Card title="Draft" description="Editable content">
            <p className="text-sm leading-6 text-body">
              God pursues the one who wanders. This draft section will be editable in a future
              sprint. For now, content is static to validate layout and theming.
            </p>
          </Card>
        </section>

        <aside className="lg:col-span-1 space-y-4">
          <Card title="Metadata">
            <dl className="text-sm text-body grid grid-cols-2 gap-y-2">
              <dt className="text-muted">Audience</dt>
              <dd>Adults</dd>
              <dt className="text-muted">Tone</dt>
              <dd>Encouraging</dd>
              <dt className="text-muted">Duration</dt>
              <dd>30 minutes</dd>
            </dl>
          </Card>
          <Card title="Actions" description="No-ops for now">
            <div className="flex flex-col gap-2">
              <button className="px-3 py-2 rounded-md bg-[var(--color-surface-raised)] border border-line text-sm hover:brightness-110">
                Save Draft
              </button>
              <button className="px-3 py-2 rounded-md bg-[var(--color-accent-600)] text-white text-sm hover:brightness-110">
                Generate Slides
              </button>
              <button className="px-3 py-2 rounded-md bg-[var(--color-surface-raised)] border border-line text-sm hover:brightness-110">
                Export PDF
              </button>
            </div>
          </Card>
        </aside>
      </div>
    </main>
  );
}
