import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { convertToParamMap, provideRouter, Router, UrlTree } from '@angular/router';
import { of } from 'rxjs';
import { adminAreaGuard } from './admin-area.guard';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { TranslateService } from '@ngx-translate/core';

/** F5-005: /admin öffnet sich je Recht; Deep-Links auf Tabs ohne Recht landen auf dem ersten erlaubten Tab. */
describe('adminAreaGuard', () => {
  let snack: jasmine.Spy;

  function configure(loggedIn: boolean, isAdmin: boolean, perms: string[] = []) {
    snack = jasmine.createSpy('info');
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn, isAdmin, isAdminLive: isAdmin, has: (p: string) => isAdmin || perms.includes(p) } },
        { provide: SnackbarService, useValue: { info: snack } },
        { provide: TranslateService, useValue: { get: (k: string) => of(k) } },
      ],
    });
  }

  const run = (tab?: string) => TestBed.runInInjectionContext(() => adminAreaGuard(
    { queryParamMap: convertToParamMap(tab ? { tab } : {}) } as any,
    { url: tab ? `/admin?tab=${tab}` : '/admin' } as any));

  it('lässt Admins durch, auch auf jeden Tab', () => {
    configure(true, true);
    expect(run()).toBe(true);
    expect(run('jobs')).toBe(true);
    expect(snack).not.toHaveBeenCalled();
  });

  it('lässt Rechteinhaber ohne Admin-Flag durch (messages.admin → Glockenlink /admin?tab=messages)', () => {
    configure(true, false, ['messages.admin']);
    expect(run()).toBe(true);
    expect(run('messages')).toBe(true);
    expect(run('uploads')).toBe(true);
    expect(snack).not.toHaveBeenCalled();
  });

  it('lenkt einen Deep-Link auf einen Tab ohne Recht auf den ersten erlaubten Tab um, mit Hinweis', () => {
    configure(true, false, ['ci.view', 'messages.admin']);
    const res = run('users') as UrlTree;
    expect(res instanceof UrlTree).toBeTrue();
    expect(res.toString().split('?')[0]).toBe('/admin');
    expect(res.queryParams).toEqual({ tab: 'messages' });
    expect(snack).toHaveBeenCalledOnceWith('app.blocked');
  });

  it('ein unbekannter Tab-Key geht durch (die Seite zeigt den ersten Tab)', () => {
    configure(true, false, ['ci.view']);
    expect(run('gibtsnicht')).toBe(true);
  });

  it('ohne jedes Admin-Recht: Hinweis und Dashboard (UX-026)', () => {
    configure(true, false, ['league.view']);
    const res = run('messages') as UrlTree;
    expect(res.toString()).toBe('/dashboard');
    expect(snack).toHaveBeenCalledOnceWith('app.blocked');
  });

  it('schickt Gäste zur Anmeldung, mit dem Deep-Link als Ziel (UX-024)', () => {
    configure(false, false);
    const res = run('messages') as UrlTree;
    expect(res.toString().split('?')[0]).toBe('/login');
    expect(res.queryParams).toEqual({ returnUrl: '/admin?tab=messages', authRequired: '1' });
    expect(snack).not.toHaveBeenCalled();
  });
});

@Component({ selector: 'app-blank', standalone: true, template: '' }) class BlankComponent {}

/** Glockenlinks (Server: `/admin?tab=messages&thread=<id>`, `/admin?tab=uploads`) über den echten Router. */
describe('adminAreaGuard – Glockenlinks', () => {
  function setup(perms: string[]) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'admin', component: BlankComponent, canActivate: [adminAreaGuard] },
          { path: 'dashboard', component: BlankComponent },
        ]),
        { provide: AuthService, useValue: { isLoggedIn: true, isAdmin: false, isAdminLive: false, has: (p: string) => perms.includes(p) } },
        { provide: SnackbarService, useValue: { info: () => {} } },
        { provide: TranslateService, useValue: { get: (k: string) => of(k) } },
      ],
    });
    return TestBed.inject(Router);
  }

  it('messages.admin ohne Admin-Flag kommt über den Nachrichten-Link auf /admin (samt thread)', async () => {
    const router = setup(['messages.admin']);
    expect(await router.navigateByUrl('/admin?tab=messages&thread=7')).toBeTrue();
    expect(router.url).toBe('/admin?tab=messages&thread=7');
  });

  it('der Registrierungslink /admin öffnet für users.manage die Seite', async () => {
    const router = setup(['users.manage']);
    await router.navigateByUrl('/admin');
    expect(router.url).toBe('/admin');
  });

  it('ein Link auf einen fremden Tab landet auf dem ersten erlaubten', async () => {
    const router = setup(['chessable.admin']);
    await router.navigateByUrl('/admin?tab=uploads');
    expect(router.url).toBe('/admin?tab=courseDl');
  });
});
