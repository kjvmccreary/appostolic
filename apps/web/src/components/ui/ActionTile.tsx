import React from 'react';
import Link from 'next/link';
import { cn } from '../../lib/cn';

type ActionTileProps = {
  href: string;
  title: string;
  description?: string;
  cta?: string;
  className?: string;
};

export function ActionTile({ href, title, description, cta = 'Open', className }: ActionTileProps) {
  return (
    <Link
      href={href}
      className={cn(
        'rounded-lg border border-line bg-canvas p-4 shadow-smx transition hover:shadow-mdx hover:-translate-y-0.5',
        'focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:ring-primary-600',
        className,
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="text-ink font-semibold text-base leading-6">{title}</h3>
          {description ? (
            <p className="text-sm text-muted mt-0.5 leading-5">{description}</p>
          ) : null}
        </div>
        <span className="text-sm font-medium text-white bg-accent-600 px-2 py-1 rounded-md">
          {cta}
        </span>
      </div>
    </Link>
  );
}
