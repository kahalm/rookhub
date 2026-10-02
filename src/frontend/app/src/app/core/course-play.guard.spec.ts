import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { of, throwError } from 'rxjs';
import { coursePlayGuard } from './course-play.guard';
import { AuthService } from './auth.service';
import { MenuService } from './menu.service';
import { SnackbarService } from './snackbar.service';
import { TranslateService } from '@ngx-translate/core';

function run(loggedIn: boolean, menuCheck?: any) {
  const auth = { isLoggedIn: loggedIn } as Partial<AuthService>;
  const menu = { check: jasmine.createSpy('check').and.returnValue(menuCheck) } as Partial<MenuService>;
  const router = { createUrlTree: jasmine.createSpy('createUrlTree').and.returnValue({} as UrlTree) };
  const snack = jasmine.createSpy('info');
  TestBed.configureTestingModule({
    providers: [
      { provide: AuthService, useValue: auth },
      { provide: MenuService, useValue: menu },
      { provide: Router, useValue: router },
      { provide: SnackbarService, useValue: { info: snack } },
      { provide: TranslateService, useValue: { get: (k: string) => of(k) } },
    ],
  });
  const res = TestBed.runInInjectionContext(() => coursePlayGuard({} as any, {} as any));
  return { res, menu, router, snack };
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

  it('logged-in + menu hides "courses" → tells the user why instead of a silent redirect (UX-026)', (done) => {
    const { res, snack } = run(true, of(false));
    (res as any).subscribe(() => {
      expect(snack).toHaveBeenCalledOnceWith('app.blocked');
      done();
    });
  });

  it('logged-in + menu allows "courses" → no notice', (done) => {
    const { res, snack } = run(true, of(true));
    (res as any).subscribe(() => {
      expect(snack).not.toHaveBeenCalled();
      done();
    });
  });

  it('fails open (true) on a menu-check error', (done) => {
    const { res } = run(true, throwError(() => new Error('api down')));
    (res as any).subscribe((v: unknown) => { expect(v).toBeTrue(); done(); });
  });
});
