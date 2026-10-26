import { Component, Input, OnDestroy } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AdminComponent } from './admin.component';
import { ADMIN_TAB_KEYS } from './admin-tabs';
import { AdminGroupsStore } from './admin-groups.store';
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
import { AdminLeagueUploadsComponent } from './tabs/admin-league-uploads.component';
import { AdminGroupsTabComponent } from './tabs/admin-groups-tab.component';
import { AdminUsersTabComponent } from './tabs/admin-users-tab.component';
import { AdminBooksTabComponent } from './tabs/admin-books-tab.component';

/**
 * Ohne Template/ngOnInit — testet die Tab-Logik der Seite. Die Logik der Tabs Nutzer, Bücher und Gruppen steht seit der
 * Zerlegung Z30 in `tabs/admin-{users,books,groups}-tab.component.spec.ts`.
 */
function make() {
  const router = { navigate: jasmine.createSpy('navigate').and.returnValue(Promise.resolve(true)) };
  TestBed.configureTestingModule({
    providers: [
      AdminGroupsStore,
      { provide: AdminService, useValue: {} },
      { provide: AuthService, useValue: { has: () => true, isAdminLive: true } },
      { provide: Router, useValue: router },
      { provide: ActivatedRoute, useValue: {} },
      { provide: SnackbarService, useValue: { info: () => {} } },
      { provide: TranslateService, useValue: { instant: (k: string) => k } },
    ],
  });
  const c = TestBed.runInInjectionContext(() => new AdminComponent());
  return { c, router };
}

describe('AdminComponent', () => {
  it('onTabChange sets the index and writes ?tab=<key> to the URL (merge, replaceUrl)', () => {
    const { c, router } = make();
    const messagesIdx = ADMIN_TAB_KEYS.indexOf('messages');

    c.onTabChange(messagesIdx);

    expect(c.selectedTabIndex()).toBe(messagesIdx);
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
    const adminCalls: string[] = [];
    const adminService = {
      getUsers: (_q: string, _p: number, size: number) => { adminCalls.push(`getUsers:${size}`); return of({ items: [], totalCount: 0 }); },
      getBooks: () => { adminCalls.push('getBooks'); return of([]); },
      getGroups: () => { adminCalls.push('getGroups'); return of([]); },
      getConfig: () => { adminCalls.push('getConfig'); return of({ kibanaUrl: '' }); },
    };
    TestBed.configureTestingModule({
      imports: [AdminComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }), provideRouter([]),
        { provide: AdminService, useValue: adminService },
        { provide: MenuService, useValue: { refresh: () => {} } },
        { provide: AuthService, useValue: { currentUser: { userId: 1, isAdmin: true }, has: () => true, isAdminLive: true } },
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
    return { fixture, go, adminCalls };
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

  // UI-Sweep z2 (z2-rk-admin-suche-abstand): das Nutzer-Suchfeld (jetzt im Nutzer-Tab) reserviert keinen Hilfstext-Platz.
  it('das Nutzer-Suchfeld reserviert keinen Platz für Hilfstext', async () => {
    const { fixture } = await render();
    const field = (fixture.nativeElement as HTMLElement).querySelector('mat-form-field.search-field') as HTMLElement;
    expect(field).not.toBeNull();
    expect(field.querySelector('.mat-mdc-form-field-subscript-dynamic-size')).not.toBeNull();
  });

  // Zerlegung Z30: Nutzer, Bücher und Gruppen sind eigene Komponenten, aber NICHT lazy — sie laden wie vorher beim
  // Seitenaufruf (Liste der Konten, Bücher, Mitglieder-Auswahl), Menü und Rollen nicht.
  it('Nutzer-, Bücher- und Gruppen-Tab laden beim Seitenaufruf', async () => {
    const { adminCalls } = await render();
    expect(adminCalls).toEqual(jasmine.arrayContaining(['getGroups', 'getConfig', 'getUsers:20', 'getBooks', 'getUsers:100']));
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
        { provide: AuthService, useValue: { currentUser: { userId: 1, isAdmin: true }, has: () => true, isAdminLive: true } },
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
      const tab = fixture.debugElement.query(By.directive(AdminGroupsTabComponent)).componentInstance as AdminGroupsTabComponent;
      tab.newGroupName.set('Gamma');
      tab.createGroup();
      tab.deleteGroup(tab.groups().find(g => g.name === 'Beta')!);
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

/** UI-Sweep 2026-10-11 (r2-rk-admin-users-mobile): neben der Tabelle steht je Konto eine Karte (am Handy sichtbar). */
describe('AdminComponent – Kartenliste der Konten', () => {
  it('rendert je Konto eine Karte mit Name, Mail, Gruppen und Admin-Vermerk', async () => {
    const users = [
      { id: 3, username: 'kahalm', email: 'k@example.com', isAdmin: true, groups: ['Schwaz'], createdAt: '2026-01-01T00:00:00Z' },
      { id: 4, username: 'test', email: null, isAdmin: false, groups: [], createdAt: '2026-01-01T00:00:00Z' },
    ];
    TestBed.configureTestingModule({
      imports: [AdminComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }), provideRouter([]),
        { provide: AdminService, useValue: {
          getUsers: () => of({ items: users, totalCount: 2 }), getBooks: () => of([]),
          getGroups: () => of([]), getConfig: () => of({ kibanaUrl: '' }) } },
        { provide: MenuService, useValue: { refresh: () => {} } },
        { provide: AuthService, useValue: { currentUser: { userId: 1, isAdmin: true }, has: () => true, isAdminLive: true } },
        { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap({})) } },
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

    const cards = fixture.nativeElement.querySelectorAll('.user-card') as NodeListOf<HTMLElement>;
    expect(cards.length).toBe(2);
    expect(cards[0].querySelector('.uc-name')!.textContent).toContain('kahalm');
    expect(cards[0].querySelector('.uc-mail')!.textContent).toContain('k@example.com');
    expect(cards[0].querySelector('.uc-meta')!.textContent).toContain('#3');
    expect(cards[0].querySelectorAll('.uc-group').length).toBe(1);
    expect(cards[1].querySelector('.uc-mail')).toBeNull();
    // a1-rk-admin-nutzer-schmal: Einsteigen + ⋮ stehen rechts in der Kopfzeile, nie allein in einer eigenen Zeile;
    // ohne Gruppen entfaellt die Gruppenzeile ganz.
    for (const card of Array.from(cards)) {
      expect(card.querySelectorAll('.uc-head > .uc-actions button').length).toBe(2);
      expect(card.querySelector('.uc-row button')).toBeNull();
    }
    expect(cards[0].querySelector('.uc-row')).not.toBeNull();
    expect(cards[1].querySelector('.uc-row')).toBeNull();
  });

  // Falz-Runde 16.10.: der Kopf steht 16 px unter der Leiste. Die Nutzer-Stile (Karten bis 899.98 px, Suchfeld) prüft
  // seit Zerlegung Z30 admin-users-tab.component.spec.ts.
  it('Kopf 16 px unter der Leiste (Falz-Runde 16.10.)', () => {
    const css = (((AdminComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? [])
      .join('\n').replace(/%NS%/g, '').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s+/g, ' ');
    expect(css).toMatch(/\.admin-container ?\{[^}]*margin: ?16px auto 24px/);
  });
});

/**
 * F5-005: der Admin-Bereich öffnet sich je Recht — nur die erlaubten Tabs werden gerendert, die Tab-Indizes zählen über
 * die sichtbaren Tabs, und es wird nur geladen, was der Nutzer sehen darf.
 */
@Component({ selector: 'app-admin-league-uploads', standalone: true, template: 'uploads' }) class UploadsTabStub {}

describe('AdminComponent – Tabs je Recht (F5-005)', () => {
  async function render(perms: string[], queryParams: Record<string, string> = {}) {
    const adminService = {
      getUsers: jasmine.createSpy('getUsers').and.returnValue(of({ items: [], totalCount: 0 })),
      getBooks: jasmine.createSpy('getBooks').and.returnValue(of([])),
      getGroups: jasmine.createSpy('getGroups').and.returnValue(of([])),
      getGroupOptions: jasmine.createSpy('getGroupOptions').and.returnValue(of([{ id: 3, name: 'Verein', isEveryone: false, memberCount: 4 }])),
      getConfig: jasmine.createSpy('getConfig').and.returnValue(of({ kibanaUrl: '' })),
      getMenuConfig: jasmine.createSpy('getMenuConfig').and.returnValue(of([])),
    };
    TestBed.configureTestingModule({
      imports: [AdminComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }), provideRouter([]),
        { provide: AdminService, useValue: adminService },
        { provide: MenuService, useValue: { refresh: () => {} } },
        { provide: AuthService, useValue: { currentUser: { userId: 2, isAdmin: false }, isAdminLive: false, has: (p: string) => perms.includes(p) } },
        { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap(queryParams)) } },
      ],
    });
    TestBed.overrideComponent(AdminComponent, {
      remove: { imports: [AdminMessagesComponent, AdminLeagueUploadsComponent] },
      add: { imports: [MessagesTabStub, UploadsTabStub] },
    });
    spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const fixture = TestBed.createComponent(AdminComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const labels = () => Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('[role="tab"]'))
      .map(t => t.textContent!.trim());
    return { fixture, adminService, labels };
  }

  it('messages.admin sieht nur Nachrichten und Uploads und lädt keine fremden Listen', async () => {
    const { fixture, adminService, labels } = await render(['messages.admin']);
    expect(labels().length).toBe(2);
    expect(fixture.componentInstance.visibleTabs()).toEqual(['messages', 'uploads']);
    expect(adminService.getUsers).not.toHaveBeenCalled();
    expect(adminService.getBooks).not.toHaveBeenCalled();
    expect(adminService.getGroups).not.toHaveBeenCalled();
    expect(adminService.getGroupOptions).not.toHaveBeenCalled();
    expect(adminService.getConfig).not.toHaveBeenCalled();
  });

  // F5-005-Nachlauf: GET /api/admin/groups verlangt groups.manage; Menü/Bücher/Rollen brauchen nur die Auswahl.
  it('menu.manage ohne groups.manage: Gruppenauswahl aus getGroupOptions, nicht getGroups', async () => {
    const { fixture, adminService } = await render(['menu.manage']);
    expect(adminService.getGroups).not.toHaveBeenCalled();
    expect(adminService.getGroupOptions).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance.keptTabGroups()).toEqual([{ id: 3, name: 'Verein', isEveryone: false, memberCount: 4 }]);
  });

  it('books.manage ohne groups.manage: Bücher-Freigaben wählen aus den Optionen', async () => {
    const { fixture, adminService } = await render(['books.manage']);
    expect(adminService.getGroups).not.toHaveBeenCalled();
    const books = fixture.debugElement.query(By.directive(AdminBooksTabComponent)).componentInstance as AdminBooksTabComponent;
    expect(books.groupOptions().map(g => g.name)).toEqual(['Verein']);
  });

  it('groups.manage: volle Liste, keine zweite Anfrage für die Optionen', async () => {
    const { fixture, adminService } = await render(['groups.manage', 'menu.manage']);
    expect(adminService.getGroups).toHaveBeenCalled();
    expect(adminService.getGroupOptions).not.toHaveBeenCalled();
    expect(fixture.componentInstance.keptTabGroups()).toEqual([]);
  });

  it('der Glockenlink ?tab=uploads wählt den Tab über die SICHTBAREN Tabs (Index 1, nicht 10)', async () => {
    const { fixture } = await render(['messages.admin'], { tab: 'uploads' });
    expect(fixture.componentInstance.selectedTabIndex()).toBe(1);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('uploads');
  });

  it('onTabChange schreibt den Key des sichtbaren Tabs in die URL', async () => {
    const { fixture } = await render(['messages.admin']);
    fixture.componentInstance.onTabChange(1);
    const router = TestBed.inject(Router);
    expect((router.navigate as jasmine.Spy).calls.mostRecent().args[1].queryParams).toEqual({ tab: 'uploads' });
  });

  it('users.manage ohne Admin-Flag: Nutzer-Tab ohne Admin-Umschalter, Admin-Konten ohne Aktionen', async () => {
    const { fixture, adminService } = await render(['users.manage']);
    expect(fixture.componentInstance.visibleTabs()).toEqual(['users']);
    expect(adminService.getUsers).toHaveBeenCalled();
    const c = fixture.debugElement.query(By.directive(AdminUsersTabComponent)).componentInstance as AdminUsersTabComponent;
    expect(c.actorIsAdmin).toBeFalse();
    expect(c.canManageUser({ id: 5, isAdmin: false } as any)).toBeTrue();
    expect(c.canManageUser({ id: 6, isAdmin: true } as any)).toBeFalse();
  });
});
