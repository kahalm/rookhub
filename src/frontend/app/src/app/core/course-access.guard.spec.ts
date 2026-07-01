import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree } from '@angular/router';
import { courseAccessGuard } from './course-access.guard';
import { AuthService } from './auth.service';

/**
 * Die Kurse-Seite ist für jeden eingeloggten Nutzer erreichbar (dort kann jeder ein eigenes
 * PGN als Kurs hochladen); nur nicht eingeloggte Nutzer werden auf /login umgeleitet.
 */
describe('courseAccessGuard', () => {
  function setup(auth: Partial<AuthService>) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
      ],
    });
  }

  function run(): any {
    return TestBed.runInInjectionContext(() => courseAccessGuard({} as any, {} as any));
  }

  it('redirects to /login when not logged in', () => {
    setup({ isLoggedIn: false });
    const result = run() as UrlTree;
    expect(result instanceof UrlTree).toBeTrue();
    expect(result.toString()).toBe('/login');
  });

  it('allows any logged-in user (even without an existing course)', () => {
    setup({ isLoggedIn: true, isAdmin: false });
    expect(run()).toBeTrue();
  });

  it('allows admins', () => {
    setup({ isLoggedIn: true, isAdmin: true });
    expect(run()).toBeTrue();
  });
});
