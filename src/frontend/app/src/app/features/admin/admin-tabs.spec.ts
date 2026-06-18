import { ADMIN_TAB_KEYS, adminTabIndex } from './admin-tabs';

describe('admin-tabs', () => {
  it('resolves known tab keys to their 0-based index', () => {
    expect(adminTabIndex('users')).toBe(0);
    expect(adminTabIndex('messages')).toBe(6);
    expect(adminTabIndex('courseDl')).toBe(ADMIN_TAB_KEYS.length - 1);
  });

  it('returns -1 for unknown / null / empty keys (deep-link ignored)', () => {
    expect(adminTabIndex('does-not-exist')).toBe(-1);
    expect(adminTabIndex(null)).toBe(-1);
    expect(adminTabIndex('')).toBe(-1);
    expect(adminTabIndex(undefined)).toBe(-1);
  });

  // Guard: hält die Key-Liste mit der mat-tab-Reihenfolge in admin.component.html konsistent.
  // Wird ein Tab im HTML verschoben/ergänzt, MUSS dieser erwartete Stand mitgezogen werden.
  it('keeps the expected canonical tab order (sync with admin.component.html)', () => {
    expect([...ADMIN_TAB_KEYS]).toEqual(
      ['users', 'books', 'daily', 'puzzles', 'groups', 'menu', 'messages', 'courseDl']);
  });
});
