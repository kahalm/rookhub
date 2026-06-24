import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { MenuService } from './menu.service';
import { AuthService } from './auth.service';

describe('MenuService', () => {
  let service: MenuService;
  let httpMock: HttpTestingController;
  let user$: BehaviorSubject<unknown>;

  beforeEach(() => {
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

  afterEach(() => httpMock.verify());

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

  it('isVisible is false for everything when the request fails', () => {
    httpMock.expectOne('/api/menu').flush('boom', { status: 500, statusText: 'err' });
    expect(service.isVisible('courses')).toBeFalse();
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
