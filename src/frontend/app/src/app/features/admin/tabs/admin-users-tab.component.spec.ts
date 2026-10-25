import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { of, Subject, throwError } from 'rxjs';
import { AdminService } from '../../../core/admin.service';
import { AuthService } from '../../../core/auth.service';
import { MenuService } from '../../../core/menu.service';
import { SnackbarService } from '../../../core/snackbar.service';
import { ConfirmService } from '../../../shared/confirm-dialog/confirm-dialog.component';
import { ResetLinkDialogComponent } from '../reset-link-dialog.component';
import { AdminUsersTabComponent } from './admin-users-tab.component';

/** Rückfrage-Doppelgänger: `answer` legt fest, was der Dialog liefert (F5-013). */
const confirmStub = { answer: true, ask: jasmine.createSpy('ask') };
/** MatDialog-Doppelgänger für den Reset-Link-Dialog (UX-002). */
const dialogStub = { open: jasmine.createSpy('open') };

/** Ohne Template/ngOnInit — testet die Komponenten-Logik (aus admin.component.spec umgezogen, Zerlegung Z30). */
function make(adminOverrides: any = {}, auth: any = {}) {
  const adminService = {
    getUsers: jasmine.createSpy('getUsers').and.returnValue(of({ items: [], totalCount: 0 })),
    ...adminOverrides,
  };
  const snackbar = { info: jasmine.createSpy('info') };
  const router = { navigate: jasmine.createSpy('navigate').and.returnValue(Promise.resolve(true)) };
  const menu = { refresh: jasmine.createSpy('refresh') };
  // Standard: Admin; Tests überschreiben einzelne Felder.
  const authStub = { has: () => true, isAdminLive: true, ...auth };
  TestBed.configureTestingModule({
    providers: [
      { provide: AdminService, useValue: adminService },
      { provide: AuthService, useValue: authStub },
      { provide: MenuService, useValue: menu },
      { provide: Router, useValue: router },
      { provide: SnackbarService, useValue: snackbar },
      { provide: TranslateService, useValue: { instant: (k: string) => k } },
      { provide: ConfirmService, useValue: confirmStub },
      { provide: MatDialog, useValue: dialogStub },
      { provide: ChangeDetectorRef, useValue: { markForCheck: () => {} } },
    ],
  });
  const c = TestBed.runInInjectionContext(() => new AdminUsersTabComponent());
  return { c, adminService, snackbar, router, menu };
}

describe('AdminUsersTabComponent', () => {
  beforeEach(() => {
    confirmStub.answer = true;
    confirmStub.ask = jasmine.createSpy('ask').and.callFake(() => of(confirmStub.answer));
    dialogStub.open = jasmine.createSpy('open');
  });

  it('lädt beim Öffnen die erste Seite der Konten', () => {
    const getUsers = jasmine.createSpy('getUsers').and.returnValue(of({ items: [{ id: 1, username: 'a' }], totalCount: 41 }));
    const { c } = make({ getUsers });
    c.ngOnInit();
    expect(getUsers).toHaveBeenCalledWith('', 1, 20);
    expect(c.users().length).toBe(1);
    expect(c.usersTotalCount()).toBe(41);
    expect(c.usersLoading()).toBeFalse();
  });

  it('Blättern lädt mit Seite und Größe der Leiste neu', () => {
    const getUsers = jasmine.createSpy('getUsers').and.returnValue(of({ items: [], totalCount: 0 }));
    const { c } = make({ getUsers });
    c.userSearch.set('bob');
    c.onUsersPageChange({ pageIndex: 2, pageSize: 50, length: 0 } as any);
    expect(getUsers).toHaveBeenCalledWith('bob', 3, 50);
  });

  it('ein Ladefehler meldet sich und beendet das Laden', () => {
    const { c, snackbar } = make({ getUsers: jasmine.createSpy('getUsers').and.returnValue(throwError(() => ({ status: 500 }))) });
    c.loadUsers();
    expect(snackbar.info).toHaveBeenCalledWith('admin.users.errors.load');
    expect(c.usersLoading()).toBeFalse();
  });

  // UX-002 / P-2: Konten ohne E-Mail bekommen vom Admin einen einmaligen Reset-Link.
  it('createResetLink ruft den Dienst und zeigt den Link im Dialog (mit E-Mail-Hinweis)', () => {
    const res = { link: 'https://rookhub.example/reset-password?token=abc', expiresAt: '2026-10-25T08:00:00Z' };
    const createPasswordResetLink = jasmine.createSpy('createPasswordResetLink').and.returnValue(of(res));
    const { c, snackbar } = make({ createPasswordResetLink });

    c.createResetLink({ id: 7, username: 'spieler', email: '' } as any);

    expect(createPasswordResetLink).toHaveBeenCalledWith(7);
    expect(dialogStub.open).toHaveBeenCalledTimes(1);
    const [cmp, cfg] = dialogStub.open.calls.mostRecent().args;
    expect(cmp).toBe(ResetLinkDialogComponent);
    expect(cfg.data).toEqual({ username: 'spieler', link: res.link, expiresAt: res.expiresAt, hasEmail: false });
    expect(c.resetLinkBusyId()).toBeNull();
    expect(snackbar.info).not.toHaveBeenCalled();
  });

  it('createResetLink meldet einen Fehler per Snackbar und öffnet keinen Dialog', () => {
    const createPasswordResetLink = jasmine.createSpy('createPasswordResetLink')
      .and.returnValue(throwError(() => ({ status: 403, error: {} })));
    const { c, snackbar } = make({ createPasswordResetLink });

    c.createResetLink({ id: 3, username: 'boss', email: 'b@x' } as any);

    expect(dialogStub.open).not.toHaveBeenCalled();
    expect(snackbar.info).toHaveBeenCalledWith('admin.users.resetLink.error');
    expect(c.resetLinkBusyId()).toBeNull();
  });

  it('createResetLink sperrt Doppelklicks, solange ein Link entsteht', () => {
    const pending = new Subject<any>();
    const createPasswordResetLink = jasmine.createSpy('createPasswordResetLink').and.returnValue(pending);
    const { c } = make({ createPasswordResetLink });

    c.createResetLink({ id: 7, username: 'a' } as any);
    c.createResetLink({ id: 7, username: 'a' } as any);

    expect(createPasswordResetLink).toHaveBeenCalledTimes(1);
    expect(c.resetLinkBusyId()).toBe(7);
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
    expect(c.impersonatingId()).toBeNull();
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

  it('users.manage ohne Admin-Flag: kein Admin-Umschalter, Admin-Konten ohne Aktionen', () => {
    const { c } = make({}, { isAdminLive: false });
    expect(c.actorIsAdmin).toBeFalse();
    expect(c.canManageUser({ id: 5, isAdmin: false } as any)).toBeTrue();
    expect(c.canManageUser({ id: 6, isAdmin: true } as any)).toBeFalse();
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
      expect(c.adminBusyId()).toBeNull();
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
      expect(c.adminBusyId()).toBe(4);
      c.toggleAdmin(user);
      expect(setAdmin).toHaveBeenCalledTimes(1);

      pending.next({ id: 4, isAdmin: true });
      expect(c.adminBusyId()).toBeNull();
    });
  });

  it('Löschen fragt mit dem Namen nach und lädt danach neu', () => {
    const deleteUser = jasmine.createSpy('deleteUser').and.returnValue(of(null));
    const { c, adminService } = make({ deleteUser });
    c.deleteUser({ id: 9, username: 'weg' } as any);
    expect(confirmStub.ask).toHaveBeenCalledWith('admin.users.deleteConfirm', { username: 'weg' });
    expect(deleteUser).toHaveBeenCalledWith(9);
    expect(adminService.getUsers).toHaveBeenCalled();
  });
});

/** Gerendert (OnPush): die Darstellung folgt den Signalen, auch nach einer Antwort ohne Klick. */
describe('AdminUsersTabComponent – Darstellung', () => {
  async function render(users: any[], auth: any = {}) {
    TestBed.configureTestingModule({
      imports: [AdminUsersTabComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }), provideRouter([]),
        { provide: AdminService, useValue: { getUsers: () => of({ items: users, totalCount: users.length }) } },
        { provide: MenuService, useValue: { refresh: () => {} } },
        { provide: AuthService, useValue: { currentUser: { userId: 1, isAdmin: true }, has: () => true, isAdminLive: true, ...auth } },
      ],
    });
    const fixture = TestBed.createComponent(AdminUsersTabComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('creates (template AOT-compiles + DI resolves)', async () => {
    const fixture = await render([]);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('rendert Tabellenzeilen und Karten je Konto', async () => {
    const fixture = await render([
      { id: 3, username: 'kahalm', email: 'k@example.com', isAdmin: true, groups: ['Schwaz'], createdAt: '2026-01-01T00:00:00Z' },
      { id: 4, username: 'test', email: null, isAdmin: false, groups: [], createdAt: '2026-01-01T00:00:00Z' },
    ]);
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('tr.mat-mdc-row').length).toBe(2);
    expect(el.querySelectorAll('.user-card').length).toBe(2);
    expect(el.querySelector('mat-paginator')).not.toBeNull();
  });

  // UI-Sweep z2 (z2-rk-admin-suche-abstand): das Nutzer-Suchfeld reserviert keinen leeren Hilfstext-Platz.
  it('das Nutzer-Suchfeld reserviert keinen Platz für Hilfstext', async () => {
    const fixture = await render([]);
    const field = (fixture.nativeElement as HTMLElement).querySelector('mat-form-field.search-field') as HTMLElement;
    expect(field).not.toBeNull();
    expect(field.querySelector('.mat-mdc-form-field-subscript-dynamic-size')).not.toBeNull();
  });
});

describe('AdminUsersTabComponent – Stile', () => {
  const css = () => (((AdminUsersTabComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? [])
    .join('\n').replace(/%NS%/g, '').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s+/g, ' ');

  it('zeigt die Kartenfassung bis 899.98 px und laesst Datum/Aktionen der Tabelle nie umbrechen (a1-rk-admin-nutzer-schmal)', () => {
    expect(css()).toMatch(/@media \(max-width: ?899\.98px\) ?\{[^@]*\.users-table-wrap[^{]*\{ ?display: ?none/);
    expect(css()).not.toContain('599.98px');
    expect(css()).toMatch(/\.users-table td\.mat-column-actions[^{]*\{ ?white-space: ?nowrap/);
  });

  it('Suchfeld mit 4 px Nachabstand (Falz-Runde 16.10.)', () => {
    expect(css()).toMatch(/\.search-field ?\{[^}]*margin-bottom: ?4px/);
  });

  it('Kartenliste klebt nicht an der Blätterleiste (b1-rk-admin-handy-seitenwahl-klebt)', () => {
    expect(css()).toMatch(/@media \(max-width: ?899\.98px\) ?\{[^@]*\.user-cards ?\{[^}]*margin-bottom: ?12px/);
  });
});
