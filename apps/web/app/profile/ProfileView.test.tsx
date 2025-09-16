import React from 'react';
import { render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import { ProfileView } from './ProfileView';

describe('ProfileView', () => {
  it('renders email', () => {
    render(<ProfileView email="user@example.com" />);
    expect(screen.getByTestId('profile-email')).toHaveTextContent('user@example.com');
  });

  it('renders No avatar placeholder when no avatarUrl', () => {
    render(<ProfileView email="user@example.com" />);
    expect(screen.getByTestId('no-avatar')).toBeInTheDocument();
  });

  it('renders img when avatarUrl provided', () => {
    render(<ProfileView email="user@example.com" avatarUrl="https://cdn.example.com/a.png" />);
    const img = screen.getByRole('img', { hidden: true });
    expect(img).toHaveAttribute('src', 'https://cdn.example.com/a.png');
  });
});
