import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth.service';

// Minimaler JWT (nur der payload-Teil wird ausgewertet) mit relativem exp.
function jwt(expSecondsFromNow: number): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + expSecondsFromNow }));
  return `header.${payload}.sig`;
}

describe('AuthService token expiry', () => {
  let svc: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    svc = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => localStorage.clear());

  it('reads an expired session token as logged out (re-check on access)', () => {
    svc.login('u', 'p').subscribe();
    http.expectOne('/api/auth/login').flush({ token: jwt(-1), username: 'u', userId: 1, isAdmin: false });

    // Ohne Re-Check waere isLoggedIn hier true (Subject gesetzt) — mit Re-Check false.
    expect(svc.isLoggedIn).toBeFalse();
    expect(svc.token).toBeNull();
  });

  it('reads a valid session token as logged in', () => {
    svc.login('u', 'p').subscribe();
    http.expectOne('/api/auth/login').flush({ token: jwt(3600), username: 'u', userId: 1, isAdmin: true });

    expect(svc.isLoggedIn).toBeTrue();
    expect(svc.isAdmin).toBeTrue();
  });

  it('treats an expired token in storage as logged out at startup', () => {
    localStorage.setItem('rookhub_user',
      JSON.stringify({ token: jwt(-60), username: 'u', userId: 1, isAdmin: false }));
    // Neue Instanz mit vorbelegtem localStorage
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fresh = TestBed.inject(AuthService);
    expect(fresh.isLoggedIn).toBeFalse();
  });
});

describe('AuthService stopImpersonation', () => {
  let svc: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    svc = TestBed.inject(AuthService);
  });

  afterEach(() => localStorage.clear());

  it('stellt die gesicherte Admin-Session wieder her', () => {
    const admin = { token: jwt(3600), username: 'admin', userId: 1, isAdmin: true };
    localStorage.setItem('rookhub_admin_user', JSON.stringify(admin));
    localStorage.setItem('rookhub_user', JSON.stringify({ ...admin, username: 'opfer', impersonating: true }));

    svc.stopImpersonation();

    expect(svc.currentUser?.username).toBe('admin');
    expect(localStorage.getItem('rookhub_admin_user')).toBeNull();
  });

  it('loggt bei beschädigtem Admin-Backup sauber aus statt zu werfen', () => {
    localStorage.setItem('rookhub_admin_user', '{ kaputt');
    localStorage.setItem('rookhub_user', JSON.stringify({ token: jwt(3600), username: 'opfer', userId: 2, isAdmin: false }));

    expect(() => svc.stopImpersonation()).not.toThrow();
    expect(localStorage.getItem('rookhub_admin_user')).toBeNull();
    expect(svc.isLoggedIn).toBeFalse();
  });
});

// JWT mit exp + optionalen perm-Claims (Array oder String) für die RBAC-Tests.
function jwtWithPerms(perm: unknown, expSecondsFromNow = 3600): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + expSecondsFromNow, perm }));
  return `header.${payload}.sig`;
}

describe('AuthService RBAC permissions/has', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  });
  afterEach(() => localStorage.clear());

  // localStorage VOR dem ersten inject() setzen — AuthService seedt seinen Zustand im Konstruktor.
  function svcWith(token: string, isAdmin = false): AuthService {
    localStorage.setItem('rookhub_user',
      JSON.stringify({ token, username: 'u', userId: 1, isAdmin }));
    return TestBed.inject(AuthService);
  }

  it('parses an array of perm claims and answers has()', () => {
    const svc = svcWith(jwtWithPerms(['books.manage', 'ci.view']));
    expect(Array.from(svc.permissions).sort()).toEqual(['books.manage', 'ci.view']);
    expect(svc.has('books.manage')).toBeTrue();
    expect(svc.has('users.manage')).toBeFalse();
  });

  it('parses a single string perm claim', () => {
    const svc = svcWith(jwtWithPerms('groups.manage'));
    expect(svc.has('groups.manage')).toBeTrue();
    expect(svc.has('books.manage')).toBeFalse();
  });

  it('admin satisfies every permission regardless of perm claims', () => {
    const svc = svcWith(jwtWithPerms(undefined), /*isAdmin*/ true);
    expect(svc.has('anything.at.all')).toBeTrue();
  });

  it('no token / no perms → has() is false', () => {
    const svc = TestBed.inject(AuthService);
    expect(svc.has('books.manage')).toBeFalse();
  });
});
