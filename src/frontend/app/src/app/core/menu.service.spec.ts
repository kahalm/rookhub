import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { MenuService } from './menu.service';
import { AuthService } from './auth.service';

describe('MenuService', () => {
  let service: MenuService;
  let httpMock: HttpTestingController;
  let user$: BehaviorSubject<unknown>;

  beforeEach(() => {
    localStorage.clear();   // Offline-Cache (rookhub_menu_keys) zwischen Tests isolieren
    user$ = new BehaviorSubject<unknown>(null);
    TestBed.configureTestingModule({
      providers: [
        MenuService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { currentUser$: user$.asObservable() } },
      ],
    });
    service = TestBed.inject(MenuService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => { httpMock.verify(); localStorage.clear(); });

  // Der Konstruktor lädt sofort einmal (currentUser$ ist BehaviorSubject).
  function flushInitial(keys: string[] = []): void {
    httpMock.expectOne('/api/menu').flush(keys);
  }

  it('loads visible keys on construction and exposes them via isVisible + visible$', () => {
    let snapshot: Set<string> | undefined;
    service.visible$.subscribe(s => (snapshot = s));
    flushInitial(['courses', 'leaderboards']);

    expect(service.isVisible('courses')).toBeTrue();
    expect(service.isVisible('weekly')).toBeFalse();
    expect(snapshot?.has('leaderboards')).toBeTrue();
  });

  it('isVisible is false for everything when the request fails and nothing is cached', () => {
    httpMock.expectOne('/api/menu').flush('boom', { status: 500, statusText: 'err' });
    expect(service.isVisible('courses')).toBeFalse();
  });

  it('keeps the cached keys when a later fetch fails (offline reload)', () => {
    // Vorlauf: ein erfolgreicher Abruf cacht die Keys in localStorage.
    flushInitial(['courses', 'puzzles']);
    expect(service.isVisible('courses')).toBeTrue();

    // Späterer fetch scheitert (Flugmodus) → Menü bleibt der gecachte Stand statt leer.
    service.refresh();
    httpMock.expectOne('/api/menu').flush('down', { status: 0, statusText: 'offline' });
    expect(service.isVisible('courses')).toBeTrue();
    expect(service.isVisible('puzzles')).toBeTrue();
  });

  it('seeds the snapshot synchronously from the cache on construction (offline cold start)', () => {
    // Cache befüllen, dann eine frische Instanz im Flugmodus erzeugen.
    flushInitial(['courses', 'leaderboards']);
    const offline = new MenuService(
      TestBed.inject(HttpClient),
      { currentUser$: user$.asObservable() } as never,
    );
    // Cache greift synchron — schon vor der (offline scheiternden) /api/menu-Antwort.
    expect(offline.isVisible('courses')).toBeTrue();
    httpMock.expectOne('/api/menu').flush('down', { status: 0, statusText: 'offline' });
    expect(offline.isVisible('leaderboards')).toBeTrue();
  });

  it('refresh re-fetches and replaces the snapshot', () => {
    flushInitial(['a']);
    expect(service.isVisible('a')).toBeTrue();

    service.refresh();
    httpMock.expectOne('/api/menu').flush(['b']);
    expect(service.isVisible('a')).toBeFalse();
    expect(service.isVisible('b')).toBeTrue();
  });

  it('check() resolves to true/false for a given key without touching the snapshot', () => {
    flushInitial(['x']);

    let result: boolean | undefined;
    service.check('y').subscribe(r => (result = r));
    httpMock.expectOne('/api/menu').flush(['y']);
    expect(result).toBeTrue();

    // Snapshot bleibt der initiale Stand (check ist eine eigene Abfrage).
    expect(service.isVisible('x')).toBeTrue();
  });

  it('reloads when the auth state changes', () => {
    flushInitial(['anon']);
    expect(service.isVisible('anon')).toBeTrue();

    user$.next({ token: 't' });          // Login → switchMap löst neuen fetch aus
    httpMock.expectOne('/api/menu').flush(['member']);
    expect(service.isVisible('member')).toBeTrue();
  });
});
