'use client';

import React from 'react';
import { Moon, Sun, Monitor, Contrast } from 'lucide-react';
import { useColorScheme } from '../theme/ColorSchemeContext';

export function ThemeToggle() {
  const { mode, toggleMode, amoled, toggleAmoled } = useColorScheme();

  const icon =
    mode === 'light' ? (
      <Sun size={18} />
    ) : mode === 'dark' ? (
      <Moon size={18} />
    ) : (
      <Monitor size={18} />
    );

  return (
    <div className="flex items-center gap-2">
      <button
        aria-label="Toggle theme"
        onClick={toggleMode}
        className="inline-flex items-center gap-1 text-sm opacity-80 hover:opacity-100"
      >
        {icon}
        <span className="text-xs">{mode}</span>
      </button>
      <button
        aria-label="Toggle AMOLED"
        onClick={toggleAmoled}
        className="inline-flex items-center gap-1 text-sm opacity-80 hover:opacity-100"
      >
        <Contrast size={18} />
        <span className="text-xs">{amoled ? 'AMOLED on' : 'AMOLED off'}</span>
      </button>
    </div>
  );
}
