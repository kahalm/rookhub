import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { CatalogComponent } from './catalog.component';
import { CatalogService } from './catalog.service';
import { AuthService } from '../../core/auth.service';
import { AdminService } from '../../core/admin.service';
import { SnackbarService } from '../../core/snackbar.service';
import { FriendsService } from '../../core/friends.service';

describe('CatalogComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [CatalogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(CatalogComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  // W3 F5-003: setGrants ERSETZT alle Freigaben — nach einem Ladefehler darf Speichern nichts entziehen.
  describe('Freigaben', () => {
    // Codereview F5-005: Besitzer ist, wer catalog.manage hat — hier bewusst KEIN Admin.
    function setup(getGrants: any, has: (p: string) => boolean = p => p === 'catalog.manage',
                   search = jasmine.createSpy('search').and.returnValue(of([]))) {
      const svc = {
        list: jasmine.createSpy('list').and.returnValue(of([])),
        getGrants,
        getRequests: jasmine.createSpy('getRequests').and.returnValue(of([])),
        setGrants: jasmine.createSpy('setGrants').and.returnValue(of({ userIds: [], groupIds: [] })),
      };
      const snackbar = { info: jasmine.createSpy('info') };
      TestBed.configureTestingModule({
        imports: [CatalogComponent],
        providers: [
          provideNoopAnimations(),
          provideRouter([]),
          provideTranslateService({ fallbackLang: 'en' }),
          { provide: CatalogService, useValue: svc },
          { provide: AuthService, useValue: { isAdmin: false, has } },
          { provide: AdminService, useValue: {
            getUsers: () => of({ items: [], totalCount: 0 }), getGroups: () => of([]) } },
          { provide: SnackbarService, useValue: snackbar },
          { provide: FriendsService, useValue: { search } },
        ],
      });
      const fixture = TestBed.createComponent(CatalogComponent);
      fixture.detectChanges();
      return { fixture, c: fixture.componentInstance, svc, snackbar, search };
    }

    it('Ladefehler → Hinweis, Speichern-Knopf gesperrt, kein setGrants', () => {
      const { fixture, c, svc, snackbar } = setup(
        jasmine.createSpy('getGrants').and.returnValue(throwError(() => ({ status: 502 }))));
      expect(snackbar.info).toHaveBeenCalled();
      expect(c.grantsLoaded).toBeFalse();
      const btn: HTMLButtonElement = fixture.nativeElement.querySelector('.grant-selects button');
      expect(btn.disabled).toBeTrue();
      c.saveGrants();
      expect(svc.setGrants).not.toHaveBeenCalled();
    });

    it('nach erfolgreichem Laden speichert der Knopf den geladenen Satz', () => {
      const { fixture, c, svc } = setup(
        jasmine.createSpy('getGrants').and.returnValue(of({ userIds: [3], groupIds: [1] })));
      const btn: HTMLButtonElement = fixture.nativeElement.querySelector('.grant-selects button');
      expect(btn.disabled).toBeFalse();
      c.saveGrants();
      expect(svc.setGrants).toHaveBeenCalledWith({ userIds: [3], groupIds: [1] });
    });

    it('User per Suche (wie bei Freunden) freigeben und wieder entfernen', () => {
      const search = jasmine.createSpy('search').and.returnValue(of([
        { userId: 5, username: 'anna', displayName: 'Anna A', chessComUsername: 'annaC', lichessUsername: null, fideId: null, chessResultsId: null },
        { userId: 3, username: 'bert', displayName: null, chessComUsername: null, lichessUsername: null, fideId: null, chessResultsId: null },
      ]));
      const { fixture, c, svc } = setup(jasmine.createSpy('getGrants').and.returnValue(of({
        userIds: [3], groupIds: [], users: [{ userId: 3, username: 'bert', displayName: null }] })), undefined, search);
      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelector('.granted-users')?.textContent).toContain('bert');

      c.searchQuery = 'a'; c.search();
      expect(search).not.toHaveBeenCalled();   // < 2 Zeichen
      c.searchQuery = 'an'; c.search();
      expect(search).toHaveBeenCalledWith('an');
      fixture.detectChanges();
      const rows = el.querySelectorAll('.search-row');
      expect(rows.length).toBe(2);
      expect(rows[0].textContent).toContain('chess.com: annaC');
      expect(rows[1].querySelector('.granted-mark')).not.toBeNull();   // bert schon freigegeben

      (rows[0].querySelector('button') as HTMLButtonElement).click();
      fixture.detectChanges();
      c.removeUser(3);
      c.saveGrants();
      expect(svc.setGrants).toHaveBeenCalledWith({ userIds: [5], groupIds: [] });
    });

    it('ohne catalog.manage: keine Besitzer-Karte, keine Besitzer-Abfragen', () => {
      const getGrants = jasmine.createSpy('getGrants').and.returnValue(of({ userIds: [], groupIds: [] }));
      const { fixture, svc } = setup(getGrants, () => false);
      expect(getGrants).not.toHaveBeenCalled();
      expect(svc.getRequests).not.toHaveBeenCalled();
      expect(fixture.nativeElement.querySelector('.grant-selects')).toBeNull();
    });
  });

  // W5 F5-017: ein Ladefehler der Liste darf nicht wie „nichts freigegeben" aussehen.
  it('Ladefehler der Liste → Fehlerhinweis mit Erneut-Knopf statt Leertext', () => {
    const list = jasmine.createSpy('list').and.returnValues(throwError(() => ({ status: 500 })), of([]));
    TestBed.configureTestingModule({
      imports: [CatalogComponent],
      providers: [
        provideNoopAnimations(), provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: CatalogService, useValue: { list } },
        { provide: AuthService, useValue: { isAdmin: false, has: () => false } },
        { provide: AdminService, useValue: {} },
        { provide: SnackbarService, useValue: { info: () => {} } },
      ],
    });
    const fixture = TestBed.createComponent(CatalogComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(fixture.componentInstance.loadError).toBeTrue();
    expect(el.querySelector('app-load-error')).not.toBeNull();
    expect(el.textContent).not.toContain('catalog.empty');
    (el.querySelector('app-load-error button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(list).toHaveBeenCalledTimes(2);
    expect(el.querySelector('app-load-error')).toBeNull();
    expect(el.textContent).toContain('catalog.empty');
  });
});
