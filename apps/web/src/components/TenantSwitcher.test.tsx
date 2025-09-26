import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '../../test/utils';
import userEvent from '@testing-library/user-event';
import {
  makeMembership,
  makeTenantSession,
  type SessionWithClaims,
} from '../../test/fixtures/authSession';
import { authMockState } from '../../test/fixtures/mswAuthHandlers';

vi.mock('next/navigation', () => ({ useRouter: () => ({ refresh: vi.fn() }) }));

let sessionData: SessionWithClaims;
let updateMock: ReturnType<typeof vi.fn>;

vi.mock('next-auth/react', () => ({
  useSession: () => ({
    data: sessionData,
    update: updateMock,
  }),
}));

import { TenantSwitcher } from './TenantSwitcher';

describe('TenantSwitcher', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    sessionData = makeTenantSession({
      email: 'u@example.com',
      memberships: [
        makeMembership({ tenantSlug: 't1', roles: ['TenantAdmin'] }),
        makeMembership({ tenantSlug: 't2', roles: ['Learner'] }),
      ],
      tenant: 't1',
    });
    updateMock = vi.fn().mockResolvedValue(undefined);
  });

  it('updates session and posts to API when changing tenant', async () => {
    render(<TenantSwitcher />);

    const select = screen.getByRole('combobox', { name: /tenant/i }) as HTMLSelectElement;
    await userEvent.selectOptions(select, 't2');

    await waitFor(() => {
      expect(updateMock).toHaveBeenCalledWith({ tenant: 't2' });
    });
    await waitFor(() => expect(authMockState.tenantSelect.calls).toHaveLength(1));
    const request = authMockState.tenantSelect.calls[0];
    expect(request.url).toContain('/api/tenant/select');
    expect(request.body).toMatchObject({ tenant: 't2' });
    expect(request.headers['content-type']).toMatch(/application\/json/i);
  });

  it('renders canonical role labels from flags only', () => {
    render(<TenantSwitcher />);
    const opt1 = screen.getByRole('option', { name: /t1 —/i });
    const opt2 = screen.getByRole('option', { name: /t2 —/i });
    expect(opt1.textContent).toMatch(/t1 — Admin$/);
    expect(opt2.textContent).toMatch(/t2 — Learner$/);
  });
});
