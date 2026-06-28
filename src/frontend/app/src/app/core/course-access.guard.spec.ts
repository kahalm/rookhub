import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { courseAccessGuard } from './course-access.guard';
import { AuthService } from './auth.service';
import { CourseService } from '../features/courses/course.service';

/**
 * Testet das Sichtbarkeits-/Zugriffs-Gating der Kurse:
 * eingeloggt UND (Admin ODER mind. ein freigegebener Kurs).
 */
describe('courseAccessGuard', () => {
  function setup(auth: Partial<AuthService>, checkAccess?: () => Observable<{ hasAccess: boolean }>) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: CourseService, useValue: { checkAccess: checkAccess ?? (() => of({ hasAccess: false })) } },
      ],
    });
  }

  function run(): any {
    return TestBed.runInInjectionContext(() => courseAccessGuard({} as any, {} as any));
  }

  it('redirects to /login when not logged in', () => {
    setup({ isLoggedIn: false, isAdmin: false });
    const result = run() as UrlTree;
    expect(result instanceof UrlTree).toBeTrue();
    expect(result.toString()).toBe('/login');
  });

  it('allows admins immediately (no server call)', () => {
    let called = false;
    setup({ isLoggedIn: true, isAdmin: true }, () => { called = true; return of({ hasAccess: true }); });
    expect(run()).toBeTrue();
    expect(called).toBeFalse();
  });

  it('allows non-admin with course access', (done) => {
    setup({ isLoggedIn: true, isAdmin: false }, () => of({ hasAccess: true }));
    (run() as Observable<boolean | UrlTree>).subscribe(r => {
      expect(r).toBeTrue();
      done();
    });
  });

  it('redirects non-admin without access to /dashboard', (done) => {
    setup({ isLoggedIn: true, isAdmin: false }, () => of({ hasAccess: false }));
    (run() as Observable<boolean | UrlTree>).subscribe(r => {
      expect(r instanceof UrlTree).toBeTrue();
      expect((r as UrlTree).toString()).toBe('/dashboard');
      done();
    });
  });

  it('allows through on access-check error (fail-open, no lockout on network glitches)', (done) => {
    setup({ isLoggedIn: true, isAdmin: false }, () => throwError(() => new Error('boom')));
    (run() as Observable<boolean | UrlTree>).subscribe(r => {
      expect(r).toBeTrue();
      done();
    });
  });

  it('allows non-admin through offline without a server call (offline courses reachable)', () => {
    const spy = spyOnProperty(navigator, 'onLine', 'get').and.returnValue(false);
    let called = false;
    setup({ isLoggedIn: true, isAdmin: false }, () => { called = true; return of({ hasAccess: false }); });
    try {
      expect(run()).toBeTrue();
      expect(called).toBeFalse();
    } finally {
      spy.and.callThrough();
    }
  });
});
