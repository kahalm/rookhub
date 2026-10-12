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
                   search = jasmine.createSpy('search').and.returnValue(of([])), open = true, requests: any[] = []) {
      const svc = {
        list: jasmine.createSpy('list').and.returnValue(of([])),
        getGrants,
        getRequests: jasmine.createSpy('getRequests').and.returnValue(of(requests)),
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
      if (open) { fixture.componentInstance.adminOpen = true; fixture.detectChanges(); }
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

  // UI-Sweep 2026-10-11 (d2-rk-catalog-freigaben-zuerst): Liste zuerst, Verwaltung zugeklappt darunter.
  describe('Freigabe-Verwaltung zugeklappt', () => {
    function make(requests: any[]) {
      const svc = {
        list: () => of([]),
        getGrants: () => of({ userIds: [3], groupIds: [], users: [{ userId: 3, username: 'bert', displayName: null }] }),
        getRequests: () => of(requests),
      };
      TestBed.configureTestingModule({
        imports: [CatalogComponent],
        providers: [
          provideNoopAnimations(), provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
          { provide: CatalogService, useValue: svc },
          { provide: AuthService, useValue: { isAdmin: true, has: () => true } },
          { provide: AdminService, useValue: { getGroups: () => of([]) } },
          { provide: SnackbarService, useValue: { info: () => {} } },
          { provide: FriendsService, useValue: { search: () => of([]) } },
        ],
      });
      const fixture = TestBed.createComponent(CatalogComponent);
      fixture.detectChanges();
      return fixture;
    }

    it('ohne offene Anforderungen: Liste vor der Verwaltung, Verwaltung zu, Kopf mit Zählern', () => {
      const fixture = make([]);
      const el: HTMLElement = fixture.nativeElement;
      const cards = Array.from(el.querySelectorAll('.catalog > mat-card'));
      expect(cards.length).toBe(2);
      expect(cards[0].classList.contains('admin-card')).toBeFalse();
      expect(cards[1].classList.contains('admin-card')).toBeTrue();
      const toggle = el.querySelector('.admin-toggle') as HTMLButtonElement;
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
      expect(toggle.textContent).toContain('catalog.admin.summaryUsersOne');
      expect(toggle.textContent).toContain('catalog.admin.summaryOpen');
      expect(el.querySelector('.grant-selects')).toBeNull();
      toggle.click();
      fixture.detectChanges();
      expect(el.querySelector('.grant-selects')).not.toBeNull();
      expect(el.querySelector('.admin-card .empty-state')).toBeNull();   // Leerzustände einzeilig
      expect(el.querySelector('.admin-card .muted-line')).not.toBeNull();
    });

    it('mit offenen Anforderungen gleich aufgeklappt', () => {
      const fixture = make([{ id: 1, requesterName: 'anna', itemType: 'course', itemId: 2, itemName: 'K' }]);
      const el: HTMLElement = fixture.nativeElement;
      expect(fixture.componentInstance.adminOpen).toBeTrue();
      expect(el.querySelector('.req-row')).not.toBeNull();
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
  // UI-Sweep z1 (z1-rk-catalog-leer): Leerzustand erklärt, wer freigibt, und bietet einen Weg weiter.
  it('leere Liste → Erklärung + Knopf „Zu meinen Kursen“', () => {
    TestBed.configureTestingModule({
      imports: [CatalogComponent],
      providers: [
        provideNoopAnimations(), provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: CatalogService, useValue: { list: () => of([]) } },
        { provide: AuthService, useValue: { isAdmin: false, has: () => false } },
        { provide: AdminService, useValue: {} },
        { provide: SnackbarService, useValue: { info: () => {} } },
      ],
    });
    const fixture = TestBed.createComponent(CatalogComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.catalog-empty-text')?.textContent).toContain('catalog.emptyHint');
    const link = el.querySelector('.catalog-empty a') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/courses');
    expect(link.textContent).toContain('catalog.toCourses');
  });
});
