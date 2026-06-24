import { DestroyRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminComponent } from './admin.component';
import { ADMIN_TAB_KEYS } from './admin-tabs';

/** Ohne Template/ngOnInit — testet die Komponenten-Logik. Instanziierung läuft im
 *  TestBed-Injection-Context, weil die Komponente `inject(DestroyRef)` als Feld nutzt. */
function make(adminOverrides: any = {}) {
  const adminService = {
    getUsers: jasmine.createSpy('getUsers').and.returnValue(of({ items: [], total: 0 })),
    getGroupMembers: jasmine.createSpy('getGroupMembers').and.returnValue(of([])),
    addGroupMember: jasmine.createSpy('addGroupMember').and.returnValue(of(null)),
    loadGroups: jasmine.createSpy('loadGroups'),
    getGroups: jasmine.createSpy('getGroups').and.returnValue(of([])),
    ...adminOverrides,
  };
  const snackbar = { info: jasmine.createSpy('info'), success: jasmine.createSpy('success'), show: jasmine.createSpy('show') };
  const translate = { instant: (k: string) => k };
  const router = { navigate: jasmine.createSpy('navigate').and.returnValue(Promise.resolve(true)) };
  const route = {};
  const c = TestBed.runInInjectionContext(() => new AdminComponent(
    adminService as any, {} as any, {} as any, {} as any,
    router as any, route as any, snackbar as any, translate as any, {} as any,
  ));
  return { c, adminService, snackbar, router };
}

describe('AdminComponent', () => {
  // Die Komponente nutzt `inject(DestroyRef)` als Feld → Instanziierung im Injection-Context
  // (DestroyRef-Stub, da kein ngOnInit/Lifecycle läuft).
  beforeEach(() => TestBed.configureTestingModule({
    providers: [{ provide: DestroyRef, useValue: { onDestroy: () => () => {} } }],
  }));

  it('onTabChange sets the index and writes ?tab=<key> to the URL (merge, replaceUrl)', () => {
    const { c, router } = make();
    const messagesIdx = ADMIN_TAB_KEYS.indexOf('messages');

    c.onTabChange(messagesIdx);

    expect(c.selectedTabIndex).toBe(messagesIdx);
    expect(router.navigate).toHaveBeenCalledTimes(1);
    const [commands, extras] = router.navigate.calls.mostRecent().args;
    expect(commands).toEqual([]);
    expect(extras.queryParams).toEqual({ tab: 'messages' });
    expect(extras.queryParamsHandling).toBe('merge');
    expect(extras.replaceUrl).toBeTrue();
  });

  it('onTabChange ignores an out-of-range index (no navigation)', () => {
    const { c, router } = make();
    c.onTabChange(999);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('loadAllUsers populates allUsers and the cached availableUsers', () => {
    const users = [{ id: 1, username: 'a' }, { id: 2, username: 'b' }];
    const { c } = make({ getUsers: jasmine.createSpy('getUsers').and.returnValue(of({ items: users, total: 2 })) });

    c.loadAllUsers();

    expect(c.allUsers.length).toBe(2);
    expect(c.availableUsers.length).toBe(2);   // keine Gruppe gewählt → alle verfügbar
  });

  it('loadAllUsers warns (does not silently truncate) when the user count exceeds the dropdown cap', () => {
    const warn = spyOn(console, 'warn');
    const { c } = make({ getUsers: jasmine.createSpy('getUsers').and.returnValue(of({ items: [{ id: 1, username: 'a' }], totalCount: 9999 })) });
    c.loadAllUsers();
    expect(warn).toHaveBeenCalled();
  });

  it('loadAllUsers does not warn when within the cap', () => {
    const warn = spyOn(console, 'warn');
    const { c } = make({ getUsers: jasmine.createSpy('getUsers').and.returnValue(of({ items: [{ id: 1, username: 'a' }], totalCount: 1 })) });
    c.loadAllUsers();
    expect(warn).not.toHaveBeenCalled();
  });

  it('loadAllUsers shows an error hint on failure', () => {
    const { c, snackbar } = make({ getUsers: jasmine.createSpy('getUsers').and.returnValue(throwError(() => ({ status: 500 }))) });
    c.loadAllUsers();
    expect(snackbar.info).toHaveBeenCalledWith('admin.users.errors.load');
  });

  it('loadMembers recomputes availableUsers to exclude current members', () => {
    const users = [{ id: 1, username: 'a' }, { id: 2, username: 'b' }, { id: 3, username: 'c' }];
    const members = [{ userId: 2, username: 'b' }];
    const { c } = make({
      getUsers: jasmine.createSpy('getUsers').and.returnValue(of({ items: users, total: 3 })),
      getGroupMembers: jasmine.createSpy('getGroupMembers').and.returnValue(of(members)),
    });
    c.loadAllUsers();        // allUsers = 1,2,3
    c.loadMembers(10);       // member 2 → available = 1,3

    expect(c.availableUsers.map(u => u.id)).toEqual([1, 3]);
  });

  it('addMember is a no-op without a selected group', () => {
    const { c, adminService } = make();
    c.selectedGroup = null;
    c.addMemberUserId = 5;
    c.addMember();
    expect(adminService.addGroupMember).not.toHaveBeenCalled();
  });
});
