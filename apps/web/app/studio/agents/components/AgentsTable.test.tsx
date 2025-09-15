vi.mock('next/navigation', () => ({ useRouter: () => ({ refresh: () => {} }) }));
let mockSession: { data: unknown } = { data: null };
// eslint-disable-next-line @typescript-eslint/no-explicit-any
vi.mock('next-auth/react', () => ({ useSession: () => mockSession }) as any);

import { render, screen } from '@testing-library/react';
import React from 'react';
import { AgentsTable, type AgentListItem } from './AgentsTable';

describe('AgentsTable gating', () => {
  beforeEach(() => {
    mockSession = { data: null };
  });
  it('empty state hides New Agent when cannot create', () => {
    render(<AgentsTable items={[]} />);
    expect(screen.queryByRole('link', { name: /new agent/i })).not.toBeInTheDocument();
  });

  it('empty state shows New Agent when canCreate', () => {
    mockSession = { data: { canCreate: true } };
    render(<AgentsTable items={[]} />);
    expect(screen.getByRole('link', { name: /new agent/i })).toBeInTheDocument();
  });

  it('row actions: run visible for all; edit hidden and delete disabled without canCreate', () => {
    const items: AgentListItem[] = [
      {
        id: 'a1',
        name: 'Agent One',
        model: 'gpt-4o',
        temperature: 0.2,
        maxSteps: 5,
        createdAt: new Date().toISOString(),
        updatedAt: null,
      },
    ];
    render(<AgentsTable items={items} />);
    expect(screen.getByRole('link', { name: /run/i })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /edit/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /delete/i })).toBeDisabled();
  });

  it('row actions: edit visible and delete enabled when canCreate', () => {
    mockSession = { data: { canCreate: true } };
    const items: AgentListItem[] = [
      {
        id: 'a1',
        name: 'Agent One',
        model: 'gpt-4o',
        temperature: 0.2,
        maxSteps: 5,
        createdAt: new Date().toISOString(),
        updatedAt: null,
      },
    ];
    render(<AgentsTable items={items} />);
    expect(screen.getByRole('link', { name: /edit/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /delete/i })).not.toBeDisabled();
  });
});
