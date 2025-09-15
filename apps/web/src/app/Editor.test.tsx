import { render, screen } from '@testing-library/react';
import React from 'react';
import EditorPage from '../../app/editor/page';

describe('EditorPage', () => {
  it('renders main landmark and back to Shepherd link', () => {
    render(<EditorPage />);
    expect(screen.getByRole('main')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /back to shepherd step 2/i })).toBeInTheDocument();
  });
});
