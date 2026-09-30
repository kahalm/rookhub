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

  // F1-019: die Tab-Rückkehr fragt höchstens so oft wie der 2-Minuten-Takt (hinter einer NAT-IP zählen alle Tabs).
  it('Tab-Rückkehr fragt erst nach dem Mindestabstand von 2 Minuten erneut', fakeAsync(() => {
    let state: DocumentVisibilityState = 'visible';
    spyOnProperty(document, 'visibilityState', 'get').and.callFake(() => state);
    const back = (s: DocumentVisibilityState) => { state = s; document.dispatchEvent(new Event('visibilitychange')); };
    void TestBed.inject(PermissionRefresher).start();
    expect(auth.refreshPermissions).toHaveBeenCalledTimes(1);

    back('hidden');
    tick(60_000);
    back('visible');                                                           // nach 1 min zurück: noch nicht
    expect(auth.refreshPermissions).toHaveBeenCalledTimes(1);

    back('hidden');
    tick(2 * 60_000);                                                          // verdeckt: der Takt fragt nicht
    expect(auth.refreshPermissions).toHaveBeenCalledTimes(1);
    back('visible');                                                           // nach 3 min zurück: jetzt
    expect(auth.refreshPermissions).toHaveBeenCalledTimes(2);
    expect(PermissionRefresher.MinGapMs).toBeGreaterThanOrEqual(2 * 60_000);
    TestBed.resetTestingModule();
  }));

  it('abgemeldet fragt es nicht', fakeAsync(() => {
    auth.isLoggedIn = false;
    void TestBed.inject(PermissionRefresher).start();
    tick(PermissionRefresher.IntervalMs);
    expect(auth.refreshPermissions).not.toHaveBeenCalled();
    TestBed.resetTestingModule();
  }));
});
