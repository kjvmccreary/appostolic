import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '../../test/utils';
import userEvent from '@testing-library/user-event';

vi.mock('next/navigation', () => ({ useRouter: () => ({ refresh: vi.fn() }) }));

const updateMock = vi.fn().mockResolvedValue(undefined);
vi.mock('next-auth/react', () => ({
  useSession: () => ({
    data: {
      user: { email: 'u@example.com' },
      tenant: 't1',
      memberships: [
        { tenantId: 't1', tenantSlug: 't1', role: 'Viewer', roles: ['TenantAdmin'] },
        { tenantId: 't2', tenantSlug: 't2', role: 'Viewer', roles: [] },
      ],
    },
    update: updateMock,
  }),
}));

import { TenantSwitcher } from './TenantSwitcher';

describe('TenantSwitcher', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('updates session and posts to API when changing tenant', async () => {
    const originalFetch = global.fetch;
    const fetchMock = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }));
    global.fetch = fetchMock as unknown as typeof fetch;

    render(<TenantSwitcher />);

    const select = screen.getByRole('combobox', { name: /tenant/i }) as HTMLSelectElement;
    await userEvent.selectOptions(select, 't2');

    await waitFor(() => {
      expect(updateMock).toHaveBeenCalledWith({ tenant: 't2' });
      expect(fetchMock).toHaveBeenCalledWith(
        '/api/tenant/select',
        expect.objectContaining({
          method: 'POST',
        }),
      );
    });

    global.fetch = originalFetch;
  });

  it('renders canonical role labels from flags only', () => {
    render(<TenantSwitcher />);
    const opt1 = screen.getByRole('option', { name: /t1 —/i });
    const opt2 = screen.getByRole('option', { name: /t2 —/i });
    expect(opt1.textContent).toMatch(/t1 — Admin$/);
    expect(opt2.textContent).toMatch(/t2 — Learner$/);
  });
});
