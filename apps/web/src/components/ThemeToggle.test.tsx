import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { ColorSchemeProvider } from '../theme/ColorSchemeContext';
import { ThemeToggle } from './ThemeToggle';

describe('ThemeToggle', () => {
  it('toggles theme and amoled attributes on the html element', async () => {
    const user = userEvent.setup();
    render(
      <ColorSchemeProvider>
        <ThemeToggle />
      </ColorSchemeProvider>,
    );

    const themeBtn = screen.getByRole('button', { name: /toggle theme/i });
    const amoledBtn = screen.getByRole('button', { name: /toggle amoled/i });

    // cycle: system -> light
    await user.click(themeBtn);
    expect(document.documentElement.classList.contains('dark')).toBe(false);

    // light -> dark
    await user.click(themeBtn);
    expect(document.documentElement.classList.contains('dark')).toBe(true);

    // enable amoled when dark
    await user.click(amoledBtn);
    expect(document.documentElement.getAttribute('data-theme')).toBe('amoled');
  });
});
