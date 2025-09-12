import React, { PropsWithChildren } from 'react';
import { render as rtlRender, RenderOptions } from '@testing-library/react';
import { CssBaseline, ThemeProvider, createTheme } from '@mui/material';
import { LocalizationProvider } from '@mui/x-date-pickers/LocalizationProvider';
import { AdapterDateFns } from '@mui/x-date-pickers/AdapterDateFnsV3';
import { themeOptions } from '../src/theme/themeOptions';

function Providers({ children }: PropsWithChildren) {
  const theme = createTheme(themeOptions);
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <LocalizationProvider dateAdapter={AdapterDateFns}>{children}</LocalizationProvider>
    </ThemeProvider>
  );
}

export function render(ui: React.ReactElement, options?: RenderOptions) {
  return rtlRender(ui, { wrapper: Providers, ...options });
}

export * from '@testing-library/react';
