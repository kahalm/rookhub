import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree } from '@angular/router';
import { of } from 'rxjs';
import { adminGuard } from './admin.guard';
import { AuthService } from './auth.service';
import { SnackbarService } from './snackbar.service';
import { TranslateService } from '@ngx-translate/core';

describe('adminGuard', () => {
  let snack: jasmine.Spy;

  function configure(loggedIn: boolean, isAdmin: boolean) {
    snack = jasmine.createSpy('info');
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn, isAdmin } },
        { provide: SnackbarService, useValue: { info: snack } },
        { provide: TranslateService, useValue: { get: (k: string) => of(k) } },
      ],
    });
  }

  const run = () => TestBed.runInInjectionContext(() => adminGuard({} as any, { url: '/admin' } as any));

  it('lässt eingeloggte Admins durch', () => {
    configure(true, true);
    expect(run()).toBe(true);
    expect(snack).not.toHaveBeenCalled();
  });

  it('nennt eingeloggten Nicht-Admins den Grund der Umleitung (UX-026)', () => {
    configure(true, false);
    run();
    expect(snack).toHaveBeenCalledOnceWith('app.blocked');
  });

  it('leitet eingeloggte Nicht-Admins auf /dashboard um', () => {
    configure(true, false);
    const res = run() as UrlTree;
    expect(res instanceof UrlTree).toBeTrue();
    expect(res.toString()).toContain('/dashboard');
  });

  it('schickt anonyme Nutzer direkt zur Anmeldung, mit /admin als Ziel und Hinweis (UX-024)', () => {
    // Bisher erst aufs Dashboard — dessen authGuard gab dann „/dashboard“ statt „/admin“ als Ziel mit.
    configure(false, false);
    const res = run() as UrlTree;
    expect(res instanceof UrlTree).toBeTrue();
    expect(res.toString().split('?')[0]).toBe('/login');
    expect(res.queryParams).toEqual({ returnUrl: '/admin', authRequired: '1' });
    expect(snack).not.toHaveBeenCalled();
  });
});
