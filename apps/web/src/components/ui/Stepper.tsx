import React from 'react';
import { cn } from '../../lib/cn';

export type Step = { id: string; label: string };

type StepperProps = {
  steps: Step[];
  activeIndex: number; // zero-based
  className?: string;
};

export function Stepper({ steps, activeIndex, className }: StepperProps) {
  return (
    <ol className={cn('flex items-center gap-3', className)} role="list" aria-label="Wizard steps">
      {steps.map((s, i) => {
        const active = i === activeIndex;
        return (
          <li key={s.id} className="flex items-center gap-2">
            <span
              aria-current={active ? 'step' : undefined}
              aria-label={s.label}
              className={cn(
                'inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-full border text-sm font-semibold',
                active
                  ? 'border-primary-600 text-primary-600 bg-primary-600/10'
                  : 'border-line text-muted bg-transparent',
              )}
            >
              {i + 1}
            </span>
            <span className={cn('text-sm', active ? 'text-ink' : 'text-muted')}>{s.label}</span>
          </li>
        );
      })}
    </ol>
  );
}
