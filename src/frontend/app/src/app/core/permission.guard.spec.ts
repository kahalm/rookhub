import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { of } from 'rxjs';
import { TranslateService } from '@ngx-translate/core';
import { permissionGuard } from './permission.guard';
import { AuthService } from './auth.service';
import { SnackbarService } from './snackbar.service';

/**
 * RBAC-Guard: durchlassen nur bei eingeloggt UND passender Permission (Admin erfüllt jede),
 * sonst Angemeldete → /dashboard mit Hinweis, Gäste → /login mit Rücksprung (a1-rk-prep-ohne-recht-stumm).
 */
function run(auth: Partial<AuthService>) {
  const router = { createUrlTree: jasmine.createSpy('createUrlTree').and.returnValue({} as UrlTree) };
  const snack = jasmine.createSpy('info');
  TestBed.configureTestingModule({
    providers: [
      { provide: AuthService, useValue: auth },
      { provide: Router, useValue: router },
      { provide: SnackbarService, useValue: { info: snack } },
      { provide: TranslateService, useValue: { get: (k: string) => of(k) } },
    ],
  });
  const result = TestBed.runInInjectionContext(() => permissionGuard('users.manage')({} as any, { url: '/prep/7' } as any));
  return { result, router, snack };
}

describe('permissionGuard', () => {
  it('allows a logged-in user that has the permission', () => {
    const { result, snack } = run({ isLoggedIn: true, has: (p: string) => p === 'users.manage' } as any);
    expect(result).toBeTrue();
    expect(snack).not.toHaveBeenCalled();
  });

  it('redirects to /dashboard when the permission is missing — and says why (UX-026)', () => {
    const { result, router, snack } = run({ isLoggedIn: true, has: () => false } as any);
    expect(result).not.toBeTrue();
    expect(router.createUrlTree).toHaveBeenCalledWith(['/dashboard']);
    expect(snack).toHaveBeenCalledOnceWith('app.blocked');
  });

  it('sends guests to the login with a return URL (even if has() would be true), no notice', () => {
    const { result, router, snack } = run({ isLoggedIn: false, has: () => true } as any);
    expect(result).not.toBeTrue();
    expect(router.createUrlTree).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/prep/7', authRequired: '1' } });
    expect(snack).not.toHaveBeenCalled();
  });
});
