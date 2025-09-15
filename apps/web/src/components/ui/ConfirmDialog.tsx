'use client';

import * as React from 'react';

type Props = {
  open: boolean;
  title?: string;
  description?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  onConfirm: () => void;
  onClose: () => void;
};

/**
 * ConfirmDialog provides an accessible modal dialog for confirmations with focus management.
 */
export function ConfirmDialog({
  open,
  title = 'Confirm',
  description,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  onConfirm,
  onClose,
}: Props) {
  const ref = React.useRef<HTMLDivElement | null>(null);
  React.useEffect(() => {
    if (open) {
      const prev = document.activeElement as HTMLElement | null;
      ref.current?.focus();
      return () => prev?.focus();
    }
  }, [open]);

  if (!open) return null;
  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="confirm-title"
      className="fixed inset-0 z-50 flex items-center justify-center"
    >
      <div className="absolute inset-0 bg-black/40" onClick={onClose} />
      <div
        ref={ref}
        tabIndex={-1}
        className="relative z-10 w-full max-w-sm rounded-md border border-line bg-[var(--color-surface)] p-4 shadow-xl"
      >
        <h2 id="confirm-title" className="text-base font-semibold mb-1">
          {title}
        </h2>
        {description && <p className="text-sm text-muted mb-3">{description}</p>}
        <div className="flex justify-end gap-2">
          <button type="button" className="rounded border px-3 py-1 text-sm" onClick={onClose}>
            {cancelLabel}
          </button>
          <button
            type="button"
            className="rounded bg-[var(--color-accent-600)] px-3 py-1 text-sm text-white"
            onClick={() => {
              onConfirm();
              onClose();
            }}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
