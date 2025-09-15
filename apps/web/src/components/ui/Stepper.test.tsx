import { render, screen } from '@testing-library/react';
import React from 'react';
import { Stepper } from './Stepper';

describe('Stepper', () => {
  it('renders 5 steps and marks active with aria-current', () => {
    const steps = [
      { id: 's1', label: 'Topic' },
      { id: 's2', label: 'Audience' },
      { id: 's3', label: 'Tone' },
      { id: 's4', label: 'Deliverables' },
      { id: 's5', label: 'Review' },
    ];
    render(<Stepper steps={steps} activeIndex={2} />);
    expect(screen.getAllByRole('listitem')).toHaveLength(5);
    const active = screen.getByLabelText('Tone');
    expect(active).toHaveAttribute('aria-current', 'step');
  });
});
