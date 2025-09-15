import Link from 'next/link';
import { Stepper } from '../../../src/components/ui/Stepper';

function preserve(searchParams: Record<string, string | string[] | undefined>) {
  const sp = new URLSearchParams();
  Object.entries(searchParams).forEach(([k, v]) => {
    if (v == null) return;
    if (Array.isArray(v)) v.forEach((x) => sp.append(k, x));
    else sp.set(k, v);
  });
  return sp.toString();
}

export default function ShepherdStep4({
  searchParams,
}: {
  searchParams: Record<string, string | string[] | undefined>;
}) {
  const steps = [
    { id: 's1', label: 'Topic' },
    { id: 's2', label: 'Audience' },
    { id: 's3', label: 'Tone' },
    { id: 's4', label: 'Deliverables' },
    { id: 's5', label: 'Review' },
  ];
  const q = preserve(searchParams);

  return (
    <main id="main" className="p-4 sm:p-6 lg:p-8 space-y-4">
      <Stepper steps={steps} activeIndex={3} />
      <section className="space-y-2">
        <h1 className="text-ink text-lg font-semibold">Select Deliverables</h1>
        <p className="text-muted text-sm">Choose slides, notes, handouts, and more.</p>
      </section>
      <div className="flex justify-between">
        <Link
          href={`/shepherd/step3${q ? `?${q}` : ''}`}
          className="px-3 py-1 rounded-md text-sm font-medium border border-line text-ink hover:bg-[var(--color-surface)]"
        >
          Back
        </Link>
        <Link
          href={`/shepherd/step5${q ? `?${q}` : ''}`}
          className="px-3 py-1 rounded-md text-sm font-medium text-white bg-[var(--color-primary-600)] hover:brightness-110"
        >
          Next
        </Link>
      </div>
    </main>
  );
}
