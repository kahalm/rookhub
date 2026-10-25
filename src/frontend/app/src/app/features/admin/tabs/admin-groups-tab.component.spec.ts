import { ChangeDetectorRef, DestroyRef } from '@angular/core';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { of, Subject, throwError } from 'rxjs';
import { AdminService, Group } from '../../../core/admin.service';
import { AuthService } from '../../../core/auth.service';
import { SnackbarService } from '../../../core/snackbar.service';
import { ConfirmService } from '../../../shared/confirm-dialog/confirm-dialog.component';
import { AdminGroupsStore } from '../admin-groups.store';
import { AdminGroupsTabComponent } from './admin-groups-tab.component';

/** Rückfrage-Doppelgänger: `answer` legt fest, was der Dialog liefert (F5-013). */
const confirmStub = { answer: true, ask: jasmine.createSpy('ask') };

/** Ohne Template/ngOnInit — testet die Komponenten-Logik (aus admin.component.spec umgezogen, Zerlegung Z30). */
function make(adminOverrides: any = {}, auth: any = {}) {
  const adminService = {
    getUsers: jasmine.createSpy('getUsers').and.returnValue(of({ items: [], total: 0 })),
    getGroupMembers: jasmine.createSpy('getGroupMembers').and.returnValue(of([])),
    addGroupMember: jasmine.createSpy('addGroupMember').and.returnValue(of(null)),
    getGroups: jasmine.createSpy('getGroups').and.returnValue(of([])),
    ...adminOverrides,
  };
  const snackbar = { info: jasmine.createSpy('info') };
  TestBed.configureTestingModule({
    providers: [
      AdminGroupsStore,
      { provide: AdminService, useValue: adminService },
      { provide: AuthService, useValue: { has: () => true, isAdminLive: true, ...auth } },
      { provide: SnackbarService, useValue: snackbar },
      { provide: TranslateService, useValue: { instant: (k: string) => k } },
      { provide: ConfirmService, useValue: confirmStub },
      { provide: DestroyRef, useValue: { onDestroy: () => () => {} } },
      { provide: ChangeDetectorRef, useValue: { markForCheck: () => {} } },
    ],
  });
  const c = TestBed.runInInjectionContext(() => new AdminGroupsTabComponent());
  const store = TestBed.inject(AdminGroupsStore);
  return { c, adminService, snackbar, store };
}

describe('AdminGroupsTabComponent', () => {
  beforeEach(() => {
    confirmStub.answer = true;
    confirmStub.ask = jasmine.createSpy('ask').and.callFake(() => of(confirmStub.answer));
  });

  it('lädt die Mitglieder-Auswahl nur mit users.manage', () => {
    const a = make();
    a.c.ngOnInit();
    expect(a.adminService.getUsers).toHaveBeenCalledWith('', 1, 500);
    TestBed.resetTestingModule();
    const b = make({}, { has: (p: string) => p === 'groups.manage' });
    b.c.ngOnInit();
    expect(b.adminService.getUsers).not.toHaveBeenCalled();
  });

  it('loadAllUsers populates allUsers and the cached availableUsers', () => {
    const users = [{ id: 1, username: 'a' }, { id: 2, username: 'b' }];
    const { c } = make({ getUsers: jasmine.createSpy('getUsers').and.returnValue(of({ items: users, total: 2 })) });

    c.loadAllUsers();

    expect(c.allUsers().length).toBe(2);
    expect(c.availableUsers().length).toBe(2);   // keine Gruppe gewählt → alle verfügbar
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

    expect(c.availableUsers().map(u => u.id)).toEqual([1, 3]);
  });

  it('addMember is a no-op without a selected group', () => {
    const { c, adminService } = make();
    c.selectedGroup.set(null);
    c.addMember({ id: 5, username: 'x' } as any);
    expect(adminService.addGroupMember).not.toHaveBeenCalled();
  });

  it('Anlegen schickt Name + Beschreibung, leert die Felder und lädt die Liste der Seite neu', () => {
    const createGroup = jasmine.createSpy('createGroup').and.returnValue(of({ id: 9 }));
    const { c, adminService, store } = make({ createGroup });
    c.newGroupName.set('  Jugend ');
    c.newGroupDescription.set('   ');
    c.createGroup();
    expect(createGroup).toHaveBeenCalledWith('Jugend', null);
    expect(c.newGroupName()).toBe('');
    expect(adminService.getGroups).toHaveBeenCalled();
    expect(store.groupsLoaded()).toBeTrue();
  });

  it('Neuladen der Liste zieht die Auswahl nach; eine verschwundene Gruppe hebt sie auf', () => {
    const { c, store } = make();
    const a = { id: 1, name: 'A', memberCount: 1 } as Group;
    store.groups.set([a]);
    c.selectedGroup.set(a);
    store.groups.set([{ ...a, name: 'A2' }]);
    expect(c.selectedGroup()!.name).toBe('A2');
    store.groups.set([]);
    expect(c.selectedGroup()).toBeNull();
  });

  it('Löschen der gewählten Gruppe räumt Auswahl und Mitglieder', () => {
    const deleteGroup = jasmine.createSpy('deleteGroup').and.returnValue(of(null));
    const { c } = make({ deleteGroup, getGroupMembers: jasmine.createSpy('m').and.returnValue(of([{ userId: 2, username: 'b' }])),
      getGroupTrainingGoal: jasmine.createSpy('goal').and.returnValue(of({ source: 'none' })) });
    const g = { id: 4, name: 'Weg', memberCount: 1 } as Group;
    c.selectGroup(g);
    expect(c.groupMembers().length).toBe(1);
    c.deleteGroup(g);
    expect(confirmStub.ask).toHaveBeenCalledWith('admin.groups.deleteConfirm', { name: 'Weg' });
    expect(c.selectedGroup()).toBeNull();
    expect(c.groupMembers()).toEqual([]);
  });

  // F5-016: das ✕ am Chip nimmt ein Mitglied erst nach Rückfrage heraus.
  it('removeMember fragt mit Name und Gruppe nach; Abbrechen nimmt niemanden heraus', () => {
    const removeGroupMember = jasmine.createSpy('removeGroupMember').and.returnValue(of(null));
    const { c } = make({ removeGroupMember });
    c.selectedGroup.set({ id: 4, name: 'Schwaz', memberCount: 2 } as any);
    confirmStub.answer = false;

    c.removeMember({ userId: 9, username: 'bob' });

    expect(confirmStub.ask).toHaveBeenCalledWith('admin.groups.removeMemberConfirm', { username: 'bob', group: 'Schwaz' });
    expect(removeGroupMember).not.toHaveBeenCalled();
  });

  it('removeMember nimmt nach Bestätigung heraus und lädt Mitglieder + Gruppen neu', () => {
    const removeGroupMember = jasmine.createSpy('removeGroupMember').and.returnValue(of(null));
    const { c, adminService } = make({ removeGroupMember });
    c.selectedGroup.set({ id: 4, name: 'Schwaz', memberCount: 2 } as any);

    c.removeMember({ userId: 9, username: 'bob' });

    expect(removeGroupMember).toHaveBeenCalledWith(4, 9);
    expect(adminService.getGroupMembers).toHaveBeenCalledWith(4);
    expect(adminService.getGroups).toHaveBeenCalled();
  });

  // Gruppe in Gruppe (2026-10-14): enthaltene/umfassende Gruppen aus der Liste, Aufnehmen lädt Mitglieder + Zahlen neu.
  it('Gruppe in Gruppe: zeigt enthaltene und umfassende Gruppen, bietet nur Aufnehmbare an und nimmt auf', () => {
    const addGroupInclusion = jasmine.createSpy('addGroupInclusion').and.returnValue(of(null));
    const { c, adminService, store } = make({ addGroupInclusion });
    store.groups.set([
      { id: 1, name: 'Everyone', isEveryone: true, memberCount: 9, includedGroupIds: [] },
      { id: 2, name: 'Jugend', memberCount: 3, includedGroupIds: [] },
      { id: 3, name: 'Verein', memberCount: 5, includedGroupIds: [2] },
      { id: 4, name: 'Tirol', memberCount: 5, includedGroupIds: [3] },
      { id: 5, name: 'Trainer', memberCount: 1, includedGroupIds: [] },
    ] as any);
    c.selectedGroup.set(c.groups()[2]);

    expect(c.includedGroups().map(g => g.name)).toEqual(['Jugend']);
    expect(c.enclosingNames()).toBe('Tirol');
    expect(c.includableGroups().map(g => g.name)).toEqual(['Tirol', 'Trainer']);   // nicht sich selbst, Everyone, Jugend
    expect(c.includedNames(c.groups()[3])).toBe('Verein');

    c.addInclusion(5);
    expect(addGroupInclusion).toHaveBeenCalledWith(3, 5);
    expect(adminService.getGroupMembers).toHaveBeenCalledWith(3);
    expect(adminService.getGroups).toHaveBeenCalled();
  });

  it('Gruppe in Gruppe: Entfernen fragt nach und nimmt erst nach Bestätigung heraus', () => {
    const removeGroupInclusion = jasmine.createSpy('removeGroupInclusion').and.returnValue(of(null));
    const { c } = make({ removeGroupInclusion });
    c.selectedGroup.set({ id: 3, name: 'Verein', memberCount: 5, includedGroupIds: [2] } as any);
    const jugend = { id: 2, name: 'Jugend', memberCount: 3 } as any;

    confirmStub.answer = false;
    c.removeInclusion(jugend);
    expect(confirmStub.ask).toHaveBeenCalledWith('admin.groups.removeIncludeConfirm', { included: 'Jugend', group: 'Verein' });
    expect(removeGroupInclusion).not.toHaveBeenCalled();

    confirmStub.answer = true;
    c.removeInclusion(jugend);
    expect(removeGroupInclusion).toHaveBeenCalledWith(3, 2);
  });

  it('Mitglieder hinzufügen (0.591.0): Liste ohne Mitglieder, neueste zuerst; ein Klick fügt genau dieses Konto hinzu', () => {
    const users = [
      { id: 1, username: 'alt', createdAt: '2026-01-01T00:00:00Z', groups: [] },
      { id: 2, username: 'mitglied', createdAt: '2026-05-01T00:00:00Z', groups: ['Schwaz'] },
      { id: 136, username: 'FM>2200', createdAt: '2026-09-28T17:01:31Z', groups: [] },
    ];
    const { c, adminService } = make({
      getUsers: jasmine.createSpy('getUsers').and.returnValue(of({ items: users, totalCount: 3 })),
      getGroupMembers: jasmine.createSpy('getGroupMembers').and.returnValue(of([{ userId: 2, username: 'mitglied' }])),
      getGroupTrainingGoal: jasmine.createSpy('goal').and.returnValue(of({ source: 'none' })),
    });
    c.loadAllUsers();
    c.selectGroup({ id: 1, name: 'Schwaz', memberCount: 1 } as any);
    expect(c.memberCandidates().map(u => u.username)).toEqual(['FM>2200', 'alt']);
    c.addMember(c.memberCandidates()[0]);
    expect(adminService.addGroupMember).toHaveBeenCalledWith(1, 136);
  });

  it('Mitglieder hinzufügen: ab zwei Zeichen fragt die Suche den Server (auch jenseits der 500 vorab geladenen)', fakeAsync(() => {
    const getUsers = jasmine.createSpy('getUsers').and.callFake((q: string) =>
      of({ items: q ? [{ id: 900, username: 'Schnabl, Andreas', createdAt: '2025-01-01', groups: [] }] : [], totalCount: q ? 1 : 0 }));
    const { c } = make({ getUsers, getGroupTrainingGoal: jasmine.createSpy('goal').and.returnValue(of({ source: 'none' })) });
    c.selectGroup({ id: 1, name: 'Schwaz', memberCount: 0 } as any);
    c.memberSearch.set('schn');
    c.onMemberSearch('schn');
    tick(300);
    expect(getUsers).toHaveBeenCalledWith('schn', 1, 50);
    expect(c.memberCandidates().map(u => u.id)).toEqual([900]);
    c.onMemberSearch('');                                   // leer → wieder die neuesten
    tick(300);
    expect(c.memberCandidates()).toEqual([]);
  }));

  // W3 F5-003: ohne geladenen Ist-Stand der GEWÄHLTEN Gruppe darf die Vorlage nicht gespeichert werden.
  it('Gruppenziel: Ladefehler bei Gruppe B → Formular nicht mit A’s Vorlage speicherbar, kein PUT', () => {
    const getGroupTrainingGoal = jasmine.createSpy('goal').and.callFake((id: number) => id === 1
      ? of({ source: 'group', dailyMinutes: 30, playGames: 5, weeklyDaysTarget: 4 })
      : throwError(() => ({ status: 500 })));
    const setGroupTrainingGoal = jasmine.createSpy('setGoal').and.returnValue(of(null));
    const deleteGroupTrainingGoal = jasmine.createSpy('delGoal').and.returnValue(of(null));
    const { c, snackbar } = make({ getGroupTrainingGoal, setGroupTrainingGoal, deleteGroupTrainingGoal });
    c.selectGroup({ id: 1, name: 'A', memberCount: 0 } as any);
    expect(c.goalLoaded()).toBeTrue();
    c.selectGroup({ id: 2, name: 'B', memberCount: 0 } as any);
    expect(snackbar.info).toHaveBeenCalledWith('admin.groups.goal.errors.load');
    expect(c.goalLoaded()).toBeFalse();
    expect(c.goalEdit.dailyMinutes).toBe(0);
    c.saveGroupGoal();
    c.clearGroupGoal();
    expect(setGroupTrainingGoal).not.toHaveBeenCalled();
    expect(deleteGroupTrainingGoal).not.toHaveBeenCalled();
  });

  it('Gruppenziel: nach erfolgreichem Laden speichert es für die gewählte Gruppe', () => {
    const setGroupTrainingGoal = jasmine.createSpy('setGoal').and.returnValue(of(null));
    const { c } = make({
      getGroupTrainingGoal: jasmine.createSpy('goal').and.returnValue(
        of({ source: 'none', dailyMinutes: 20, playGames: 2, weeklyDaysTarget: 3 })),
      setGroupTrainingGoal,
    });
    c.selectGroup({ id: 2, name: 'B', memberCount: 0 } as any);
    c.saveGroupGoal();
    expect(setGroupTrainingGoal).toHaveBeenCalledWith(2, { dailyMinutes: 20, playGames: 2, weeklyDaysTarget: 3 });
    expect(c.goalHasTemplate()).toBeTrue();
  });

  // W5 F5-004: Gruppe A (langsam), dann B — A's Mitglieder duerfen nicht unter B stehen.
  it('selectGroup: die spaete Mitgliederliste der vorher gewaehlten Gruppe landet nicht bei der neuen', () => {
    const a$ = new Subject<any[]>();
    const b$ = new Subject<any[]>();
    const getGroupMembers = jasmine.createSpy('getGroupMembers').and.callFake((id: number) => id === 1 ? a$ : b$);
    const { c } = make({ getGroupMembers, getGroupTrainingGoal: jasmine.createSpy('goal').and.returnValue(of({ source: 'none' })) });
    c.selectGroup({ id: 1, name: 'A', memberCount: 1 } as any);
    c.selectGroup({ id: 2, name: 'B', memberCount: 1 } as any);
    expect(a$.observed).toBeFalse();
    b$.next([{ userId: 22, username: 'b-mitglied' }]);
    a$.next([{ userId: 11, username: 'a-mitglied' }]);
    expect(c.groupMembers().map(m => m.userId)).toEqual([22]);
    expect(c.membersLoading()).toBeFalse();
  });

  it('Hinzufuegen in A, Antwort erst nach dem Wechsel zu B: B behaelt seine Mitgliederliste', () => {
    const add$ = new Subject<any>();
    const getGroupMembers = jasmine.createSpy('getGroupMembers').and.callFake((id: number) => of([{ userId: id * 10, username: 'm' }]));
    const { c } = make({
      getGroupMembers,
      addGroupMember: jasmine.createSpy('addGroupMember').and.returnValue(add$),
      getGroupTrainingGoal: jasmine.createSpy('goal').and.returnValue(of({ source: 'none' })),
    });
    c.selectGroup({ id: 1, name: 'A', memberCount: 1 } as any);
    c.addMember({ id: 5, username: 'neu' } as any);
    c.selectGroup({ id: 2, name: 'B', memberCount: 1 } as any);
    add$.next(null);
    expect(getGroupMembers.calls.allArgs()).toEqual([[1], [2]]);
    expect(c.groupMembers().map(m => m.userId)).toEqual([20]);
  });

  // W5 F5-004: ein Fehler im Such-Request beendete den Strom — danach reagierte die Suche bis zum Neuladen nicht.
  it('Mitgliedersuche ueberlebt einen fehlgeschlagenen Request', fakeAsync(() => {
    const getUsers = jasmine.createSpy('getUsers').and.returnValues(
      throwError(() => ({ status: 500 })),
      of({ items: [{ id: 900, username: 'Schnabl', createdAt: '2025-01-01', groups: [] }], totalCount: 1 }));
    const { c } = make({ getUsers, getGroupTrainingGoal: jasmine.createSpy('goal').and.returnValue(of({ source: 'none' })) });
    c.selectGroup({ id: 1, name: 'Schwaz', memberCount: 0 } as any);
    c.onMemberSearch('sch');
    tick(300);
    expect(c.memberCandidates()).toEqual([]);
    c.onMemberSearch('schn');
    tick(300);
    expect(getUsers).toHaveBeenCalledTimes(2);
    expect(c.memberCandidates().map(u => u.id)).toEqual([900]);
  }));
});

describe('AdminGroupsTabComponent – Darstellung', () => {
  it('creates und zeigt nach Auswahl die Mitglieder-Karte (OnPush, Antwort ohne Klick)', async () => {
    TestBed.configureTestingModule({
      imports: [AdminGroupsTabComponent],
      providers: [
        AdminGroupsStore,
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }), provideRouter([]),
        { provide: AdminService, useValue: {
          getUsers: () => of({ items: [], totalCount: 0 }),
          getGroups: () => of([{ id: 1, name: 'Alpha', memberCount: 2 }]),
          getGroupMembers: () => of([{ userId: 7, username: 'mitglied', direct: true }]),
          getGroupTrainingGoal: () => of({ source: 'none', dailyMinutes: 0, playGames: 0, weeklyDaysTarget: 0 }),
        } },
        { provide: AuthService, useValue: { currentUser: { userId: 1 }, has: () => true, isAdminLive: true } },
      ],
    });
    TestBed.inject(AdminGroupsStore).loadGroups();
    const fixture = TestBed.createComponent(AdminGroupsTabComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('tr.mat-mdc-row').length).toBe(1);
    expect(el.querySelector('.member-panel')).toBeNull();

    fixture.componentInstance.selectGroup(fixture.componentInstance.groups()[0]);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(el.querySelectorAll('.member-panel').length).toBe(2);
    expect(el.querySelector('tr.selected-group')).not.toBeNull();
    expect(el.textContent).toContain('mitglied');
  });
});
