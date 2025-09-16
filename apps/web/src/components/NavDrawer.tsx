'use client';

import React from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { cn } from '../lib/cn';

export type NavItem = { label: string; href: string };

/**
 * NavDrawer — mobile navigation drawer with accessibility in mind.
 * - Focus trap while open; restores focus on close
 * - Closes on ESC and backdrop click
 * - Auto-closes on route change
 */
export function NavDrawer({
  open,
  onClose,
  isAdmin = false,
  navItems,
  adminItems = [],
}: {
  open: boolean;
  onClose: () => void;
  isAdmin?: boolean;
  navItems: NavItem[];
  adminItems?: NavItem[];
}) {
  const pathname = usePathname() || '';
  const panelRef = React.useRef<HTMLDivElement | null>(null);
  const lastActive = React.useRef<HTMLElement | null>(null);

  // Close on route change
  React.useEffect(() => {
    // Auto-close the drawer when the current pathname changes
    onClose();
    // We intentionally depend only on pathname so toggling open state externally doesn't retrigger
  }, [pathname]);

  // Focus trap + restore
  React.useEffect(() => {
    if (open) {
      lastActive.current = (document.activeElement as HTMLElement) ?? null;
      // move focus to panel
      const panel = panelRef.current;
      panel?.focus();
      const handleKeyDown = (e: KeyboardEvent) => {
        if (e.key === 'Escape') {
          e.preventDefault();
          onClose();
        }
        if (e.key === 'Tab') {
          // naive focus trap
          const f = panel?.querySelectorAll<HTMLElement>(
            'a, button, [tabindex]:not([tabindex="-1"])',
          );
          if (!f || f.length === 0) return;
          const first = f[0];
          const last = f[f.length - 1];
          const active = document.activeElement as HTMLElement | null;
          if (e.shiftKey) {
            if (active === first) {
              e.preventDefault();
              last.focus();
            }
          } else if (active === last) {
            e.preventDefault();
            first.focus();
          }
        }
      };
      document.addEventListener('keydown', handleKeyDown);
      return () => document.removeEventListener('keydown', handleKeyDown);
    } else {
      // restore focus
      lastActive.current?.focus?.();
    }
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div
      aria-label="Navigation drawer"
      role="dialog"
      aria-modal="true"
      className="fixed inset-0 z-50"
    >
      <div
        data-testid="backdrop"
        className="absolute inset-0 bg-black/40 backdrop-blur-[1px]"
        onClick={onClose}
      />
      <div
        ref={panelRef}
        tabIndex={-1}
        className={cn(
          'absolute left-0 top-0 h-full w-80 max-w-[85%] bg-[var(--color-surface)] border-r border-[var(--color-line)] shadow-lg outline-none',
          'animate-in slide-in-from-left',
        )}
      >
        <div className="p-3 border-b border-[var(--color-line)]">
          <span className="font-semibold">Menu</span>
        </div>
        <nav className="p-2" aria-label="Primary">
          {navItems.map((item) => {
            const active = pathname === item.href || pathname.startsWith(item.href + '/');
            return (
              <Link
                key={item.href}
                href={item.href}
                className={cn(
                  'block rounded px-3 py-2 text-sm focus-ring',
                  active
                    ? 'bg-[var(--color-surface-raised)]'
                    : 'hover:bg-[var(--color-surface-raised)]',
                )}
                aria-current={active ? 'page' : undefined}
                onClick={() => onClose()}
              >
                {item.label}
              </Link>
            );
          })}
        </nav>
        {isAdmin && adminItems.length > 0 ? (
          <nav className="p-2 border-t border-[var(--color-line)]" aria-label="Admin">
            <div className="px-3 pb-1 text-xs text-muted">Admin</div>
            {adminItems.map((item) => {
              const active = pathname === item.href || pathname.startsWith(item.href + '/');
              return (
                <Link
                  key={item.href}
                  href={item.href}
                  className={cn(
                    'block rounded px-3 py-2 text-sm focus-ring',
                    active
                      ? 'bg-[var(--color-surface-raised)]'
                      : 'hover:bg-[var(--color-surface-raised)]',
                  )}
                  aria-current={active ? 'page' : undefined}
                  onClick={() => onClose()}
                >
                  {item.label}
                </Link>
              );
            })}
          </nav>
        ) : null}
      </div>
    </div>
  );
}
