import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree } from '@angular/router';
import { Observable, of, throwError, isObservable } from 'rxjs';
import { menuGuard } from './menu.guard';
import { AuthService } from './auth.service';
import { MenuService } from './menu.service';
import { SnackbarService } from './snackbar.service';
import { TranslateService } from '@ngx-translate/core';

describe('menuGuard', () => {
  let snack: jasmine.Spy;

  /** `visible` = die Keys, die der Nutzer laut Snapshot sehen darf (Ausweich-Ziel des Guards). */
  function configure(loggedIn: boolean, check$: Observable<boolean>, visible: string[] = ['dashboard']) {
    snack = jasmine.createSpy('info');
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
        { provide: MenuService, useValue: { check: () => check$, isVisible: (k: string) => visible.includes(k) } },
        { provide: SnackbarService, useValue: { info: snack } },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
      ],
    });
  }

  function runSync(key = 'courses'): boolean | UrlTree {
    const result = TestBed.runInInjectionContext(() => menuGuard(key)({} as any, {} as any));
    let value!: boolean | UrlTree;
    (isObservable(result) ? result : of(result as any)).subscribe(v => (value = v));
    return value;
  }

  it('lässt durch, wenn der Menüeintrag sichtbar ist', () => {
    configure(true, of(true));
    expect(runSync()).toBe(true);
    expect(snack).not.toHaveBeenCalled();
  });

  it('nennt Angemeldeten beim Umleiten den Grund, statt sie stumm aufs Dashboard zu stellen (UX-026)', () => {
    configure(true, of(false));
    runSync();
    expect(snack).toHaveBeenCalledOnceWith('app.blocked');
  });

  it('meldet keine Sperre, wenn nur das Dashboard (die Startseite) ausgeblendet ist', () => {
    configure(true, of(false), ['help']);
    expect((runSync('dashboard') as UrlTree).toString()).toContain('/help');
    expect(snack).not.toHaveBeenCalled();
  });

  it('meldet Gästen keine Sperre — sie gehen zur Anmeldung', () => {
    configure(false, of(false));
    runSync();
    expect(snack).not.toHaveBeenCalled();
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

  it('gibt anonymen Nutzern Rücksprungziel und Anmelde-Hinweis mit (UX-024)', () => {
    configure(false, of(false));
    const result = TestBed.runInInjectionContext(() => menuGuard('analysis')({} as any, { url: '/analysis?fen=x' } as any));
    let res!: UrlTree;
    (isObservable(result) ? result : of(result as any)).subscribe(v => (res = v as UrlTree));
    expect(res.toString().split('?')[0]).toBe('/login');
    expect(res.queryParams).toEqual({ returnUrl: '/analysis?fen=x', authRequired: '1' });
  });

  it('fail-open: bei API-Fehler wird NICHT ausgesperrt (true)', () => {
    configure(true, throwError(() => new Error('netz weg')));
    expect(runSync()).toBe(true);
  });
});
