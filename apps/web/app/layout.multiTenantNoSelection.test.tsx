import { describe, it, expect } from 'vitest';
import { shouldShowTopBar } from './layout';

/**
 * A narrow test confirming that without a selected tenant cookie and session.tenant,
 * nav should not render. Explicit cookie+session alignment already covered in layout.gating tests.
 */

describe('Root layout multi-tenant no-selection gating', () => {
  it('shouldShowTopBar is false when cookie missing and session tenant missing', () => {
    expect(shouldShowTopBar(undefined, undefined)).toBe(false);
  });
  it('shouldShowTopBar is false when cookie present but session tenant unset', () => {
    expect(shouldShowTopBar('alpha', undefined)).toBe(false);
  });
  it('shouldShowTopBar is false when session tenant set but cookie missing', () => {
    expect(shouldShowTopBar(undefined, 'alpha')).toBe(false);
  });
});
