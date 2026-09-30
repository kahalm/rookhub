import { of, throwError, Subject } from 'rxjs';
import { AdminRolesComponent } from './admin-roles.component';
import { Role } from '../../../core/admin.service';

function make(overrides: any = {}) {
  const admin = {
    getRoles: jasmine.createSpy('getRoles').and.returnValue(of([])),
    getPermissions: jasmine.createSpy('getPermissions').and.returnValue(of(['users.manage', 'books.manage'])),
    getUsers: jasmine.createSpy('getUsers').and.returnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 20 })),
    createRole: jasmine.createSpy('createRole').and.returnValue(of({} as Role)),
    updateRole: jasmine.createSpy('updateRole').and.returnValue(of({} as Role)),
    deleteRole: jasmine.createSpy('deleteRole').and.returnValue(of(void 0)),
    getUserRoles: jasmine.createSpy('getUserRoles').and.returnValue(of({ userId: 1, roleIds: [2] })),
    setUserRoles: jasmine.createSpy('setUserRoles').and.returnValue(of(void 0)),
    getGroups: jasmine.createSpy('getGroups').and.returnValue(of([
      { id: 4, name: 'Everyone', isEveryone: true, memberCount: 99 }, { id: 1, name: 'Schwaz', memberCount: 10 }])),
    getGroupRoles: jasmine.createSpy('getGroupRoles').and.returnValue(of({ groupId: 1, roleIds: [3] })),
    setGroupRoles: jasmine.createSpy('setGroupRoles').and.returnValue(of(void 0)),
    ...overrides,
  } as any;
  const snackbar = { info: jasmine.createSpy('info') } as any;
  const translate = { instant: (k: string) => k } as any;
  return { c: new AdminRolesComponent(admin, snackbar, translate), admin, snackbar };
}

const role = (over: Partial<Role>): Role =>
  ({ id: 1, key: 'trainer', name: 'Trainer', isSystem: false, permissions: [], memberCount: 0, ...over });

describe('AdminRolesComponent', () => {
  it('permKey maps dotted permission keys to underscore i18n keys', () => {
    expect(make().c.permKey('users.manage')).toBe('admin.roles.perm.users_manage');
  });

  it('canCreate requires a valid lowercase key and a name', () => {
    const { c } = make();
    c.newKey = 'trainer'; c.newName = 'Trainer';
    expect(c.canCreate).toBeTrue();
    c.newKey = '9bad'; // must start with a letter
    expect(c.canCreate).toBeFalse();
    c.newKey = 'trainer'; c.newName = '  ';
    expect(c.canCreate).toBeFalse();
  });

  it('isAdminRole / assignableRoles excludes the admin role', () => {
    const { c } = make();
    c.roles = [role({ id: 1, key: 'admin', isSystem: true }), role({ id: 2, key: 'trainer' })];
    expect(c.isAdminRole(c.roles[0])).toBeTrue();
    expect(c.assignableRoles.map(r => r.id)).toEqual([2]);
  });

  it('createRole posts the lowercased key + selected permissions and reloads', () => {
    const { c, admin } = make();
    c.newKey = 'Trainer'; c.newName = 'Trainer';
    c.toggleNewPerm('books.manage');
    c.createRole();
    expect(admin.createRole).toHaveBeenCalledWith({ key: 'trainer', name: 'Trainer', permissions: ['books.manage'] });
    expect(admin.getRoles).toHaveBeenCalled();
  });

  it('selectUser loads the user role ids', () => {
    const { c, admin } = make();
    c.selectUser({ id: 1, username: 'x' } as any);
    expect(admin.getUserRoles).toHaveBeenCalledWith(1);
    expect([...c.userRoleIds]).toEqual([2]);
  });

  it('Gruppenrollen (0.589.0): ohne „Everyone", laden, umschalten, speichern, danach Rollen neu laden', () => {
    const { c, admin } = make();
    c.ngOnInit();
    expect(c.assignableGroups.map(g => g.name)).toEqual(['Schwaz']);
    c.selectGroup(c.assignableGroups[0]);
    expect(admin.getGroupRoles).toHaveBeenCalledWith(1);
    expect([...c.groupRoleIds]).toEqual([3]);
    c.toggleGroupRole(5);
    c.toggleGroupRole(3);
    admin.getRoles.calls.reset();
    c.saveGroupRoles();
    expect(admin.setGroupRoles).toHaveBeenCalledWith(1, [5]);
    expect(admin.getRoles).toHaveBeenCalled();
  });

  it('saveUserRoles sends the toggled role ids', () => {
    const { c, admin } = make({ getUserRoles: jasmine.createSpy('getUserRoles').and.returnValue(of({ userId: 5, roleIds: [3] })) });
    c.selectUser({ id: 5, username: 'x' } as any);
    c.toggleUserRole(4);
    c.saveUserRoles();
    expect(admin.setUserRoles).toHaveBeenCalledWith(5, [3, 4]);
  });

  // W3 F5-003: der Server ERSETZT den Rollensatz — ohne geladenen Ist-Stand darf nichts gespeichert werden.
  it('Nutzerrollen: Ladefehler → Hinweis, Speichern gesperrt, kein PUT (sonst entzöge es alle übrigen Rollen)', () => {
    const { c, admin, snackbar } = make({
      getUserRoles: jasmine.createSpy('getUserRoles').and.returnValue(throwError(() => ({ status: 502 }))),
    });
    c.selectUser({ id: 7, username: 'x' } as any);
    expect(c.userRolesLoaded).toBeFalse();
    expect(snackbar.info).toHaveBeenCalledWith('admin.roles.loadError');
    c.toggleUserRole(2);
    c.saveUserRoles();
    expect(admin.setUserRoles).not.toHaveBeenCalled();
  });

  it('Nutzerrollen: späte Antwort des vorher gewählten Nutzers landet nicht beim neuen', () => {
    const a$ = new Subject<any>();
    const b$ = new Subject<any>();
    const { c, admin } = make({
      getUserRoles: jasmine.createSpy('getUserRoles').and.callFake((id: number) => id === 1 ? a$ : b$),
    });
    c.selectUser({ id: 1, username: 'a' } as any);
    c.selectUser({ id: 2, username: 'b' } as any);
    a$.next({ userId: 1, roleIds: [9] });
    expect(c.userRolesLoaded).toBeFalse();
    c.saveUserRoles();
    expect(admin.setUserRoles).not.toHaveBeenCalled();
    b$.next({ userId: 2, roleIds: [3] });
    expect([...c.userRoleIds]).toEqual([3]);
    c.saveUserRoles();
    expect(admin.setUserRoles).toHaveBeenCalledWith(2, [3]);
  });

  it('Gruppenrollen: Ladefehler → Speichern gesperrt, kein PUT', () => {
    const { c, admin, snackbar } = make({
      getGroupRoles: jasmine.createSpy('getGroupRoles').and.returnValue(throwError(() => ({ status: 500 }))),
    });
    c.selectGroup({ id: 1, name: 'Schwaz', memberCount: 10 } as any);
    expect(snackbar.info).toHaveBeenCalledWith('admin.roles.loadError');
    expect(c.groupRolesLoaded).toBeFalse();
    c.toggleGroupRole(3);
    c.saveGroupRoles();
    expect(admin.setGroupRoles).not.toHaveBeenCalled();
  });
});
