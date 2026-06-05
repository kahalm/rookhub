import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';

describe('authGuard', () => {
  function configure(loggedIn: boolean) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
      ],
    });
  }

  const run = (url: string) =>
    TestBed.runInInjectionContext(() => authGuard({} as any, { url } as any));

  it('lets logged-in users through', () => {
    configure(true);
    expect(run('/weekly')).toBe(true);
  });

  it('redirects logged-out users to /login with returnUrl + authRequired flag', () => {
    configure(false);
    const res = run('/weekly/5') as UrlTree;
    expect(res instanceof UrlTree).toBe(true);
    expect(res.toString()).toContain('/login');
    expect(res.queryParams['returnUrl']).toBe('/weekly/5');
    expect(res.queryParams['authRequired']).toBe('1');
  });
});
