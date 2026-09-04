import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree } from '@angular/router';
import { Observable, of, throwError, isObservable } from 'rxjs';
import { menuGuard } from './menu.guard';
import { AuthService } from './auth.service';
import { MenuService } from './menu.service';

describe('menuGuard', () => {
  /** `visible` = die Keys, die der Nutzer laut Snapshot sehen darf (Ausweich-Ziel des Guards). */
  function configure(loggedIn: boolean, check$: Observable<boolean>, visible: string[] = ['dashboard']) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
        { provide: MenuService, useValue: { check: () => check$, isVisible: (k: string) => visible.includes(k) } },
      ],
    });
  }

  function runSync(): boolean | UrlTree {
    const result = TestBed.runInInjectionContext(() => menuGuard('courses')({} as any, {} as any));
    let value!: boolean | UrlTree;
    (isObservable(result) ? result : of(result as any)).subscribe(v => (value = v));
    return value;
  }

  it('lässt durch, wenn der Menüeintrag sichtbar ist', () => {
    configure(true, of(true));
    expect(runSync()).toBe(true);
  });

  it('leitet eingeloggte Nutzer ohne Sichtbarkeit auf /dashboard um', () => {
    configure(true, of(false));
    const res = runSync() as UrlTree;
    expect(res instanceof UrlTree).toBeTrue();
    expect(res.toString()).toContain('/dashboard');
  });

  it('weicht auf /help aus, wenn das Dashboard selbst gesperrt ist', () => {
    // FALLE: /dashboard trägt selbst menuGuard('dashboard'). Ist der Eintrag für den Nutzer
    // gesperrt, schickte der Guard ihn auf eine Route, die derselbe Guard wieder ablehnt — die
    // Navigation drehte endlos und der Nutzer landete auf KEINER Seite.
    configure(true, of(false), ['help']);
    expect((runSync() as UrlTree).toString()).toContain('/help');
  });

  it('landet auf /login, wenn gar nichts sichtbar ist (einzige guard-freie Seite)', () => {
    configure(true, of(false), []);
    expect((runSync() as UrlTree).toString()).toContain('/login');
  });

  it('leitet anonyme Nutzer ohne Sichtbarkeit auf /login um', () => {
    configure(false, of(false));
    expect((runSync() as UrlTree).toString()).toContain('/login');
  });

  it('fail-open: bei API-Fehler wird NICHT ausgesperrt (true)', () => {
    configure(true, throwError(() => new Error('netz weg')));
    expect(runSync()).toBe(true);
  });
});
