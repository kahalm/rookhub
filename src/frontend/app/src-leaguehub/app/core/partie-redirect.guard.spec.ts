import { TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRouteSnapshot, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { HandoffService } from '@rh/core/handoff.service';
import { SHARE_REDIRECT_TIMEOUT_MS } from '@rh/core/share-redirect.guard';
import { partieRedirectGuard } from './partie-redirect.guard';

/** LeagueHub `/partie/:token` → RookHub `/club-games/{id}` für Angemeldete mit normalem Zugriff (Wunsch 2026-11-22). */
describe('partieRedirectGuard', () => {
  let http: HttpTestingController;
  let handoff: { rookHubUrl: string | null; jumpToRookHub: jasmine.Spy };

  function setup(loggedIn: boolean, rookHubUrl: string | null = 'https://rookhub.example') {
    handoff = { rookHubUrl, jumpToRookHub: jasmine.createSpy('jumpToRookHub').and.resolveTo() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
        { provide: HandoffService, useValue: handoff },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  }

  function run(query: Record<string, string> = {}, fragment: string | null = null): { value: unknown } {
    const route = { paramMap: convertToParamMap({ token: 'tok' }), queryParams: query, fragment } as unknown as ActivatedRouteSnapshot;
    const out = { value: undefined as unknown };
    void (TestBed.runInInjectionContext(() => partieRedirectGuard(route, { url: '' } as never)) as Promise<unknown>).then(v => (out.value = v));
    return out;
  }

  afterEach(() => http.verify());

  it('Gäste: kein Request, die Teilen-Ansicht bleibt', fakeAsync(() => {
    setup(false);
    const out = run();
    flushMicrotasks();
    expect(out.value).toBeTrue();
    expect(handoff.jumpToRookHub).not.toHaveBeenCalled();
  }));

  it('ohne bekanntes RookHub (localhost) kein Sprung', fakeAsync(() => {
    setup(true, null);
    const out = run();
    flushMicrotasks();
    expect(out.value).toBeTrue();
  }));

  it('mit Zugriff: Sprung nach RookHub /club-games/{id}, Query und Fragment ohne Einmal-Code', fakeAsync(() => {
    setup(true);
    const out = run({ ply: '12', h: 'alt' }, 'm3');
    const req = http.expectOne(r => r.url === '/api/share-links/target');
    expect(req.request.params.get('kind')).toBe('lp');
    expect(req.request.params.get('token')).toBe('tok');
    req.flush({ path: '/club-games/241' });
    flushMicrotasks();
    expect(handoff.jumpToRookHub).toHaveBeenCalledWith('club-games/241?ply=12#m3');
    expect(out.value).toBeFalse();
  }));

  it('kein Zugriff (404): alles wie heute', fakeAsync(() => {
    setup(true);
    const out = run();
    http.expectOne(r => r.url === '/api/share-links/target').flush(null, { status: 404, statusText: 'Not Found' });
    flushMicrotasks();
    expect(out.value).toBeTrue();
    expect(handoff.jumpToRookHub).not.toHaveBeenCalled();
  }));

  it('ein fremdes Ziel wird nicht angesteuert (kein offener Umleiter)', fakeAsync(() => {
    setup(true);
    const out = run();
    http.expectOne(r => r.url === '/api/share-links/target').flush({ path: '//evil.example/x' });
    flushMicrotasks();
    expect(out.value).toBeTrue();
    expect(handoff.jumpToRookHub).not.toHaveBeenCalled();
  }));

  it('antwortet der Server nicht, zeigt sich nach der Frist die Teilen-Ansicht', fakeAsync(() => {
    setup(true);
    const out = run();
    http.expectOne(r => r.url === '/api/share-links/target');
    tick(SHARE_REDIRECT_TIMEOUT_MS + 1);
    flushMicrotasks();
    expect(out.value).toBeTrue();
  }));
});
