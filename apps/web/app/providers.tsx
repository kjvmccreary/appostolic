'use client';

import React, { PropsWithChildren } from 'react';
import { SessionProvider } from 'next-auth/react';
import ThemeRegistry from '../src/theme/ThemeRegistry';
import { ColorSchemeProvider } from '../src/theme/ColorSchemeContext';
import { ToastProvider } from '../src/components/ui/Toaster';

export default function Providers({ children }: PropsWithChildren) {
  return (
    <SessionProvider>
      <ColorSchemeProvider>
        <ThemeRegistry>
          <ToastProvider>{children}</ToastProvider>
        </ThemeRegistry>
      </ColorSchemeProvider>
    </SessionProvider>
  );
}
