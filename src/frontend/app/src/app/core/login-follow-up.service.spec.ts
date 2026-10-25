import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { LoginFollowUpService } from './login-follow-up.service';
import { ANON_PUZZLE_SESSION_KEY, getOrCreateAnonSessionId } from './anon-session';

describe('LoginFollowUpService (Zerlegung Z34)', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
  });
  afterEach(() => localStorage.clear());

  it('ohne anonyme Kennung übernimmt der Nachlauf keine Sitzung', async () => {
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(LoginFollowUpService).afterLogin();
    await new Promise<void>(r => setTimeout(r, 50));
    expect(http.match('/api/puzzles/claim-session').length).toBe(0);
  });

  it('mit anonymer Kennung holt der Nachlauf die Puzzle-Sitzung ins Konto (dynamischer Import)', async () => {
    const anon = getOrCreateAnonSessionId(ANON_PUZZLE_SESSION_KEY);
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(LoginFollowUpService).afterLogin();
    let claim = http.match('/api/puzzles/claim-session');
    for (let i = 0; i < 50 && claim.length === 0; i++) {
      await new Promise<void>(r => setTimeout(r, 10));
      claim = http.match('/api/puzzles/claim-session');
    }
    expect(claim.length).toBe(1);
    expect(claim[0].request.body).toEqual({ sessionId: anon });
  });

  it('nach dem Abbau des Injektors läuft kein Nachlauf mehr (kein NG0205)', async () => {
    getOrCreateAnonSessionId(ANON_PUZZLE_SESSION_KEY);
    const svc = TestBed.inject(LoginFollowUpService);
    svc.afterLogin();
    expect(() => TestBed.resetTestingModule()).not.toThrow();
    await new Promise<void>(r => setTimeout(r, 50));   // die then()-Zweige laufen jetzt ins Leere, ohne zu werfen
  });
});
