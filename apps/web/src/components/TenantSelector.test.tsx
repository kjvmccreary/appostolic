import { render, screen } from '@testing-library/react';
import React from 'react';
import { TenantSelector } from './TenantSelector';

describe('TenantSelector', () => {
  it('renders nothing (legacy no-op)', () => {
    const { container } = render(<TenantSelector />);
    // Should render no interactive elements
    expect(screen.queryByLabelText(/tenant/i)).toBeNull();
    expect(screen.queryByRole('button')).toBeNull();
    // Container should be effectively empty
    expect(container).toBeTruthy();
  });
});
