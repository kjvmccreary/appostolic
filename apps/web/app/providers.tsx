'use client';

import React, { PropsWithChildren } from 'react';
import { SessionProvider } from 'next-auth/react';
import ThemeRegistry from '../src/theme/ThemeRegistry';

export default function Providers({ children }: PropsWithChildren) {
  return (
    <SessionProvider>
      <ThemeRegistry>{children}</ThemeRegistry>
    </SessionProvider>
  );
}
