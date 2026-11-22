import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRouteSnapshot, RedirectCommand, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';
import { AuthService } from './auth.service';
import { MenuService } from './menu.service';
import { SHARE_REDIRECT_TIMEOUT_MS, ShareLinkKind, shareRedirectGuard } from './share-redirect.guard';

/** „Teilen-Link → volle Seite“ (2026-11-22): Umleitung nur für Angemeldete mit Zugriff, Unterseite + Query bleiben. */
describe('shareRedirectGuard', () => {
  let http: HttpTestingController;
  let menuCheck: jasmine.Spy;

  function setup(loggedIn: boolean, menuOk = true) {
    menuCheck = jasmine.createSpy('check').and.returnValue(of(menuOk));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
        { provide: MenuService, useValue: { check: menuCheck } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  }

  function run(kind: ShareLinkKind, suffix: string[], params: Record<string, string>, query: Record<string, string> = {}, fragment: string | null = null) {
    const route = { paramMap: convertToParamMap(params), queryParams: query, fragment } as unknown as ActivatedRouteSnapshot;
    return TestBed.runInInjectionContext(() => shareRedirectGuard(kind, suffix)(route, { url: '' } as never));
  }

  function collect(res: unknown): { value: unknown } {
    const out = { value: undefined as unknown };
    (res as Observable<unknown>).subscribe(v => (out.value = v));
    return out;
  }

  const url = (v: unknown) => (v as RedirectCommand).redirectTo.toString();

  afterEach(() => http.verify());

  it('Gäste: kein Request, die Teilen-Ansicht bleibt', () => {
    setup(false);
    expect(run('cg', [], { token: 'abc' })).toBeTrue();
    http.expectNone(() => true);
  });

  it('/cg/{token}/summary → /club-games/{id}/summary mit ersetzter Adresse', () => {
    setup(true);
    const out = collect(run('cg', ['summary'], { token: 'abc' }));
    const req = http.expectOne(r => r.url === '/api/share-links/target');
    expect(req.request.params.get('kind')).toBe('cg');
    expect(req.request.params.get('token')).toBe('abc');
    expect(req.request.params.has('id')).toBeFalse();
    req.flush({ path: '/club-games/241' });
    expect(out.value instanceof RedirectCommand).toBeTrue();
    expect(url(out.value)).toBe('/club-games/241/summary');
    expect((out.value as RedirectCommand).navigationBehaviorOptions?.replaceUrl).toBeTrue();
    expect(menuCheck).not.toHaveBeenCalled();
  });

  it('/cg/{token}?train=white → /club-games/{id}?train=white (Query und Fragment bleiben)', () => {
    setup(true);
    const out = collect(run('cg', [], { token: 'abc' }, { train: 'white', ply: '12' }, 'm3'));
    http.expectOne(r => r.url === '/api/share-links/target').flush({ path: '/club-games/7' });
    expect(url(out.value)).toBe('/club-games/7?train=white&ply=12#m3');
  });

  it('/g/{token} → /games/{id}, nur mit sichtbarem Menüpunkt „games“', () => {
    setup(true);
    const out = collect(run('g', [], { token: 'tok' }, { roast: '3' }));
    http.expectOne(r => r.url === '/api/share-links/target').flush({ path: '/games/12' });
    expect(menuCheck).toHaveBeenCalledWith('games');
    expect(url(out.value)).toBe('/games/12?roast=3');
  });

  it('Liga-Link /s/{token}/club-games/{id} schickt die Id mit', () => {
    setup(true);
    const out = collect(run('s', ['summary'], { token: 'lig', id: '55' }));
    const req = http.expectOne(r => r.url === '/api/share-links/target');
    expect(req.request.params.get('id')).toBe('55');
    req.flush({ path: '/club-games/55' });
    expect(url(out.value)).toBe('/club-games/55/summary');
  });

  it('/tl/{token}/drill → /training-videos/{id}/drill', () => {
    setup(true);
    const out = collect(run('tl', ['drill'], { token: 't' }));
    http.expectOne(r => r.url === '/api/share-links/target').flush({ path: '/training-videos/4' });
    expect(menuCheck).toHaveBeenCalledWith('training-videos');
    expect(url(out.value)).toBe('/training-videos/4/drill');
  });

  it('kein Zugriff (404): keine Umleitung', () => {
    setup(true);
    const out = collect(run('cg', ['summary'], { token: 'abc' }));
    http.expectOne(r => r.url === '/api/share-links/target').flush(null, { status: 404, statusText: 'Not Found' });
    expect(out.value).toBeTrue();
  });

  it('ausgeblendeter Menüpunkt des Ziels: keine Umleitung', () => {
    setup(true, false);
    const out = collect(run('g', [], { token: 'tok' }));
    http.expectOne(r => r.url === '/api/share-links/target').flush({ path: '/games/12' });
    expect(out.value).toBeTrue();
  });

  it('ein Ziel außerhalb der bekannten Seiten wird nicht angesteuert (kein offener Umleiter)', () => {
    setup(true);
    const out = collect(run('cg', [], { token: 'abc' }));
    http.expectOne(r => r.url === '/api/share-links/target').flush({ path: '//evil.example/x' });
    expect(out.value).toBeTrue();
  });

  it('antwortet der Server nicht, zeigt sich nach der Frist die Teilen-Ansicht', fakeAsync(() => {
    setup(true);
    const out = collect(run('cg', [], { token: 'abc' }));
    http.expectOne(r => r.url === '/api/share-links/target');
    tick(SHARE_REDIRECT_TIMEOUT_MS + 1);
    expect(out.value).toBeTrue();
  }));
});
