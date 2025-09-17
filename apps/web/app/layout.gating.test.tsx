import { describe, it, expect } from 'vitest';
import { shouldShowTopBar } from './layout';

describe('shouldShowTopBar', () => {
  it('returns false when no cookie and no session tenant', () => {
    expect(shouldShowTopBar(undefined, undefined)).toBe(false);
  });
  it('returns false when only cookie present', () => {
    expect(shouldShowTopBar('tenant-a', undefined)).toBe(false);
  });
  it('returns false when only session tenant present', () => {
    expect(shouldShowTopBar(undefined, 'tenant-a')).toBe(false);
  });
  it('returns false when cookie and session tenant mismatch', () => {
    expect(shouldShowTopBar('tenant-a', 'tenant-b')).toBe(false);
  });
  it('returns true when cookie and session tenant match', () => {
    expect(shouldShowTopBar('tenant-a', 'tenant-a')).toBe(true);
  });
});
