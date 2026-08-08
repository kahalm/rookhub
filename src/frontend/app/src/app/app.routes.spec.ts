import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Route, Router, provideRouter } from '@angular/router';
import { routes } from './app.routes';

/**
 * Reihenfolge-Test der Routentabelle.
 *
 * Hintergrund: für die öffentlichen Kurz-URLs gibt es zwei Catch-all-artige Routen — `:slug`
 * (ein Segment) und `:slug/:chapter` (ZWEI Segmente). Die zweiteilige ist die gefährliche: steht
 * sie auch nur eine Zeile zu früh, verschluckt sie jede echte zweiteilige Route (`/courses/403`,
 * `/repertoires/12`, `/tournaments/9`, `/t/5`, …) — niemand käme mehr in seine Kurse, und zwar
 * ohne Fehlermeldung: der Slug-Auflöser schickt bei unbekanntem Alias einfach aufs Dashboard.
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

  it('lässt die übrigen zweiteiligen Routen unangetastet', async () => {
    expect(await matchedPath('/tournaments/9')).toBe('tournaments/:id');
    expect(await matchedPath('/puzzles/12')).toBe('puzzles/:id');
    expect(await matchedPath('/weekly/5')).toBe('weekly/:weeklyId');
    expect(await matchedPath('/t/5')).toBe('t/:id');
    expect(await matchedPath('/g/abc')).toBe('g/:token');
    expect(await matchedPath('/l/abc')).toBe('l/:token');
    expect(await matchedPath('/friends/3/stats')).toBe('friends/:userId/stats');
    expect(await matchedPath('/courses/403/calc')).toBe('courses/:bookId/calc');
    expect(await matchedPath('/courses/403/flashcards')).toBe('courses/:bookId/flashcards');
  });

  it('lässt literale Einzelsegmente vor dem Slug matchen', async () => {
    expect(await matchedPath('/dashboard')).toBe('dashboard');
    expect(await matchedPath('/courses')).toBe('courses');
    expect(await matchedPath('/analysis')).toBe('analysis');
  });

  it('fängt unbekannte Einzel- und Doppelsegmente als Kurz-URL ab', async () => {
    expect(await matchedPath('/noel')).toBe(':slug');
    expect(await matchedPath('/noel/KW46')).toBe(':slug/:chapter');
  });

  it('leitet alles Längere weiterhin aufs Dashboard um', async () => {
    expect(await matchedPath('/noel/KW46/zuviel')).toBe('dashboard');
  });
});
