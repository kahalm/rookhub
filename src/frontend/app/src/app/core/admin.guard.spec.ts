import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree } from '@angular/router';
import { adminGuard } from './admin.guard';
import { AuthService } from './auth.service';

describe('adminGuard', () => {
  function configure(loggedIn: boolean, isAdmin: boolean) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn, isAdmin } },
      ],
    });
  }

  const run = () => TestBed.runInInjectionContext(() => adminGuard({} as any, {} as any));

  it('lässt eingeloggte Admins durch', () => {
    configure(true, true);
    expect(run()).toBe(true);
  });

  it('leitet eingeloggte Nicht-Admins auf /dashboard um', () => {
    configure(true, false);
    const res = run() as UrlTree;
    expect(res instanceof UrlTree).toBeTrue();
    expect(res.toString()).toContain('/dashboard');
  });

  it('leitet anonyme Nutzer auf /dashboard um', () => {
    configure(false, false);
    expect((run() as UrlTree) instanceof UrlTree).toBeTrue();
  });
});
