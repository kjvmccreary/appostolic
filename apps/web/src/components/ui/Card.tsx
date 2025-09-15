import React from 'react';
import { cn } from '../../lib/cn';

type CardProps = React.HTMLAttributes<HTMLDivElement> & {
  title?: React.ReactNode;
  description?: React.ReactNode;
};

export function Card({ title, description, className, children, ...rest }: CardProps) {
  return (
    <div
      className={cn('rounded-lg shadow-mdx bg-canvas text-body border border-line p-4', className)}
      {...rest}
    >
      {(title || description) && (
        <div className="mb-3">
          {title ? <h3 className="text-ink font-semibold text-base leading-6">{title}</h3> : null}
          {description ? (
            <p className="text-sm text-muted mt-0.5 leading-5">{description}</p>
          ) : null}
        </div>
      )}
      {children}
    </div>
  );
}
