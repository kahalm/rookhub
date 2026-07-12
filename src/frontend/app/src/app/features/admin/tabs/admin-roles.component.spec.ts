import { of } from 'rxjs';
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
    ...overrides,
  } as any;
  const snackbar = { info: () => {} } as any;
  const translate = { instant: (k: string) => k } as any;
  return { c: new AdminRolesComponent(admin, snackbar, translate), admin };
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

  it('saveUserRoles sends the toggled role ids', () => {
    const { c, admin } = make();
    c.selectedUser = { id: 5, username: 'x' } as any;
    c.userRoleIds = new Set([3, 4]);
    c.saveUserRoles();
    expect(admin.setUserRoles).toHaveBeenCalledWith(5, [3, 4]);
  });
});
