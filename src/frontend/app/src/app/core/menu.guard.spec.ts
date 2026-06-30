import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree } from '@angular/router';
import { Observable, of, throwError, isObservable } from 'rxjs';
import { menuGuard } from './menu.guard';
import { AuthService } from './auth.service';
import { MenuService } from './menu.service';

describe('menuGuard', () => {
  function configure(loggedIn: boolean, check$: Observable<boolean>) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
        { provide: MenuService, useValue: { check: () => check$ } },
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

  it('leitet anonyme Nutzer ohne Sichtbarkeit auf /login um', () => {
    configure(false, of(false));
    expect((runSync() as UrlTree).toString()).toContain('/login');
  });

  it('fail-open: bei API-Fehler wird NICHT ausgesperrt (true)', () => {
    configure(true, throwError(() => new Error('netz weg')));
    expect(runSync()).toBe(true);
  });
});
