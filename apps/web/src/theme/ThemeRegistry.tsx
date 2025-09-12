'use client';

import React, { PropsWithChildren, useMemo } from 'react';
import { CacheProvider } from '@emotion/react';
import createCache from '@emotion/cache';
import { CssBaseline, ThemeProvider, createTheme } from '@mui/material';
import { enUS as enUSCore } from '@mui/material/locale';
import { LicenseInfo } from '@mui/x-license-pro';
import { AdapterDateFns } from '@mui/x-date-pickers/AdapterDateFnsV3';
import { LocalizationProvider } from '@mui/x-date-pickers/LocalizationProvider';

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

const theme = createTheme(themeOptions, enUSCore);

export default function ThemeRegistry({ children }: PropsWithChildren) {
  const cache = useMemo(() => createEmotionCache(), []);
  return (
    <CacheProvider value={cache}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <LocalizationProvider dateAdapter={AdapterDateFns}>{children}</LocalizationProvider>
      </ThemeProvider>
    </CacheProvider>
  );
}
