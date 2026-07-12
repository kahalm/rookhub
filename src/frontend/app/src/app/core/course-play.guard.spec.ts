import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { of, throwError } from 'rxjs';
import { coursePlayGuard } from './course-play.guard';
import { AuthService } from './auth.service';
import { MenuService } from './menu.service';

function run(loggedIn: boolean, menuCheck?: any) {
  const auth = { isLoggedIn: loggedIn } as Partial<AuthService>;
  const menu = { check: jasmine.createSpy('check').and.returnValue(menuCheck) } as Partial<MenuService>;
  const router = { createUrlTree: jasmine.createSpy('createUrlTree').and.returnValue({} as UrlTree) };
  TestBed.configureTestingModule({
    providers: [
      { provide: AuthService, useValue: auth },
      { provide: MenuService, useValue: menu },
      { provide: Router, useValue: router },
    ],
  });
  const res = TestBed.runInInjectionContext(() => coursePlayGuard({} as any, {} as any));
  return { res, menu, router };
}

describe('coursePlayGuard', () => {
  it('lets anonymous visitors through (public courses are server-gated)', () => {
    const { res, menu } = run(false);
    expect(res).toBeTrue();
    expect(menu.check).not.toHaveBeenCalled();
  });

  it('logged-in + menu allows "courses" → true', (done) => {
    const { res } = run(true, of(true));
    (res as any).subscribe((v: unknown) => { expect(v).toBeTrue(); done(); });
  });

  it('logged-in + menu hides "courses" → redirect to /dashboard', (done) => {
    const { res, router } = run(true, of(false));
    (res as any).subscribe((v: unknown) => {
      expect(v).not.toBeTrue();
      expect(router.createUrlTree).toHaveBeenCalledWith(['/dashboard']);
      done();
    });
  });

  it('fails open (true) on a menu-check error', (done) => {
    const { res } = run(true, throwError(() => new Error('api down')));
    (res as any).subscribe((v: unknown) => { expect(v).toBeTrue(); done(); });
  });
});
