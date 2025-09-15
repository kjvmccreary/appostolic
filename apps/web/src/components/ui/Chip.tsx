import React from 'react';
import { cn } from '../../lib/cn';

type ChipVariant = 'default' | 'draft' | 'slides' | 'handout';

type ChipProps = React.HTMLAttributes<HTMLSpanElement> & {
  variant?: ChipVariant;
};

const variantClasses: Record<ChipVariant, string> = {
  default: 'bg-[var(--color-line)] text-ink',
  draft: 'bg-amber-600/15 text-amber-600',
  slides: 'bg-primary-600/15 text-primary-600',
  handout: 'bg-accent-600/15 text-accent-600',
};

export function Chip({ variant = 'default', className, children, ...rest }: ChipProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        variantClasses[variant],
        className,
      )}
      {...rest}
    >
      {children}
    </span>
  );
}
