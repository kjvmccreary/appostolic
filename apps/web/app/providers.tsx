'use client';

import React, { PropsWithChildren } from 'react';
import { SessionProvider } from 'next-auth/react';
import ThemeRegistry from '../src/theme/ThemeRegistry';
import { ColorSchemeProvider } from '../src/theme/ColorSchemeContext';

export default function Providers({ children }: PropsWithChildren) {
  return (
    <SessionProvider>
      <ColorSchemeProvider>
        <ThemeRegistry>{children}</ThemeRegistry>
      </ColorSchemeProvider>
    </SessionProvider>
  );
}
