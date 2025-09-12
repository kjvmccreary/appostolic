import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { AgentsTable, type AgentListItem } from './AgentsTable';

describe('AgentsTable', () => {
  it('renders empty state with New Agent link', () => {
    render(<AgentsTable items={[]} />);
    expect(screen.getByText('No agents yet.')).toBeInTheDocument();
    const link = screen.getByRole('link', { name: 'New Agent' });
    expect(link).toHaveAttribute('href', '/studio/agents/new');
  });

  it('renders rows with actions when items exist', () => {
    const items: AgentListItem[] = [
      {
        id: 'a1',
        name: 'Agent One',
        model: 'gpt-4o-mini',
        temperature: 0.5,
        maxSteps: 10,
        createdAt: new Date(Date.now() - 30_000).toISOString(),
        updatedAt: null,
      },
    ];
    render(<AgentsTable items={items} />);

    expect(screen.getByRole('link', { name: 'Agent One' })).toHaveAttribute(
      'href',
      '/studio/agents/a1',
    );
    expect(screen.getByRole('cell', { name: 'gpt-4o-mini' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Edit' })).toHaveAttribute('href', '/studio/agents/a1');
    expect(screen.getByRole('link', { name: 'Delete' })).toHaveAttribute(
      'href',
      '/studio/agents/a1/delete',
    );

    // Updated column should end with "ago"
    expect(screen.getByText(/ago$/)).toBeInTheDocument();
  });
});
