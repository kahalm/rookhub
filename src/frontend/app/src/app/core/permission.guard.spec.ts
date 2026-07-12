import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { permissionGuard } from './permission.guard';
import { AuthService } from './auth.service';

/**
 * RBAC-Guard: durchlassen nur bei eingeloggt UND passender Permission (Admin erfüllt jede),
 * sonst Redirect (UrlTree) auf /dashboard.
 */
function run(auth: Partial<AuthService>) {
  const router = { createUrlTree: jasmine.createSpy('createUrlTree').and.returnValue({} as UrlTree) };
  TestBed.configureTestingModule({
    providers: [
      { provide: AuthService, useValue: auth },
      { provide: Router, useValue: router },
    ],
  });
  const result = TestBed.runInInjectionContext(() => permissionGuard('users.manage')({} as any, {} as any));
  return { result, router };
}

describe('permissionGuard', () => {
  it('allows a logged-in user that has the permission', () => {
    const { result } = run({ isLoggedIn: true, has: (p: string) => p === 'users.manage' } as any);
    expect(result).toBeTrue();
  });

  it('redirects to /dashboard when the permission is missing', () => {
    const { result, router } = run({ isLoggedIn: true, has: () => false } as any);
    expect(result).not.toBeTrue();
    expect(router.createUrlTree).toHaveBeenCalledWith(['/dashboard']);
  });

  it('redirects when not logged in (even if has() would be true)', () => {
    const { result, router } = run({ isLoggedIn: false, has: () => true } as any);
    expect(result).not.toBeTrue();
    expect(router.createUrlTree).toHaveBeenCalledWith(['/dashboard']);
  });
});
