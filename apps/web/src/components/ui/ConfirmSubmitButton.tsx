'use client';

import * as React from 'react';
import { ConfirmDialog } from './ConfirmDialog';

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
  const [open, setOpen] = React.useState(false);
  const submit = React.useCallback(() => {
    const form = document.getElementById(formId) as HTMLFormElement | null;
    if (form) form.requestSubmit();
  }, [formId]);

  return (
    <>
      <button
        type="button"
        onClick={(e) => {
          e.preventDefault();
          setOpen(true);
        }}
        className={className}
        aria-label={label}
      >
        {label}
      </button>
      <ConfirmDialog
        open={open}
        onClose={() => setOpen(false)}
        onConfirm={submit}
        title={label}
        description={confirmText ?? 'Are you sure?'}
        confirmLabel={label}
        cancelLabel="Cancel"
      />
    </>
  );
}
