'use client';

import * as React from 'react';

export default function AutoSubmitCheckbox({
  name,
  label,
  defaultChecked,
  disabled,
  describedById,
  formId,
}: {
  name: string;
  label: string;
  defaultChecked?: boolean;
  disabled?: boolean;
  describedById?: string;
  /** Optional form id to associate and submit when the input isn't a descendant of the form */
  formId?: string;
}) {
  const ref = React.useRef<HTMLInputElement | null>(null);
  const [pending, setPending] = React.useState(false);

  // On change, submit the closest form
  const onChange = React.useCallback(() => {
    const input = ref.current;
    if (!input) return;
    // If a formId is provided, submit that form; otherwise fallback to closest form
    const form = formId
      ? (document.getElementById(formId) as HTMLFormElement | null)
      : (input.closest('form') as HTMLFormElement | null);
    if (!form) return;
    setPending(true);
    // next.js server actions submit via form.requestSubmit
    form.requestSubmit();
  }, []);

  // Reset pending when navigation completes (best-effort: timer as simple heuristic)
  React.useEffect(() => {
    if (!pending) return;
    const t = setTimeout(() => setPending(false), 1500);
    return () => clearTimeout(t);
  }, [pending]);

  return (
    <label className="inline-flex items-center gap-2">
      <input
        ref={ref}
        type="checkbox"
        name={name}
        defaultChecked={defaultChecked}
        disabled={disabled || pending}
        aria-label={label}
        aria-describedby={describedById}
        onChange={onChange}
        className="h-4 w-4 rounded border-[var(--color-line)] accent-[var(--color-accent-600)]"
        data-pending={pending ? 'true' : undefined}
        // When provided, associate the input with a specific form element
        form={formId}
      />
      <span className="select-none">{label}</span>
      {pending ? (
        <span
          aria-hidden
          className="ml-1 h-3 w-3 animate-pulse rounded-full bg-[var(--color-accent-600)]/60"
        />
      ) : null}
    </label>
  );
}
