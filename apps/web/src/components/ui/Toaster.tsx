'use client';

import * as React from 'react';
import { createPortal } from 'react-dom';

type Toast = {
  id: number;
  kind?: 'success' | 'error' | 'info';
  message: string;
  duration?: number; // ms
};

type ToastContextValue = {
  showToast: (t: Omit<Toast, 'id'>) => void;
};

const ToastContext = React.createContext<ToastContextValue | undefined>(undefined);

export function useToast() {
  const ctx = React.useContext(ToastContext);
  if (!ctx) throw new Error('useToast must be used within <ToastProvider>');
  return ctx;
}

// Optional variant for scenarios like isolated tests where the provider
// may not be mounted. Returns undefined instead of throwing.
export function useToastOptional() {
  return React.useContext(ToastContext);
}

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = React.useState<Toast[]>([]);
  const counter = React.useRef(1);
  // Avoid referencing document during SSR by rendering the portal only after mount
  const [mounted, setMounted] = React.useState(false);
  React.useEffect(() => {
    setMounted(true);
  }, []);

  const showToast = React.useCallback((t: Omit<Toast, 'id'>) => {
    const id = counter.current++;
    const duration = t.duration ?? 3500;
    setToasts((prev) => [...prev, { id, ...t, duration }]);
    window.setTimeout(() => {
      setToasts((prev) => prev.filter((x) => x.id !== id));
    }, duration);
  }, []);

  return (
    <ToastContext.Provider value={{ showToast }}>
      {children}
      {mounted
        ? createPortal(
            <div
              aria-live="polite"
              role="status"
              className="fixed bottom-4 right-4 z-50 flex flex-col gap-2"
            >
              {toasts.map((t) => (
                <div
                  key={t.id}
                  className={
                    'rounded-md border px-3 py-2 text-sm shadow-md min-w-[240px] ' +
                    (t.kind === 'error'
                      ? 'bg-red-100 text-red-900 border-red-300'
                      : t.kind === 'success'
                        ? 'bg-emerald-100 text-emerald-900 border-emerald-300'
                        : 'bg-slate-100 text-slate-900 border-slate-300')
                  }
                >
                  {t.message}
                </div>
              ))}
            </div>,
            document.body,
          )
        : null}
    </ToastContext.Provider>
  );
}
