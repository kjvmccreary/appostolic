import { render, screen } from '@testing-library/react';
import React from 'react';
import { Chip } from './Chip';

describe('Chip', () => {
  it('renders variant styles', () => {
    render(
      <div>
        <Chip variant="draft">Draft</Chip>
        <Chip variant="slides">Slides</Chip>
        <Chip variant="handout">Handout</Chip>
      </div>,
    );
    expect(screen.getByText('Draft')).toBeInTheDocument();
    expect(screen.getByText('Slides')).toBeInTheDocument();
    expect(screen.getByText('Handout')).toBeInTheDocument();
  });
});
