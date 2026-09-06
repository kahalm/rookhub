import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { HandoffService } from './handoff.service';

/**
 * Der Sprung zwischen RookHub und der Turnierseite — und die geteilte Anmeldung, die ohne Sprung
 * auskommt. Beide Wege enden in `adoptSession`; geprueft wird vor allem, dass eine BESTEHENDE
 * Anmeldung nie ueberschrieben wird und ein Fehlschlag still bleibt.
 */
describe('HandoffService', () => {
  let svc: HandoffService;
  let auth: AuthService;
  let http: HttpTestingController;
  const url = location.pathname + location.search + location.hash;

  // Ein echtes (wenn auch ungezeichnetes) JWT: AuthService prueft das Ablaufdatum im payload —
  // mit einem Platzhalter-String gilt die Sitzung sofort als ungueltig.
  const jwt = (secondsFromNow = 3600) =>
    `header.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + secondsFromNow }))}.sig`;
  const session = { token: jwt(), username: 'u', userId: 7, isAdmin: false };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    svc = TestBed.inject(HandoffService);
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    history.replaceState({}, '', url);
    localStorage.clear();
  });

  /** adoptSession zieht die Profil-Einstellungen nach — hier nur abraeumen, nicht Gegenstand. */
  function drainPreferences(): void {
    http.match('/api/profile').forEach(r => r.flush({}));
  }

  it('holt sich beim Start die Anmeldung der Schwesterseite', async () => {
    // Der Nachweis ist ein HttpOnly-Cookie auf der Elterndomaene — hier nicht lesbar, nur der
    // Server kann sagen, ob es taugt.
    const done = svc.consumeIncoming();
    http.expectOne({ method: 'POST', url: '/api/auth/session' }).flush(session);

    expect(await done).toBeTrue();
    expect(auth.currentUser?.userId).toBe(7);
    drainPreferences();
    http.verify();
  });

  it('bleibt still, wenn es keine geteilte Anmeldung gibt', async () => {
    const done = svc.consumeIncoming();
    http.expectOne('/api/auth/session')
      .flush('keine', { status: 401, statusText: 'Unauthorized' });

    expect(await done).toBeFalse();
    expect(auth.currentUser).toBeNull();
    http.verify();
  });

  it('fragt gar nicht erst, wenn hier schon jemand angemeldet ist', async () => {
    auth.adoptSession(session);

    expect(await svc.consumeIncoming()).toBeFalse();
    http.verify();
  });

  it('löst einen mitgebrachten Übergabe-Code ein und räumt ihn aus der Adresse', async () => {
    history.replaceState({}, '', `${location.pathname}?h=EINMAL`);

    const done = svc.consumeIncoming();
    const req = http.expectOne('/api/auth/handoff/exchange');
    expect(req.request.body).toEqual({ code: 'EINMAL' });
    req.flush(session);

    expect(await done).toBeTrue();
    // Verbraucht — er hat im Verlauf nichts verloren.
    expect(location.search).not.toContain('h=');
    drainPreferences();
    http.verify();
  });

  it('fällt bei einem abgelaufenen Code NICHT auf die geteilte Anmeldung zurück', async () => {
    // Der Code war die Aussage „nimm DIESE Anmeldung mit". Schlägt sie fehl, gehört die
    // Anmeldemaske hin — sonst landete man still in einem anderen Konto als gedacht.
    history.replaceState({}, '', `${location.pathname}?h=ALT`);

    const done = svc.consumeIncoming();
    http.expectOne('/api/auth/handoff/exchange')
      .flush('abgelaufen', { status: 400, statusText: 'Bad Request' });

    expect(await done).toBeFalse();
    http.verify();
  });

  it('überschreibt eine bestehende Anmeldung nicht mit der geteilten', async () => {
    auth.adoptSession(session);

    expect(await svc.adoptSharedSession()).toBeFalse();
    http.verify();
  });
});
