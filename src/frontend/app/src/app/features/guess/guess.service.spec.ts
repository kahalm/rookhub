import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { GuessService } from './guess.service';
import { AuthService } from '../../core/auth.service';

/**
 * Der anonyme Zweig ist keine Bequemlichkeit, sondern trägt die eiserne Regel: der Fortschritt
 * liegt am SERVER, also muss jeder Aufruf ohne Anmeldung seine Sitzungskennung mitnehmen und auf
 * `…/anonymous` zeigen. Geht das schief, fällt es nicht auf — es fällt nur die Wertung aus.
 */
describe('GuessService', () => {
  let service: GuessService;
  let http: HttpTestingController;
  let loggedIn: boolean;

  beforeEach(() => {
    loggedIn = true;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { get isLoggedIn() { return loggedIn; } } },
      ],
    });
    service = TestBed.inject(GuessService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('angemeldet', () => {
    it('nutzt die normalen Routen ohne Sitzungskennung', () => {
      service.list().subscribe();
      const list = http.expectOne('/api/guess-sessions');
      expect(list.request.params.has('sessionId')).toBeFalse();
      list.flush([]);

      service.get(7).subscribe();
      const get = http.expectOne('/api/guess-sessions/7');
      expect(get.request.params.has('sessionId')).toBeFalse();
      get.flush({});

      service.delete(7).subscribe();
      const del = http.expectOne('/api/guess-sessions/7');
      expect(del.request.method).toBe('DELETE');
      del.flush(null);
    });

    it('schickt guessWhite nur, wenn eine Seite gewählt wurde', () => {
      service.start(3, false).subscribe();
      const withSide = http.expectOne('/api/guess-sessions');
      expect(withSide.request.body).toEqual({ gameAnalysisId: 3, guessWhite: false });
      withSide.flush({});

      // Ohne Seite: das Feld darf NICHT mitgehen, sonst kann der Server nicht „Gewinner" ableiten.
      service.start(3).subscribe();
      const noSide = http.expectOne('/api/guess-sessions');
      expect(noSide.request.body).toEqual({ gameAnalysisId: 3 });
      expect('guessWhite' in (noSide.request.body as object)).toBeFalse();
      noSide.flush({});
    });
  });

  describe('ohne Anmeldung', () => {
    beforeEach(() => { loggedIn = false; });

    it('nimmt die anonyme Route und die Kennung als Parameter', () => {
      service.list().subscribe();
      const req = http.expectOne(r => r.url === '/api/guess-sessions/anonymous');
      expect(req.request.params.get('sessionId')).toMatch(/^[a-fA-F0-9-]{32,36}$/);
      req.flush([]);
    });

    it('legt die Kennung bei Schreib-Aufrufen in den Rumpf', () => {
      service.guess(9, 'e2e4', 12).subscribe();
      const req = http.expectOne('/api/guess-sessions/anonymous/9/guess');
      const body = req.request.body as { uci: string; addSeconds: number; sessionId: string };
      expect(body.uci).toBe('e2e4');
      expect(body.addSeconds).toBe(12);
      expect(body.sessionId).toMatch(/^[a-fA-F0-9-]{32,36}$/);
      req.flush({});
    });

    it('benutzt für alle Aufrufe DIESELBE Kennung', () => {
      service.list().subscribe();
      const first = http.expectOne(r => r.url === '/api/guess-sessions/anonymous');
      const id = first.request.params.get('sessionId');
      first.flush([]);

      service.review(4).subscribe();
      const second = http.expectOne(r => r.url === '/api/guess-sessions/anonymous/4/review');
      expect(second.request.params.get('sessionId')).toBe(id);
      second.flush([]);
    });
  });

  describe('Übernahme nach dem Anmelden (N11-003)', () => {
    const anon = '11111111-2222-3333-4444-555555555555';
    afterEach(() => localStorage.removeItem(GuessService.AnonKey));

    it('schickt die Kennung dieses Browsers und räumt sie nach der Übernahme ab', () => {
      // Fund-Weg: anonym bis Halbzug 40 gespielt, dann registriert — ohne Übernahme verschwand der Durchlauf.
      localStorage.setItem(GuessService.AnonKey, anon);
      let res: { claimed: number } | null | undefined;

      service.claimAnonymous().subscribe(r => res = r);
      const req = http.expectOne('/api/guess-sessions/claim-session');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ sessionId: anon });
      req.flush({ claimed: 1 });

      expect(res).toEqual({ claimed: 1 });
      expect(localStorage.getItem(GuessService.AnonKey)).toBeNull();
    });

    it('fragt ohne Kennung gar nicht erst — und legt auch keine an', () => {
      let res: { claimed: number } | null | undefined;
      service.claimAnonymous().subscribe(r => res = r);

      http.expectNone('/api/guess-sessions/claim-session');
      expect(res).toBeNull();
      expect(localStorage.getItem(GuessService.AnonKey)).toBeNull();
    });

    it('behält die Kennung, wenn die Übernahme scheitert (nächstes Anmelden versucht es erneut)', () => {
      localStorage.setItem(GuessService.AnonKey, anon);
      let res: { claimed: number } | null | undefined;

      service.claimAnonymous().subscribe(r => res = r);
      http.expectOne('/api/guess-sessions/claim-session').flush('weg', { status: 503, statusText: 'Unavailable' });

      expect(res).toBeNull();
      expect(localStorage.getItem(GuessService.AnonKey)).toBe(anon);
    });
  });
});
