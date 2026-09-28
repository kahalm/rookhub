import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { BehaviorSubject } from 'rxjs';
import { AuthResponse, AuthService } from './auth.service';
import { PermissionRefresher } from './permission-refresher.service';

describe('PermissionRefresher', () => {
  let user$: BehaviorSubject<AuthResponse | null>;
  let auth: { currentUser: AuthResponse | null; currentUser$: BehaviorSubject<AuthResponse | null>; isLoggedIn: boolean; refreshPermissions: jasmine.Spy };

  beforeEach(() => {
    const u = { token: 't', username: 'fm', userId: 136, isAdmin: false };
    user$ = new BehaviorSubject<AuthResponse | null>(u);
    auth = { currentUser: u, currentUser$: user$, isLoggedIn: true, refreshPermissions: jasmine.createSpy('refresh').and.resolveTo() };
    TestBed.configureTestingModule({ providers: [{ provide: AuthService, useValue: auth }] });
  });

  it('holt beim Start, bei einer anderen Anmeldung und regelmäßig — nicht bei derselben', fakeAsync(() => {
    const r = TestBed.inject(PermissionRefresher);
    void r.start();
    expect(auth.refreshPermissions).toHaveBeenCalledTimes(1);
    user$.next({ token: 't2', username: 'fm', userId: 136, isAdmin: false });   // derselbe Nutzer, frisches Token
    expect(auth.refreshPermissions).toHaveBeenCalledTimes(1);
    user$.next({ token: 'x', username: 'other', userId: 7, isAdmin: false });
    expect(auth.refreshPermissions).toHaveBeenCalledTimes(2);
    tick(PermissionRefresher.IntervalMs);
    expect(auth.refreshPermissions).toHaveBeenCalledTimes(3);
    TestBed.resetTestingModule();                                              // räumt Timer und Abos ab
  }));

  it('abgemeldet fragt es nicht', fakeAsync(() => {
    auth.isLoggedIn = false;
    void TestBed.inject(PermissionRefresher).start();
    tick(PermissionRefresher.IntervalMs);
    expect(auth.refreshPermissions).not.toHaveBeenCalled();
    TestBed.resetTestingModule();
  }));
});
