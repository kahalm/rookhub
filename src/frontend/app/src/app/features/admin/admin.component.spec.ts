import { Component, DestroyRef, Input, OnDestroy } from '@angular/core';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, Subject, throwError } from 'rxjs';
import { AdminComponent } from './admin.component';
import { ADMIN_TAB_KEYS } from './admin-tabs';
import { AdminService, Group, MenuItemConfig } from '../../core/admin.service';
import { MenuService } from '../../core/menu.service';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { AdminMenuVisibilityComponent } from './tabs/admin-menu-visibility.component';
import { AdminRolesComponent } from './tabs/admin-roles.component';
import { AdminMessagesComponent } from './tabs/admin-messages.component';
import { AdminDailyPuzzleComponent } from './tabs/admin-daily-puzzle.component';
import { AdminPuzzleTagsComponent } from './tabs/admin-puzzle-tags.component';
import { AdminChessableDownloadComponent } from './tabs/admin-chessable-download.component';
import { AdminGithubActionsComponent } from './admin-github-actions.component';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { PromptService } from '../../shared/prompt-dialog/prompt-dialog.component';

/** Rückfrage-Doppelgänger: `answer` legt fest, was der Dialog liefert (F5-013). */
const confirmStub = { answer: true, ask: jasmine.createSpy('ask') };
/** Eingabe-Doppelgänger (F5-016): `answer` = Werte je Feld-Key, `null` = abgebrochen. */
const promptStub: { answer: Record<string, string> | null; ask: jasmine.Spy } = { answer: null, ask: jasmine.createSpy('ask') };

/** Ohne Template/ngOnInit — testet die Komponenten-Logik. Instanziierung läuft im
 *  TestBed-Injection-Context, weil die Komponente `inject(DestroyRef)` als Feld nutzt. */
function make(adminOverrides: any = {}, auth: any = {}) {
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
  const menu = { refresh: jasmine.createSpy('refresh') };
  const c = TestBed.runInInjectionContext(() => new AdminComponent(
    adminService as any, menu as any, auth,
    router as any, route as any, snackbar as any, translate as any,
  ));
  return { c, adminService, snackbar, router, menu };
}

describe('AdminComponent', () => {
  // Die Komponente nutzt `inject(DestroyRef)` als Feld → Instanziierung im Injection-Context
  // (DestroyRef-Stub, da kein ngOnInit/Lifecycle läuft).
  beforeEach(() => {
    confirmStub.answer = true;
    confirmStub.ask = jasmine.createSpy('ask').and.callFake(() => of(confirmStub.answer));
    promptStub.answer = null;
    promptStub.ask = jasmine.createSpy('ask').and.callFake(() => of(promptStub.answer));
    TestBed.configureTestingModule({
      providers: [
        { provide: DestroyRef, useValue: { onDestroy: () => () => {} } },
        { provide: ConfirmService, useValue: confirmStub },
        { provide: PromptService, useValue: promptStub },
      ],
    });
  });

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
    c.addMember({ id: 5, username: 'x' } as any);
    expect(adminService.addGroupMember).not.toHaveBeenCalled();
  });

  // F1-013: AuthService.impersonate liefert false, wenn die Admin-Sicherung nicht geschrieben werden kann
  // (Speicher gesperrt/voll) — dann kein „Eingestiegen als …", kein Menü-Auffrischen, kein Sprung ins Dashboard.
  it('impersonate meldet den Fehlschlag und bleibt, wenn die Admin-Sicherung nicht geschrieben werden kann', () => {
    const auth = { impersonate: jasmine.createSpy('impersonate').and.returnValue(false) };
    const token = { token: 't', userId: 7, username: 'spieler', isAdmin: false };
    const { c, snackbar, router, menu } = make({ impersonate: jasmine.createSpy('impersonate').and.returnValue(of(token)) }, auth);

    c.impersonate({ id: 7, username: 'spieler' } as any);

    expect(auth.impersonate).toHaveBeenCalledWith(token as any);
    expect(snackbar.info).toHaveBeenCalledWith('admin.users.impersonateFailed');
    expect(snackbar.info).not.toHaveBeenCalledWith('admin.users.impersonateStarted');
    expect(menu.refresh).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
    expect(c.impersonatingId).toBeNull();
  });

  it('impersonate steigt ein, frischt das Menü auf und geht ins Dashboard', () => {
    const auth = { impersonate: jasmine.createSpy('impersonate').and.returnValue(true) };
    const token = { token: 't', userId: 7, username: 'spieler', isAdmin: false };
    const { c, snackbar, router, menu } = make({ impersonate: jasmine.createSpy('impersonate').and.returnValue(of(token)) }, auth);

    c.impersonate({ id: 7, username: 'spieler' } as any);

    expect(menu.refresh).toHaveBeenCalled();
    expect(snackbar.info).toHaveBeenCalledWith('admin.users.impersonateStarted');
    expect(snackbar.info).not.toHaveBeenCalledWith('admin.users.impersonateFailed');
    expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);
  });

  // F5-016: das ✕ am Chip nimmt ein Mitglied erst nach Rückfrage heraus.
  it('removeMember fragt mit Name und Gruppe nach; Abbrechen nimmt niemanden heraus', () => {
    const removeGroupMember = jasmine.createSpy('removeGroupMember').and.returnValue(of(null));
    const { c } = make({ removeGroupMember });
    c.selectedGroup = { id: 4, name: 'Schwaz', memberCount: 2 } as any;
    confirmStub.answer = false;

    c.removeMember({ userId: 9, username: 'bob' });

    expect(confirmStub.ask).toHaveBeenCalledWith('admin.groups.removeMemberConfirm', { username: 'bob', group: 'Schwaz' });
    expect(removeGroupMember).not.toHaveBeenCalled();
  });

  it('removeMember nimmt nach Bestätigung heraus und lädt Mitglieder + Gruppen neu', () => {
    const removeGroupMember = jasmine.createSpy('removeGroupMember').and.returnValue(of(null));
    const { c, adminService } = make({ removeGroupMember });
    c.selectedGroup = { id: 4, name: 'Schwaz', memberCount: 2 } as any;

    c.removeMember({ userId: 9, username: 'bob' });

    expect(removeGroupMember).toHaveBeenCalledWith(4, 9);
    expect(adminService.getGroupMembers).toHaveBeenCalledWith(4);
    expect(adminService.getGroups).toHaveBeenCalled();
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
    expect(c.memberCandidates.map(u => u.username)).toEqual(['FM>2200', 'alt']);
    c.addMember(c.memberCandidates[0]);
    expect(adminService.addGroupMember).toHaveBeenCalledWith(1, 136);
  });

  it('Mitglieder hinzufügen: ab zwei Zeichen fragt die Suche den Server (auch jenseits der 500 vorab geladenen)', fakeAsync(() => {
    const getUsers = jasmine.createSpy('getUsers').and.callFake((q: string) =>
      of({ items: q ? [{ id: 900, username: 'Schnabl, Andreas', createdAt: '2025-01-01', groups: [] }] : [], totalCount: q ? 1 : 0 }));
    const { c } = make({ getUsers, getGroupTrainingGoal: jasmine.createSpy('goal').and.returnValue(of({ source: 'none' })) });
    c.selectGroup({ id: 1, name: 'Schwaz', memberCount: 0 } as any);
    c.memberSearch = 'schn';
    c.onMemberSearch('schn');
    tick(300);
    expect(getUsers).toHaveBeenCalledWith('schn', 1, 50);
    expect(c.memberCandidates.map(u => u.id)).toEqual([900]);
    c.onMemberSearch('');                                   // leer → wieder die neuesten
    tick(300);
    expect(c.memberCandidates).toEqual([]);
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
    expect(c.goalLoaded).toBeTrue();
    c.selectGroup({ id: 2, name: 'B', memberCount: 0 } as any);
    expect(snackbar.info).toHaveBeenCalledWith('admin.groups.goal.errors.load');
    expect(c.goalLoaded).toBeFalse();
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
  });

  it('applyBookFilter filters by name, file name and tags (case-insensitive)', () => {
    const { c } = make();
    c.books = [
      { id: 1, displayName: 'Endgame Essentials', fileName: 'endgame.pgn', tags: 'endgame' },
      { id: 2, displayName: 'Tactics Trainer', fileName: 'tactics.pgn', tags: 'fork,pin' },
    ] as any;

    c.bookSearch = '';
    c.applyBookFilter();
    expect(c.filteredBooks.length).toBe(2);

    c.bookSearch = 'endGAME';        // matches name + tag of book 1
    c.applyBookFilter();
    expect(c.filteredBooks.map((b: any) => b.id)).toEqual([1]);

    c.bookSearch = 'pin';            // matches tag of book 2
    c.applyBookFilter();
    expect(c.filteredBooks.map((b: any) => b.id)).toEqual([2]);

    c.bookSearch = 'nope';
    c.applyBookFilter();
    expect(c.filteredBooks.length).toBe(0);
  });

  it('applyBookFilter applies per-column filters (kind, tri-state, group, ranges) combined with AND', () => {
    const { c } = make();
    c.books = [
      { id: 1, displayName: 'Endgame', fileName: 'e.pgn', tags: null, kind: 'Puzzle', difficulty: 'Easy', minElo: 1000, maxElo: 1500, puzzleCount: 50, forDaily: true, forRandom: false, forBlind: false, isPublic: true, forKids: false, accessGroupIds: [4] },
      { id: 2, displayName: 'Tactics', fileName: 't.pgn', tags: null, kind: 'Study', difficulty: 'Hard', minElo: 2000, maxElo: 2400, puzzleCount: 500, forDaily: false, forRandom: true, forBlind: false, isPublic: false, forKids: true, accessGroupIds: [] },
    ] as any;

    c.bookFilters.kind = 'Puzzle';
    c.applyBookFilter();
    expect(c.filteredBooks.map((b: any) => b.id)).toEqual([1]);

    c.resetBookFilters();
    expect(c.filteredBooks.length).toBe(2);
    expect(c.hasActiveBookFilters()).toBeFalse();

    c.bookFilters.public = 'no';               // tri-state
    c.applyBookFilter();
    expect(c.filteredBooks.map((b: any) => b.id)).toEqual([2]);

    c.resetBookFilters();
    c.bookFilters.group = 'none';              // admin-only (no groups)
    c.applyBookFilter();
    expect(c.filteredBooks.map((b: any) => b.id)).toEqual([2]);

    c.resetBookFilters();
    c.bookFilters.group = 4;                    // specific group
    c.applyBookFilter();
    expect(c.filteredBooks.map((b: any) => b.id)).toEqual([1]);

    c.resetBookFilters();
    c.bookFilters.puzzlesMin = 100;             // range
    c.applyBookFilter();
    expect(c.filteredBooks.map((b: any) => b.id)).toEqual([2]);

    c.resetBookFilters();
    c.bookFilters.eloMax = 1600;                // elo range: only book 1 fits within
    c.applyBookFilter();
    expect(c.filteredBooks.map((b: any) => b.id)).toEqual([1]);

    c.resetBookFilters();
    expect(c.hasActiveBookFilters()).toBeFalse();
    c.bookFilters.difficulty = 'hard';
    expect(c.hasActiveBookFilters()).toBeTrue();
    c.applyBookFilter();
    expect(c.filteredBooks.map((b: any) => b.id)).toEqual([2]);
  });

  it('renameBook sends the new DisplayName and updates the row + filter', () => {
    const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
    const { c } = make({ updateBook });
    const book = { id: 7, displayName: 'Old Name', fileName: 'x.pgn', tags: null, minElo: 800, maxElo: 1200 } as any;
    c.books = [book];
    promptStub.answer = { name: '  New Name  ' };

    c.renameBook(book);

    expect(promptStub.ask.calls.mostRecent().args[0].fields).toEqual([{ key: 'name', label: 'admin.books.renamePrompt', value: 'Old Name' }]);
    // Nur der Name: der Endpunkt lässt fehlende Felder unverändert, auch die Elo-Spanne (N9-009).
    expect(updateBook).toHaveBeenCalledWith(7, { displayName: 'New Name' });
    expect(book.displayName).toBe('New Name');
  });

  it('renameBook does nothing on cancel or unchanged name', () => {
    const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
    const { c } = make({ updateBook });
    const book = { id: 7, displayName: 'Same', fileName: 'x.pgn', tags: null } as any;

    promptStub.answer = null;                                           // cancelled
    c.renameBook(book);
    promptStub.answer = { name: 'Same' };                               // unchanged
    c.renameBook(book);

    expect(updateBook).not.toHaveBeenCalled();
  });

  it('editKidsTitles fragt alle KidHub-Sprachen in EINEM Dialog ab und speichert alle, ohne die Elo zu verlieren', () => {
    const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({ kidsTitles: { de: 'Matt in einem Zug', en: 'Checkmate in One' } }));
    const { c } = make({ updateBook });
    const book = { id: 9, displayName: 'Learn Chess', forKids: true, minElo: null, maxElo: 1000, kidsTitles: { de: 'Alt' } } as any;
    promptStub.answer = { de: ' Matt in einem Zug ', en: 'Checkmate in One', hr: '', hu: '' };

    c.editKidsTitles(book);

    // F5-016: ein Dialog statt vier Abfragen nacheinander; vorhandener Titel vorbelegt.
    expect(promptStub.ask).toHaveBeenCalledTimes(1);
    const fields = promptStub.ask.calls.mostRecent().args[0].fields;
    expect(fields.map((f: any) => f.key)).toEqual(['de', 'en', 'hr', 'hu']);
    expect(fields[0].value).toBe('Alt');
    expect(updateBook).toHaveBeenCalledWith(9, {
      kidsTitles: { de: 'Matt in einem Zug', en: 'Checkmate in One', hr: '', hu: '' },
    });
    expect(book.kidsTitles).toEqual({ de: 'Matt in einem Zug', en: 'Checkmate in One' });
  });

  it('saveBook schickt ein geleertes Elo-Feld als 0 (Grenze entfernen), null hiesse unverändert', () => {
    const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
    const { c } = make({ updateBook });

    c.saveBook({ id: 3, displayName: 'x', kind: 'Puzzle', minElo: null, maxElo: 1800 } as any);

    expect(updateBook.calls.mostRecent().args[1]).toEqual(jasmine.objectContaining({ minElo: 0, maxElo: 1800 }));
  });

  it('editKidsTitles: Abbrechen speichert nichts', () => {
    const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
    const { c } = make({ updateBook });
    promptStub.answer = null;

    c.editKidsTitles({ id: 9, displayName: 'x', forKids: true } as any);

    expect(updateBook).not.toHaveBeenCalled();
  });

  // F5-013: Admin-Recht nur nach Rückfrage, als Soll-Wert, Knopf während der Anfrage gesperrt.
  describe('Admin-Recht', () => {
    it('fragt mit dem Namen nach und setzt dann den Soll-Wert', () => {
      const setAdmin = jasmine.createSpy('setAdmin').and.returnValue(of({ id: 4, isAdmin: true }));
      const { c } = make({ setAdmin });
      const user = { id: 4, username: 'bob', isAdmin: false } as any;

      c.toggleAdmin(user);

      expect(confirmStub.ask).toHaveBeenCalledWith('admin.users.confirmPromote', { username: 'bob' });
      expect(setAdmin).toHaveBeenCalledWith(4, true);
      expect(user.isAdmin).toBeTrue();
      expect(c.adminBusyId).toBeNull();
    });

    it('Abbrechen ändert nichts', () => {
      confirmStub.answer = false;
      const setAdmin = jasmine.createSpy('setAdmin');
      const { c } = make({ setAdmin });

      c.toggleAdmin({ id: 4, username: 'bob', isAdmin: true } as any);

      expect(confirmStub.ask).toHaveBeenCalledWith('admin.users.confirmDemote', { username: 'bob' });
      expect(setAdmin).not.toHaveBeenCalled();
    });

    it('ein zweiter Klick während der Anfrage schickt keine zweite', () => {
      const pending = new Subject<any>();
      const setAdmin = jasmine.createSpy('setAdmin').and.returnValue(pending);
      const { c } = make({ setAdmin });
      const user = { id: 4, username: 'bob', isAdmin: false } as any;

      c.toggleAdmin(user);
      expect(c.adminBusyId).toBe(4);
      c.toggleAdmin(user);
      expect(setAdmin).toHaveBeenCalledTimes(1);

      pending.next({ id: 4, isAdmin: true });
      expect(c.adminBusyId).toBeNull();
    });
  });

  // F5-013: „Öffentlich" und „Kinder" erst nach Rückfrage einschalten; Abbrechen stellt den Schalter zurück.
  describe('Öffentlich/Kinder', () => {
    it('Einschalten fragt nach; Abbrechen stellt zurück und speichert nicht', () => {
      confirmStub.answer = false;
      const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
      const { c } = make({ updateBook });
      const book = { id: 9, displayName: 'Privatkurs', isPublic: true, forKids: false } as any;   // ngModel hat schon umgestellt

      c.toggleExposure(book, 'isPublic');

      expect(confirmStub.ask).toHaveBeenCalledWith('admin.books.confirmPublic', { name: 'Privatkurs' });
      expect(book.isPublic).toBeFalse();
      expect(updateBook).not.toHaveBeenCalled();
    });

    it('Einschalten mit OK speichert; Ausschalten speichert ohne Rückfrage', () => {
      const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
      const { c } = make({ updateBook });

      c.toggleExposure({ id: 9, displayName: 'K', forKids: true } as any, 'forKids');
      expect(confirmStub.ask).toHaveBeenCalledWith('admin.books.confirmKids', { name: 'K' });
      expect(updateBook).toHaveBeenCalledTimes(1);

      confirmStub.ask.calls.reset();
      c.toggleExposure({ id: 9, displayName: 'K', forKids: false } as any, 'forKids');
      expect(confirmStub.ask).not.toHaveBeenCalled();
      expect(updateBook).toHaveBeenCalledTimes(2);
    });
  });

  // N9-010: private Bücher anderer Konten — Besitzer erkennbar, auch die Pools fragen nach und nennen ihn.
  describe('fremde private Bücher', () => {
    const me = { currentUser: { userId: 1 } };

    it('erkennt fremde private Bücher, nicht globale und nicht die eigenen', () => {
      const { c } = make({}, me);
      expect(c.isForeignBook({ ownerUserId: 42 } as any)).toBeTrue();
      expect(c.isForeignBook({ ownerUserId: 1 } as any)).toBeFalse();
      expect(c.isForeignBook({ ownerUserId: null } as any)).toBeFalse();
    });

    it('Tagespuzzle auf einer fremden Kopie fragt mit Besitzer nach; Abbrechen stellt zurück', () => {
      confirmStub.answer = false;
      const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
      const { c } = make({ updateBook }, me);
      const book = { id: 7, displayName: 'Lifetime Repertoires', ownerUserId: 42, ownerName: 'bob', forDaily: true } as any;

      c.toggleExposure(book, 'forDaily');

      expect(confirmStub.ask).toHaveBeenCalledWith('admin.books.confirmForeign', { name: 'Lifetime Repertoires', owner: 'bob' });
      expect(book.forDaily).toBeFalse();
      expect(updateBook).not.toHaveBeenCalled();
    });

    it('Pools auf globalen und eigenen Büchern speichern ohne Rückfrage', () => {
      const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
      const { c } = make({ updateBook }, me);

      c.toggleExposure({ id: 7, displayName: 'G', ownerUserId: null, forRandom: true } as any, 'forRandom');
      c.toggleExposure({ id: 8, displayName: 'Mein', ownerUserId: 1, forBlind: true } as any, 'forBlind');

      expect(confirmStub.ask).not.toHaveBeenCalled();
      expect(updateBook).toHaveBeenCalledTimes(2);
    });

    it('die Suche findet Bücher über den Besitzernamen', () => {
      const { c } = make({}, me);
      c.books = [{ id: 1, displayName: 'Kurs', fileName: 'a.pgn', ownerName: 'alice' }, { id: 2, displayName: 'Kurs', fileName: 'b.pgn', ownerName: 'bob' }] as any;
      c.bookSearch = 'bob';
      c.applyBookFilter();
      expect(c.filteredBooks.map(b => b.id)).toEqual([2]);
    });
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
    expect(c.groupMembers.map(m => m.userId)).toEqual([22]);
    expect(c.membersLoading).toBeFalse();
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
    expect(c.groupMembers.map(m => m.userId)).toEqual([20]);
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
    expect(c.memberCandidates).toEqual([]);
    c.onMemberSearch('schn');
    tick(300);
    expect(getUsers).toHaveBeenCalledTimes(2);
    expect(c.memberCandidates.map(u => u.id)).toEqual([900]);
  }));
});

/**
 * F5-020: Menü- und Rollen-Tab speichern erst per Knopf. Als lazy `matTabContent` zerstörte ein Tabwechsel die
 * Komponente samt ungespeicherter Änderungen; jetzt wird sie beim ersten Öffnen erzeugt und danach behalten.
 * Gerendert mit dem echten Template, die Tab-Komponenten als Zähl-Attrappen.
 */
@Component({ selector: 'app-admin-menu-visibility', standalone: true, template: 'menu' })
class MenuTabStub implements OnDestroy {
  static created = 0;
  static destroyed = 0;
  @Input() groups: Group[] | null = null;
  draft = '';
  constructor() { MenuTabStub.created++; }
  ngOnDestroy(): void { MenuTabStub.destroyed++; }
}
@Component({ selector: 'app-admin-roles', standalone: true, template: 'roles' })
class RolesTabStub implements OnDestroy {
  static created = 0;
  static destroyed = 0;
  @Input() groups: Group[] | null = null;
  constructor() { RolesTabStub.created++; }
  ngOnDestroy(): void { RolesTabStub.destroyed++; }
}
@Component({ selector: 'app-admin-messages', standalone: true, template: '' }) class MessagesTabStub {}
@Component({ selector: 'app-admin-daily-puzzle', standalone: true, template: '' }) class DailyTabStub {}
@Component({ selector: 'app-admin-puzzle-tags', standalone: true, template: '' }) class TagsTabStub {}
@Component({ selector: 'app-admin-chessable-download', standalone: true, template: '' }) class DownloadTabStub {}
@Component({ selector: 'app-admin-github-actions', standalone: true, template: '' }) class CiTabStub {}

describe('AdminComponent – Tabs mit eigenem Formular (F5-020)', () => {
  async function render(queryParams: Record<string, string> = {}) {
    MenuTabStub.created = MenuTabStub.destroyed = 0;
    RolesTabStub.created = RolesTabStub.destroyed = 0;
    const adminService = {
      getUsers: () => of({ items: [], totalCount: 0 }),
      getBooks: () => of([]),
      getGroups: () => of([]),
      getConfig: () => of({ kibanaUrl: '' }),
    };
    TestBed.configureTestingModule({
      imports: [AdminComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }), provideRouter([]),
        { provide: AdminService, useValue: adminService },
        { provide: MenuService, useValue: { refresh: () => {} } },
        { provide: AuthService, useValue: { currentUser: { userId: 1, isAdmin: true } } },
        { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap(queryParams)) } },
      ],
    });
    TestBed.overrideComponent(AdminComponent, {
      remove: { imports: [AdminMenuVisibilityComponent, AdminRolesComponent, AdminMessagesComponent, AdminDailyPuzzleComponent,
        AdminPuzzleTagsComponent, AdminChessableDownloadComponent, AdminGithubActionsComponent] },
      add: { imports: [MenuTabStub, RolesTabStub, MessagesTabStub, DailyTabStub, TagsTabStub, DownloadTabStub, CiTabStub] },
    });
    spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const fixture = TestBed.createComponent(AdminComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const go = async (key: string) => {
      fixture.componentInstance.onTabChange(ADMIN_TAB_KEYS.indexOf(key as any));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, go };
  }

  it('lädt Menü und Rollen nicht beim Seitenaufruf, behält sie aber nach dem ersten Öffnen über Tabwechsel hinweg', async () => {
    const { fixture, go } = await render();
    expect(MenuTabStub.created).toBe(0);
    expect(RolesTabStub.created).toBe(0);

    await go('menu');
    expect(MenuTabStub.created).toBe(1);
    const menu = fixture.debugElement.query(By.directive(MenuTabStub)).componentInstance as MenuTabStub;
    menu.draft = 'fünf Einträge auf Gruppen';

    await go('messages');
    await go('roles');
    await go('users');
    await go('menu');

    expect(MenuTabStub.destroyed).toBe(0);
    expect(MenuTabStub.created).toBe(1);
    expect(fixture.debugElement.query(By.directive(MenuTabStub)).componentInstance).toBe(menu);
    expect(menu.draft).toBe('fünf Einträge auf Gruppen');
    expect(RolesTabStub.created).toBe(1);
    expect(RolesTabStub.destroyed).toBe(0);
  });

  it('ein Deep-Link auf den Rollen-Tab öffnet ihn gleich', async () => {
    await render({ tab: 'roles' });
    expect(RolesTabStub.created).toBe(1);
    expect(MenuTabStub.created).toBe(0);
  });
});

/**
 * F5-020 (Nacharbeit): Die behaltenen Tabs bekommen ihre Gruppenliste von der Admin-Seite. Vorher lud jeder Tab sie
 * einmal selbst — nach Anlegen/Löschen im Gruppen-Tab blieb sie veraltet: die neue Gruppe war nicht wählbar, die
 * gelöschte ging im Menü-Payload mit (Fremdschlüssel → 500) bzw. blieb im Rollen-Tab wählbar (→ 404).
 * Mit den ECHTEN Menü- und Rollen-Komponenten; Gruppen-Bestand wie auf dem Server (anlegen/löschen ändern ihn).
 */
describe('AdminComponent – Gruppenliste der behaltenen Tabs (F5-020)', () => {
  async function render() {
    let groups = [{ id: 1, name: 'Alpha', memberCount: 2 }, { id: 2, name: 'Beta', memberCount: 3 }] as Group[];
    let nextId = 3;
    const adminService = {
      getUsers: () => of({ items: [], totalCount: 0 }),
      getBooks: () => of([]),
      getConfig: () => of({ kibanaUrl: '' }),
      getGroups: () => of(groups.map(g => ({ ...g }))),
      createGroup: (name: string) => {
        const g = { id: nextId++, name, memberCount: 0 } as Group;
        groups = [...groups, g];
        return of(g);
      },
      deleteGroup: (id: number) => { groups = groups.filter(g => g.id !== id); return of(void 0); },
      getMenuConfig: () => of([{ key: 'courses', level: 'Groups', groupIds: [1, 2] }] as MenuItemConfig[]),
      saveMenuConfig: jasmine.createSpy('saveMenuConfig').and.callFake((items: MenuItemConfig[]) => of(items)),
      getRoles: () => of([]),
      getPermissions: () => of([]),
      getGroupRoles: (id: number) => of({ groupId: id, roleIds: [] }),
    };
    TestBed.configureTestingModule({
      imports: [AdminComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }), provideRouter([]),
        { provide: AdminService, useValue: adminService },
        { provide: MenuService, useValue: { refresh: () => {} } },
        { provide: SnackbarService, useValue: { info: () => {} } },
        { provide: ConfirmService, useValue: { ask: () => of(true) } },
        { provide: AuthService, useValue: { currentUser: { userId: 1, isAdmin: true } } },
        { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap({})) } },
      ],
    });
    TestBed.overrideComponent(AdminComponent, {
      remove: { imports: [AdminMessagesComponent, AdminDailyPuzzleComponent, AdminPuzzleTagsComponent,
        AdminChessableDownloadComponent, AdminGithubActionsComponent] },
      add: { imports: [MessagesTabStub, DailyTabStub, TagsTabStub, DownloadTabStub, CiTabStub] },
    });
    spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const fixture = TestBed.createComponent(AdminComponent);
    const settle = async () => { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); };
    await settle();
    const c = fixture.componentInstance;
    const go = async (key: string) => { c.onTabChange(ADMIN_TAB_KEYS.indexOf(key as any)); await settle(); };
    /** Im Gruppen-Tab „Gamma" anlegen und „Beta" löschen, dann zurück in `back`. */
    const changeGroupsAndReturn = async (back: string) => {
      await go('groups');
      c.newGroupName = 'Gamma';
      c.createGroup();
      c.deleteGroup(c.groups.find(g => g.name === 'Beta')!);
      await go(back);
    };
    return { fixture, c, go, changeGroupsAndReturn, adminService };
  }

  it('Menü: neu angelegte Gruppe ist wählbar, die gelöschte geht beim Speichern nicht mehr mit', async () => {
    const { fixture, go, changeGroupsAndReturn, adminService } = await render();
    await go('menu');
    const menu = fixture.debugElement.query(By.directive(AdminMenuVisibilityComponent)).componentInstance as AdminMenuVisibilityComponent;
    expect(menu.groups!.map(g => g.name)).toEqual(['Alpha', 'Beta']);

    await changeGroupsAndReturn('menu');

    expect(fixture.debugElement.query(By.directive(AdminMenuVisibilityComponent)).componentInstance).toBe(menu);
    expect(menu.groups!.map(g => g.name)).toEqual(['Alpha', 'Gamma']);
    menu.saveMenuConfig();
    expect(adminService.saveMenuConfig).toHaveBeenCalledWith([{ key: 'courses', level: 'Groups', groupIds: [1] }]);
  });

  it('Rollen: neue Gruppe erscheint, die gelöschte verschwindet samt Auswahl', async () => {
    const { fixture, go, changeGroupsAndReturn } = await render();
    await go('roles');
    const roles = fixture.debugElement.query(By.directive(AdminRolesComponent)).componentInstance as AdminRolesComponent;
    roles.selectGroup(roles.assignableGroups.find(g => g.name === 'Beta')!);
    expect(roles.groupRolesLoaded).toBeTrue();

    await changeGroupsAndReturn('roles');

    expect(fixture.debugElement.query(By.directive(AdminRolesComponent)).componentInstance).toBe(roles);
    const chips = Array.from(fixture.nativeElement.querySelectorAll('.group-roles .user-chip') as NodeListOf<HTMLElement>)
      .map(e => e.textContent!.replace(/\s+/g, ' ').trim());
    expect(chips).toEqual(['Alpha (2)', 'Gamma (0)']);
    expect(roles.selectedGroup).toBeNull();
    expect(roles.groupRolesLoaded).toBeFalse();
  });
});
