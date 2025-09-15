'use client';

import * as React from 'react';

type Props = {
  id?: string;
  name: string;
  placeholder?: string;
  className?: string;
};

/**
 * EmailField adds inline validation messages for required email input.
 * It relies on native validity with a small touched state to avoid noisy messages.
 */
export default function EmailField({ id = 'invite-email', name, placeholder }: Props) {
  const [touched, setTouched] = React.useState(false);
  const [invalid, setInvalid] = React.useState(false);
  const onBlur = (e: React.FocusEvent<HTMLInputElement>) => {
    setTouched(true);
    setInvalid(!e.currentTarget.validity.valid);
  };
  const onInput = (e: React.FormEvent<HTMLInputElement>) => {
    if (!touched) return;
    setInvalid(!e.currentTarget.validity.valid);
  };

  return (
    <div className="flex-1">
      <input
        id={id}
        type="email"
        name={name}
        placeholder={placeholder}
        required
        onBlur={onBlur}
        onInput={onInput}
        aria-invalid={invalid ? 'true' : 'false'}
        aria-describedby={invalid ? `${id}-error` : undefined}
        className={
          'h-8 w-full rounded-md border border-line bg-[var(--color-surface-raised)] px-2 text-sm ' +
          (invalid ? 'outline outline-2 outline-red-400' : '')
        }
      />
      <p
        id={`${id}-error`}
        role="alert"
        aria-live="polite"
        className={'mt-1 text-xs ' + (invalid ? 'text-red-600' : 'text-transparent select-none')}
      >
        Please enter a valid email address.
      </p>
    </div>
  );
}
