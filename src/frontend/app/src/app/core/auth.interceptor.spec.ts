import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpHeaders, HttpRequest } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let captured: HttpRequest<unknown> | null;
  const next: HttpHandlerFn = (req) => { captured = req; return of({} as HttpEvent<unknown>); };
  const authStub = { token: 'jwt-123', isLoggedIn: true, logout: () => {} } as Partial<AuthService>;

  beforeEach(() => {
    captured = null;
    TestBed.configureTestingModule({ providers: [{ provide: AuthService, useValue: authStub }] });
  });

  function run(url: string) {
    TestBed.runInInjectionContext(() =>
      authInterceptor(new HttpRequest('GET', url), next).subscribe());
  }

  it('attaches the Bearer token to /api requests', () => {
    run('/api/profile');
    expect(captured!.headers.get('Authorization')).toBe('Bearer jwt-123');
  });

  it('does NOT attach the token to non-/api requests (no leak to assets/third parties)', () => {
    run('/i18n/en.json');
    expect(captured!.headers.has('Authorization')).toBeFalse();
  });

  it('does NOT attach the token to an absolute third-party URL', () => {
    run('https://lichess.org/api/games');
    expect(captured!.headers.has('Authorization')).toBeFalse();
  });

  // ---- Wann ein 401 die Sitzung beendet -------------------------------------------------------
  // Nur wenn der SERVER das Token ablehnt (WWW-Authenticate: Bearer error="invalid_token"). Ein 401
  // aus einem Controller — falsches aktuelles Passwort beim Passwortwechsel — trägt den Header nicht
  // und darf den Nutzer nicht rauswerfen (so geschehen am 2026-09-09).

  function failingWith(status: number, headers?: Record<string, string>): HttpHandlerFn {
    return () => throwError(() => new HttpErrorResponse({ status, headers: new HttpHeaders(headers ?? {}) }));
  }

  function runFailing(handler: HttpHandlerFn, loggedIn = true): jasmine.Spy {
    const logout = jasmine.createSpy('logout');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: { token: 'jwt-123', isLoggedIn: loggedIn, logout } }],
    });
    TestBed.runInInjectionContext(() =>
      authInterceptor(new HttpRequest('GET', '/api/profile'), handler).subscribe({ error: () => {} }));
    return logout;
  }

  it('loggt aus, wenn der Server das Token als ungültig ablehnt', () => {
    const logout = runFailing(failingWith(401, { 'WWW-Authenticate': 'Bearer error="invalid_token", error_description="The token expired"' }));
    expect(logout).toHaveBeenCalled();
  });

  it('loggt NICHT aus bei einem 401 ohne Token-Ablehnung (falsches Passwort im Controller)', () => {
    const logout = runFailing(failingWith(401));
    expect(logout).not.toHaveBeenCalled();
  });

  it('loggt NICHT aus bei einem 401 mit bloßem "Bearer"-Challenge (kein Token mitgeschickt)', () => {
    const logout = runFailing(failingWith(401, { 'WWW-Authenticate': 'Bearer' }));
    expect(logout).not.toHaveBeenCalled();
  });

  it('loggt NICHT aus, wenn gar keine Sitzung besteht', () => {
    const logout = runFailing(failingWith(401, { 'WWW-Authenticate': 'Bearer error="invalid_token"' }), false);
    expect(logout).not.toHaveBeenCalled();
  });

  it('loggt NICHT aus bei anderen Fehlern (403, 500)', () => {
    expect(runFailing(failingWith(403))).not.toHaveBeenCalled();
    expect(runFailing(failingWith(500))).not.toHaveBeenCalled();
  });
});
