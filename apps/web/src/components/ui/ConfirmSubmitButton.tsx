'use client';

import * as React from 'react';

type Props = {
  formId: string;
  label: string;
  confirmText?: string;
  className?: string;
};

/**
 * ConfirmSubmitButton renders a button that, when clicked, asks for confirmation
 * via window.confirm, and if accepted, programmatically submits the given form.
 * This keeps server actions as-is while adding a safety confirmation on the client.
 */
export default function ConfirmSubmitButton({ formId, label, confirmText, className }: Props) {
  const onClick = React.useCallback(
    (e: React.MouseEvent<HTMLButtonElement>) => {
      e.preventDefault();
      const ok = window.confirm(confirmText ?? 'Are you sure?');
      if (!ok) return;
      const form = document.getElementById(formId) as HTMLFormElement | null;
      if (form) form.requestSubmit();
    },
    [formId, confirmText],
  );

  return (
    <button type="button" onClick={onClick} className={className} aria-label={label}>
      {label}
    </button>
  );
}
