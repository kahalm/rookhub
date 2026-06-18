import { HttpEvent, HttpHandlerFn, HttpRequest } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
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
});
