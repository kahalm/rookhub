import { ADMIN_TAB_KEYS, ADMIN_TAB_PERMISSIONS, AdminTabAccess, adminTabIndex, canOpenAdmin, canSeeAdminTab, visibleAdminTabs } from './admin-tabs';

/** Zugriff wie AuthService: Admin erfüllt jedes Recht. */
function access(perms: string[], isAdmin = false): AdminTabAccess {
  return { has: p => isAdmin || perms.includes(p), isAdminLive: isAdmin };
}

describe('admin-tabs', () => {
  it('resolves known tab keys to their 0-based index', () => {
    expect(adminTabIndex('users')).toBe(0);
    expect(adminTabIndex('messages')).toBe(6);
    expect(adminTabIndex('ci')).toBe(8);
    expect(adminTabIndex('uploads')).toBe(10);
    expect(adminTabIndex('jobs')).toBe(ADMIN_TAB_KEYS.length - 1);
  });

  it('returns -1 for unknown / null / empty keys (deep-link ignored)', () => {
    expect(adminTabIndex('does-not-exist')).toBe(-1);
    expect(adminTabIndex(null)).toBe(-1);
    expect(adminTabIndex('')).toBe(-1);
    expect(adminTabIndex(undefined)).toBe(-1);
  });

  // Guard: hält die Key-Liste mit der mat-tab-Reihenfolge in admin.component.html konsistent.
  // Wird ein Tab im HTML verschoben/ergänzt, MUSS dieser erwartete Stand mitgezogen werden.
  // Umbenennen bricht außerdem die Glockenlinks des Servers: `AdminLinks` (Services/Accounts) liest diese Liste im Test `AdminLinksTests`.
  it('keeps the expected canonical tab order (sync with admin.component.html)', () => {
    expect([...ADMIN_TAB_KEYS]).toEqual(
      ['users', 'books', 'daily', 'puzzles', 'groups', 'menu', 'messages', 'courseDl', 'ci', 'roles', 'uploads', 'jobs']);
  });

  it('resolves the index over a given (visible) tab list', () => {
    expect(adminTabIndex('messages', ['messages', 'uploads'])).toBe(0);
    expect(adminTabIndex('uploads', ['messages', 'uploads'])).toBe(1);
    expect(adminTabIndex('users', ['messages', 'uploads'])).toBe(-1);
  });

  // F5-005: jeder Tab hat genau einen Eintrag in der Rechte-Tabelle (null = nur Admins).
  it('maps every tab key to a permission (complete, no extras)', () => {
    expect(Object.keys(ADMIN_TAB_PERMISSIONS).sort()).toEqual([...ADMIN_TAB_KEYS].sort());
    for (const k of ADMIN_TAB_KEYS) {
      const p = ADMIN_TAB_PERMISSIONS[k];
      expect(p === null || /^[a-z]+\.[a-z]+$/.test(p)).withContext(k).toBeTrue();
    }
  });

  it('keeps the permissions the backend controllers require', () => {
    expect(ADMIN_TAB_PERMISSIONS).toEqual({
      users: 'users.manage', books: 'books.manage', daily: 'daily.manage', puzzles: 'puzzles.manage',
      groups: 'groups.manage', menu: 'menu.manage', messages: 'messages.admin', courseDl: 'chessable.admin',
      ci: 'ci.view', roles: 'roles.manage', uploads: 'messages.admin', jobs: null,
    });
  });

  it('admins see every tab', () => {
    expect(visibleAdminTabs(access([], true))).toEqual([...ADMIN_TAB_KEYS]);
    expect(canOpenAdmin(access([], true))).toBeTrue();
  });

  it('a permission holder sees only the matching tabs, in canonical order', () => {
    const a = access(['messages.admin']);
    expect(visibleAdminTabs(a)).toEqual(['messages', 'uploads']);
    expect(canSeeAdminTab(a, 'users')).toBeFalse();
    expect(canOpenAdmin(a)).toBeTrue();
  });

  it('the jobs tab needs the admin flag — no permission opens it', () => {
    const all = Object.values(ADMIN_TAB_PERMISSIONS).filter((p): p is string => !!p);
    expect(canSeeAdminTab(access(all), 'jobs')).toBeFalse();
    expect(canSeeAdminTab(access([], true), 'jobs')).toBeTrue();
  });

  it('without any admin-tab permission the area stays closed', () => {
    expect(visibleAdminTabs(access(['league.view', 'catalog.manage']))).toEqual([]);
    expect(canOpenAdmin(access([]))).toBeFalse();
  });
});
