'use client';

import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';

export type ThemeMode = 'light' | 'dark' | 'system';

type ColorSchemeContextValue = {
  mode: ThemeMode;
  setMode: (mode: ThemeMode) => void;
  toggleMode: () => void; // cycles light -> dark -> system
  amoled: boolean;
  setAmoled: (v: boolean) => void;
  toggleAmoled: () => void;
};

const ColorSchemeContext = createContext<ColorSchemeContextValue | undefined>(undefined);

function applyHtmlAttributes(mode: ThemeMode, amoled: boolean) {
  if (typeof document === 'undefined') return;
  const root = document.documentElement; // <html>
  const prefersDark =
    window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
  const isDark = mode === 'dark' || (mode === 'system' && prefersDark);
  root.classList.toggle('dark', isDark);
  if (amoled && isDark) {
    root.setAttribute('data-theme', 'amoled');
  } else {
    root.removeAttribute('data-theme');
  }
}

export function ColorSchemeProvider({ children }: { children: React.ReactNode }) {
  const [mode, setMode] = useState<ThemeMode>('system');
  const [amoled, setAmoled] = useState<boolean>(false);

  // Initialize from localStorage + system preference
  useEffect(() => {
    try {
      const savedMode = (localStorage.getItem('theme') as ThemeMode | null) ?? 'system';
      const savedAmoled = localStorage.getItem('amoled') === 'true';
      setMode(savedMode);
      setAmoled(savedAmoled);
      applyHtmlAttributes(savedMode, savedAmoled);
    } catch {
      // no-op
    }
  }, []);

  // React to system changes when in system mode
  useEffect(() => {
    if (mode !== 'system' || typeof window === 'undefined') return;
    const mql = window.matchMedia('(prefers-color-scheme: dark)');
    const handler = () => applyHtmlAttributes(mode, amoled);
    mql.addEventListener?.('change', handler);
    return () => mql.removeEventListener?.('change', handler);
  }, [mode, amoled]);

  const setModeAndPersist = useCallback(
    (m: ThemeMode) => {
      setMode(m);
      try {
        localStorage.setItem('theme', m);
      } catch {
        // ignore unavailable localStorage
      }
      applyHtmlAttributes(m, amoled);
    },
    [amoled],
  );

  const toggleMode = useCallback(() => {
    setModeAndPersist(mode === 'light' ? 'dark' : mode === 'dark' ? 'system' : 'light');
  }, [mode, setModeAndPersist]);

  const setAmoledAndPersist = useCallback(
    (v: boolean) => {
      setAmoled(v);
      try {
        localStorage.setItem('amoled', v ? 'true' : 'false');
      } catch {
        // ignore unavailable localStorage
      }
      applyHtmlAttributes(mode, v);
    },
    [mode],
  );

  const toggleAmoled = useCallback(
    () => setAmoledAndPersist(!amoled),
    [amoled, setAmoledAndPersist],
  );

  const value = useMemo<ColorSchemeContextValue>(
    () => ({
      mode,
      setMode: setModeAndPersist,
      toggleMode,
      amoled,
      setAmoled: setAmoledAndPersist,
      toggleAmoled,
    }),
    [mode, setModeAndPersist, toggleMode, amoled, setAmoledAndPersist, toggleAmoled],
  );

  return <ColorSchemeContext.Provider value={value}>{children}</ColorSchemeContext.Provider>;
}

export function useColorScheme() {
  const ctx = useContext(ColorSchemeContext);
  if (!ctx) throw new Error('useColorScheme must be used within ColorSchemeProvider');
  return ctx;
}
