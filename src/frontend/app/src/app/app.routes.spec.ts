import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, CanActivateFn, Route, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { appConfig } from './app.config';
import { AuthService } from './core/auth.service';
import { checkSharedPageLinks } from './testing/shared-page-links';
import { NotFoundComponent } from './shared/not-found/not-found.component';

/**
 * Reihenfolge-Test der Routentabelle.
 *
 * Hintergrund: für die öffentlichen Kurz-URLs gibt es zwei Catch-all-artige Routen — `:slug`
 * (ein Segment) und `:slug/:chapter` (ZWEI Segmente). Die zweiteilige ist die gefährliche: steht
 * sie auch nur eine Zeile zu früh, verschluckt sie jede echte zweiteilige Route (`/courses/403`,
 * `/repertoires/12`, `/tournaments/9`, `/t/5`, …) — niemand käme mehr in seine Kurse, und zwar
 * ohne Fehlermeldung: der Slug-Auflöser zeigt bei unbekanntem Alias nur „Seite nicht gefunden“.
 *
 * Geprüft wird gegen den ECHTEN Router: dieselbe Tabelle, dieselbe Reihenfolge, nur ohne Guards
 * (die würden HTTP ziehen) und ohne Lazy-Chunks (die brauchen die volle Komponenten-DI). Was hier
 * zählt, ist ausschließlich, WELCHE Route ein Pfad trifft.
 */
@Component({ standalone: true, template: '' })
class StubRouteComponent {}

function testRoutes(): Route[] {
  return routes.map(r => (r.redirectTo
    ? { path: r.path, pathMatch: r.pathMatch, redirectTo: r.redirectTo }
    : { path: r.path, pathMatch: r.pathMatch, component: StubRouteComponent }) as Route);
}

async function matchedPath(url: string): Promise<string | undefined> {
  const router = TestBed.inject(Router);
  await router.navigateByUrl(url);
  return router.routerState.snapshot.root.firstChild?.routeConfig?.path;
}

describe('app.routes', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter(testRoutes())] });
  });

  it('lässt die zweiteilige Slug-Route ganz am Ende stehen (direkt vor dem Catch-all)', () => {
    const paths = routes.map(r => r.path);
    expect(paths.at(-1)).toBe('**');
    expect(paths.at(-2)).toBe(':slug/:chapter');
    expect(paths.at(-3)).toBe(':slug');
  });

  it('lässt echte zweiteilige Routen unangetastet — /courses/403 trifft weiterhin den Kurs', async () => {
    expect(await matchedPath('/courses/403')).toBe('courses/:bookId');
  });

  it('lässt /repertoires/12 weiterhin auf die Repertoire-Route laufen', async () => {
    expect(await matchedPath('/repertoires/12')).toBe('repertoires/:id');
  });

  it('faengt die umgezogenen Turnier-Adressen ab, statt sie in die Slug-Route laufen zu lassen', async () => {
    // Turniere sind eine eigene Seite (turnier.oberschmid.homes). Die Routen hier ERSATZLOS zu
    // streichen war ein Fehler: alte Lesezeichen, geteilte /t/{id}-Links und die Dashboard-Liste
    // „Abonnierte Turniere" zeigen weiter dorthin — sie fielen dann NICHT ins Catch-all, sondern
    // in ':slug/:chapter', bekamen 404 und landeten still auf dem Dashboard.
    expect(await matchedPath('/tournaments')).toBe('tournaments');
    expect(await matchedPath('/tournaments/9')).toBe('tournaments/:id');
    expect(await matchedPath('/t/5')).toBe('t/:id');
  });

  it('lässt die übrigen zweiteiligen Routen unangetastet', async () => {
    expect(await matchedPath('/puzzles/12')).toBe('puzzles/:id');
    expect(await matchedPath('/weekly/5')).toBe('weekly/:weeklyId');
    expect(await matchedPath('/g/abc')).toBe('g/:token');
    expect(await matchedPath('/l/abc')).toBe('l/:token');
    expect(await matchedPath('/friends/3/stats')).toBe('friends/:userId/stats');
    expect(await matchedPath('/courses/403/calc')).toBe('courses/:bookId/calc');
    expect(await matchedPath('/courses/403/flashcards')).toBe('courses/:bookId/flashcards');
    expect(await matchedPath('/prep/31252')).toBe('prep/:id');
  });

  it('lässt literale Einzelsegmente vor dem Slug matchen', async () => {
    expect(await matchedPath('/dashboard')).toBe('dashboard');
    expect(await matchedPath('/courses')).toBe('courses');
    expect(await matchedPath('/analysis')).toBe('analysis');
    expect(await matchedPath('/prep')).toBe('prep');
  });

  it('fängt unbekannte Einzel- und Doppelsegmente als Kurz-URL ab', async () => {
    expect(await matchedPath('/noel')).toBe(':slug');
    expect(await matchedPath('/noel/KW46')).toBe(':slug/:chapter');
  });

  it('zeigt für alles Längere die Hinweisseite, statt still aufs Dashboard umzuleiten (UX-026)', async () => {
    expect(await matchedPath('/noel/KW46/zuviel')).toBe('**');
    // Ohne Umleitung und ohne Guard: Gäste sollen nicht vor der Anmeldemaske landen und dabei das Ziel verlieren.
    const catchAll = routes.at(-1)!;
    expect(catchAll.redirectTo).toBeUndefined();
    expect(catchAll.canActivate).toBeUndefined();
    expect(await catchAll.loadComponent!()).toBe(NotFoundComponent);
  });
});

describe('app.routes — Startadresse (UX-025)', () => {
  // Die Umleitung von „/“ fragt den Anmeldestand; die Tabelle ist dieselbe, nur ohne Guards (s. o.).
  function start(isLoggedIn: boolean): void {
    TestBed.configureTestingModule({
      providers: [provideRouter(testRoutes()), { provide: AuthService, useValue: { isLoggedIn } }],
    });
  }

  it('schickt Gäste von „/“ in einen offenen Bereich (Puzzles) statt vor die Anmeldesperre des Dashboards', async () => {
    start(false);
    expect(await matchedPath('/')).toBe('puzzles');
  });

  it('schickt Angemeldete von „/“ weiterhin aufs Dashboard', async () => {
    start(true);
    expect(await matchedPath('/')).toBe('dashboard');
  });
});

describe('app.routes — Links der Anmelde- und Rechtsseiten', () => {
  // Eigener Block ohne das beforeEach oben: der Helfer stellt sich das TestBed selbst zusammen.
  it('jeder Link der geteilten Anmelde- und Rechtsseiten trifft seinen eigenen Weg, nicht die Kurz-URL (UX-003)', async () => {
    const report = await checkSharedPageLinks(routes, appConfig);
    expect(report.mounted).toEqual(jasmine.arrayWithExactContents(['login', 'register', 'forgot-password', 'reset-password', 'privacy', 'impressum', 'account-deletion']));
    // „Konto jetzt loeschen" fuehrt ins Profil — bewusst ueber die Anmeldung (data-login-required, UX-023).
    expect(report.links).toContain('/account-deletion → /profile');
    expect(report.problems).toEqual([]);
  });
});

describe('app.routes — Kurs-Seiten hinter der Anmeldung (F1-017)', () => {
  // Bisher eigener courseAccessGuard mit derselben Bedingung wie authGuard, aber Umleitung auf das nackte /login:
  // kein Hinweis „bitte einloggen“, und nach der Anmeldung das Dashboard statt der Kursseite.
  const COURSE_PAGES = ['courses', 'courses/:bookId', 'courses/:bookId/browse',
    'courses/:bookId/chapter/:chapterIndex/browse', 'courses/:bookId/flashcards'];

  it('schicken Gäste mit Rücksprungziel und Anmelde-Hinweis auf /login', () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: { isLoggedIn: false } }],
    });
    for (const path of COURSE_PAGES) {
      const guard = routes.find(r => r.path === path)?.canActivate?.[0] as CanActivateFn;
      const url = '/' + path.replace(':bookId', '340').replace(':chapterIndex', '2');
      const result = TestBed.runInInjectionContext(
        () => guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot)) as UrlTree;
      expect(result instanceof UrlTree).withContext(path).toBeTrue();
      expect(result.toString().split('?')[0]).withContext(path).toBe('/login');
      expect(result.queryParams).withContext(path).toEqual({ returnUrl: url, authRequired: '1' });
    }
  });
});
