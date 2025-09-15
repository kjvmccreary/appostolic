import { render, screen } from '@testing-library/react';
import React from 'react';
import DashboardPage from '../../app/page';

describe('DashboardPage', () => {
  it('renders main landmarks and quick start tiles', () => {
    render(<DashboardPage />);
    expect(screen.getByRole('main')).toBeInTheDocument();
    // Quick Start section should have at least the Shepherd CTA
    expect(screen.getByRole('link', { name: /create lesson/i })).toBeInTheDocument();
  });
});
