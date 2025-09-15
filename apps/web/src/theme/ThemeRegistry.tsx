'use client';

import React, { PropsWithChildren, useMemo } from 'react';
import { CacheProvider } from '@emotion/react';
import createCache from '@emotion/cache';
import { CssBaseline, ThemeProvider, createTheme } from '@mui/material';
import { enUS as enUSCore } from '@mui/material/locale';
import { LicenseInfo } from '@mui/x-license-pro';
import { AdapterDateFns } from '@mui/x-date-pickers/AdapterDateFnsV3';
import { LocalizationProvider } from '@mui/x-date-pickers/LocalizationProvider';
import { useColorScheme } from './ColorSchemeContext';

// Initialize MUI X Pro license from env if provided (no-op if missing in dev)
if (process.env.NEXT_PUBLIC_MUI_LICENSE_KEY) {
  try {
    LicenseInfo.setLicenseKey(process.env.NEXT_PUBLIC_MUI_LICENSE_KEY);
  } catch {
    /* ignore license init errors in dev */
  }
}

function createEmotionCache() {
  return createCache({ key: 'mui', prepend: true });
}

import { themeOptions } from './themeOptions';

export default function ThemeRegistry({ children }: PropsWithChildren) {
  const cache = useMemo(() => createEmotionCache(), []);
  const { mode } = useColorScheme();

  const isDark = useMemo(() => {
    if (mode === 'dark') return true;
    if (mode === 'light') return false;
    if (typeof window !== 'undefined') {
      return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    }
    return false;
  }, [mode]);

  const theme = useMemo(
    () =>
      createTheme(
        {
          ...themeOptions,
          palette: {
            ...themeOptions.palette,
            mode: isDark ? 'dark' : 'light',
          },
        },
        enUSCore,
      ),
    [isDark],
  );

  return (
    <CacheProvider value={cache}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <LocalizationProvider dateAdapter={AdapterDateFns}>{children}</LocalizationProvider>
      </ThemeProvider>
    </CacheProvider>
  );
}
