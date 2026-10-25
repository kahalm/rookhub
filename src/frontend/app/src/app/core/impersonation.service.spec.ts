import { TestBed } from '@angular/core/testing';
import { ImpersonationService } from './impersonation.service';
import { SessionStore } from './session-store';

function jwt(expSecondsFromNow: number): string {
  return `h.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + expSecondsFromNow }))}.s`;
}

describe('ImpersonationService (Zerlegung Z34)', () => {
  const admin = { token: jwt(7200), username: 'admin', userId: 1, isAdmin: true };
  const target = { token: jwt(3600), username: 'opfer', userId: 2, isAdmin: false, impersonatorUsername: 'admin' };
  let svc: ImpersonationService;
  let session: SessionStore;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
    svc = TestBed.inject(ImpersonationService);
    session = TestBed.inject(SessionStore);
  });
  afterEach(() => localStorage.clear());

  it('start sichert den Admin und übernimmt das markierte fremde Token', () => {
    session.set(admin);
    expect(svc.start(target)).toBeTrue();
    expect(session.value?.impersonating).toBeTrue();
    expect(svc.isImpersonating).toBeTrue();
    expect(svc.impersonatorUsername).toBe('admin');
    expect(JSON.parse(localStorage.getItem('rookhub_admin_user')!).username).toBe('admin');
  });

  it('stop: mit Sicherung zurück zum Admin, ohne Sicherung „logout"', () => {
    session.set(admin);
    svc.start(target);
    expect(svc.stop()).toBe('restored');
    expect(session.value?.username).toBe('admin');
    expect(localStorage.getItem('rookhub_admin_user')).toBeNull();
    expect(svc.isImpersonating).toBeFalse();
    expect(svc.stop()).toBe('logout');
  });

  it('stop mit beschädigter Sicherung: Reste weg, „logout"', () => {
    localStorage.setItem('rookhub_admin_user', '{ kaputt');
    expect(svc.stop()).toBe('logout');
    expect(localStorage.getItem('rookhub_admin_user')).toBeNull();
  });
});
