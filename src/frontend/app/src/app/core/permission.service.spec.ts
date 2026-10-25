import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PermissionService } from './permission.service';
import { SessionStore } from './session-store';

function token(perm: unknown): string {
  return `h.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600, perm }))}.s`;
}

describe('PermissionService (Zerlegung Z34)', () => {
  let svc: PermissionService;
  let session: SessionStore;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    svc = TestBed.inject(PermissionService);
    session = TestBed.inject(SessionStore);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); localStorage.clear(); });

  it('Claims des Tokens: Liste, einzelner String, keiner', () => {
    session.set({ token: token(['a', 'b']), username: 'u', userId: 1, isAdmin: false });
    expect(Array.from(svc.permissions).sort()).toEqual(['a', 'b']);
    session.set({ token: token('a'), username: 'u', userId: 1, isAdmin: false });
    expect(svc.has('a')).toBeTrue();
    session.set({ token: token(undefined), username: 'u', userId: 1, isAdmin: false });
    expect(svc.permissions.size).toBe(0);
    session.set(null);
    expect(svc.has('a')).toBeFalse();
  });

  it('Admin am Token erfüllt jedes Recht; der Live-Stand gewinnt danach', async () => {
    session.set({ token: token([]), username: 'u', userId: 1, isAdmin: true });
    expect(svc.has('x')).toBeTrue();
    expect(svc.isAdminLive).toBeTrue();
    const done = svc.refresh();
    http.expectOne('/api/auth/permissions').flush({ isAdmin: false, permissions: ['y'] });
    await done;
    expect(svc.isAdminLive).toBeFalse();
    expect(svc.has('x')).toBeFalse();
    expect(svc.has('y')).toBeTrue();
  });
});
