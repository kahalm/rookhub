import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
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

  /** Laesst die Antwort einer Anfrage bis zur naechsten durchlaufen (sie folgt erst nach einem await). */
  const settle = () => new Promise<void>(r => setTimeout(r));
  const noContent = { status: 204, statusText: 'No Content' };

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

  it('bleibt still, wenn es keine geteilte Anmeldung gibt (204 ohne Rumpf)', async () => {
    const done = svc.consumeIncoming();
    http.expectOne('/api/auth/session')
      .flush(null, { status: 204, statusText: 'No Content' });

    expect(await done).toBeFalse();
    expect(auth.currentUser).toBeNull();
    // adoptSession darf mit dem leeren Rumpf gar nicht erst laufen — sonst stuende "null" im Speicher.
    expect(localStorage.getItem('rookhub_user')).toBeNull();
    http.verify();
  });

  it('bleibt auch beim 401 einer aelteren API still', async () => {
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
    // Danach die Frage, ob der Tausch auch das geteilte Cookie angelegt hat (hier: nein).
    await settle();
    http.expectOne('/api/auth/session').flush(null, noContent);

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

  describe('Sprung zur Schwesterseite', () => {
    let go: jasmine.Spy;

    beforeEach(() => {
      spyOnProperty(svc, 'partnerUrl', 'get').and.returnValue('https://turnier.example');
      go = spyOn(svc as unknown as { go: (url: string) => void }, 'go');
    });

    it('holt angemeldet einen Übergabe-Code und hängt ihn an', async () => {
      auth.adoptSession(session);

      const done = svc.jump('dashboard');
      http.expectOne({ method: 'POST', url: '/api/auth/handoff' }).flush({ code: 'C1' });
      await done;

      expect(go).toHaveBeenCalledWith('https://turnier.example/dashboard?h=C1');
      drainPreferences();
      http.verify();
    });

    it('fragt während einer Impersonation gar nicht erst nach einem Code', async () => {
      // Gemeldet im Codereview 2026-09-29 (F1-001): eingelöst wird der Code drüben zu einer
      // GEWÖHNLICHEN 30-Tage-Anmeldung des Zielkontos samt geteiltem Cookie — ohne imp-Claim, also
      // ohne die Impersonations-Sperren. Der Server lehnt seit W1 (A1-001) mit 403 ab; hier wird
      // gar nicht erst gefragt, drüben steht dann die Anmeldemaske.
      auth.adoptSession({ token: jwt(), username: 'admin', userId: 1, isAdmin: true });
      auth.impersonate({ token: jwt(), username: 'x', userId: 9, isAdmin: false });
      expect(auth.isImpersonating).toBeTrue();

      await svc.jump('dashboard');

      http.expectNone('/api/auth/handoff');
      expect(go).toHaveBeenCalledWith('https://turnier.example/dashboard');
      drainPreferences();
      http.verify();
    });

    it('springt auch ohne Admin-Sicherung ohne Code, solange das Token eine Impersonation ist', async () => {
      // isImpersonating verlangt zusätzlich das Admin-Backup; der Server prüft nur das Token.
      auth.adoptSession({ ...session, impersonating: true });

      await svc.jump();

      http.expectNone('/api/auth/handoff');
      expect(go).toHaveBeenCalledWith('https://turnier.example/');
      drainPreferences();
      http.verify();
    });

    it('springt ohne Anmeldung ohne Code', async () => {
      await svc.jump('tournaments/calendar');

      http.expectNone('/api/auth/handoff');
      expect(go).toHaveBeenCalledWith('https://turnier.example/tournaments/calendar');
    });
  });

  describe('Abmelden über die Oberflächen hinweg', () => {
    // Gemeldet im Codereview 2026-09-29 (A1-005): Abmelden in RookHub löschte nur das Cookie. KidHub,
    // Turnierseite und LeagueHub, die es beim Start gegen ein eigenes 30-Tage-Token getauscht hatten,
    // fragten nie wieder nach — die nächsten Kinder am Vereins-PC spielten im Konto der Lehrerin.
    const visible = () => spyOnProperty(document, 'visibilityState', 'get').and.returnValue('visible');

    /** Eine aus der geteilten Anmeldung übernommene Sitzung (so, wie sie beim Start entsteht). */
    function adopted(): void {
      auth.adoptSession({ ...session, adopted: true });
    }

    /** Am Ende: das Profil-Nachladen von adoptSession abräumen, dann darf nichts mehr offen sein. */
    async function finish(): Promise<void> {
      await settle();
      drainPreferences();
      http.verify();
    }

    beforeEach(() => spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true));

    it('merkt sich, dass die Anmeldung aus der geteilten stammt', async () => {
      const done = svc.consumeIncoming();
      http.expectOne('/api/auth/session').flush(session);

      expect(await done).toBeTrue();
      expect(auth.currentUser?.adopted).toBeTrue();
      expect(JSON.parse(localStorage.getItem('rookhub_user')!).adopted).toBeTrue();
      await finish();
    });

    it('meldet eine übernommene Anmeldung beim Start ab, wenn die geteilte weg ist', async () => {
      adopted();

      expect(await svc.consumeIncoming()).toBeFalse();
      http.expectOne({ method: 'POST', url: '/api/auth/session' }).flush(null, noContent);
      await settle();

      expect(auth.currentUser).toBeNull();
      expect(localStorage.getItem('rookhub_user')).toBeNull();
      http.expectOne('/api/auth/session/end').flush(null, noContent);
      await finish();
    });

    it('gleicht beim Zurückkehren in den Tab ab', async () => {
      adopted();
      await svc.consumeIncoming();
      http.expectOne('/api/auth/session').flush(session);           // beim Start: steht noch
      await settle();
      expect(auth.currentUser?.userId).toBe(7);

      (svc as unknown as { lastCheck: number }).lastCheck = 0;       // Mindestabstand abgelaufen
      visible();
      document.dispatchEvent(new Event('visibilitychange'));
      http.expectOne('/api/auth/session').flush(null, noContent);    // inzwischen abgemeldet
      await settle();

      expect(auth.currentUser).toBeNull();
      http.expectOne('/api/auth/session/end').flush(null, noContent);
      await finish();
    });

    it('fragt beim schnellen Hin- und Herschalten nicht jedes Mal', async () => {
      adopted();
      await svc.consumeIncoming();
      http.expectOne('/api/auth/session').flush(session);
      await settle();

      visible();
      document.dispatchEvent(new Event('visibilitychange'));
      http.expectNone('/api/auth/session');
      await finish();
    });

    it('lässt eine selbst angemeldete Sitzung in Ruhe', async () => {
      auth.adoptSession(session);                                    // ohne adopted: Maske/Registrierung
      await svc.consumeIncoming();
      visible();
      document.dispatchEvent(new Event('visibilitychange'));

      http.expectNone('/api/auth/session');
      expect(auth.currentUser?.userId).toBe(7);
      await finish();
    });

    it('bleibt angemeldet, wenn der Abgleich keine Antwort bekommt (offline, 429)', async () => {
      adopted();

      const done = svc.verifyAdoptedSession();
      http.expectOne('/api/auth/session').flush('zu viele', { status: 429, statusText: 'Too Many Requests' });

      expect(await done).toBeFalse();
      expect(auth.currentUser?.userId).toBe(7);
      await finish();
    });

    it('übernimmt das andere Konto, wenn sich dort inzwischen jemand anderes angemeldet hat', async () => {
      // Abmelden wäre hier falsch: das schickte session/end und löschte das Cookie des anderen mit.
      adopted();

      const done = svc.verifyAdoptedSession();
      http.expectOne('/api/auth/session').flush({ ...session, userId: 8, username: 'andere' });

      expect(await done).toBeTrue();
      expect(auth.currentUser?.userId).toBe(8);
      expect(auth.currentUser?.adopted).toBeTrue();
      http.expectNone('/api/auth/session/end');
      await finish();
    });

    it('räumt beim Kontowechsel die lokalen Spuren des vorigen Kontos ab — ohne session/end', async () => {
      // Nachgereicht im Review zu A1-005: der Wechsel übernahm B, ließ aber Offline-Inhalte und
      // Endless-Läufe von A liegen — der Endless-Modus überträgt lokale Läufe ins Konto, B erbte
      // damit Laufhistorie und Highscore von A. Aufgeräumt wird wie beim Abmelden.
      adopted();
      localStorage.setItem('rookhub_courses_cache', '[]');
      localStorage.setItem('rookhub_repertoire_offline_5', '{}');
      localStorage.setItem('rookhub_endless_history', '[]');
      localStorage.setItem('rookhub_calc_local_1', '{}');
      localStorage.setItem('rookhub_admin_user', '{}');
      localStorage.setItem('rookhub_lang', 'de');                    // Geräte-Einstellung, bleibt
      const nav = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);

      const done = svc.verifyAdoptedSession();
      http.expectOne('/api/auth/session').flush({ ...session, userId: 8, username: 'andere' });

      expect(await done).toBeTrue();
      for (const k of ['rookhub_courses_cache', 'rookhub_repertoire_offline_5', 'rookhub_endless_history',
        'rookhub_calc_local_1', 'rookhub_admin_user'])
        expect(localStorage.getItem(k)).withContext(k).toBeNull();
      expect(localStorage.getItem('rookhub_lang')).toBe('de');
      expect(JSON.parse(localStorage.getItem('rookhub_user')!).userId).toBe(8);
      expect(nav).toHaveBeenCalledWith('/');
      http.expectNone('/api/auth/session/end');
      await finish();
    });

    it('gleicht auch beim Wechsel zwischen zwei sichtbaren Fenstern ab (focus)', async () => {
      // Nebeneinander offene Fenster bleiben beide sichtbar — dort feuert kein visibilitychange.
      adopted();
      await svc.consumeIncoming();
      http.expectOne('/api/auth/session').flush(session);
      await settle();

      (svc as unknown as { lastCheck: number }).lastCheck = 0;
      visible();
      window.dispatchEvent(new Event('focus'));
      http.expectOne('/api/auth/session').flush(null, noContent);
      await settle();

      expect(auth.currentUser).toBeNull();
      http.expectOne('/api/auth/session/end').flush(null, noContent);
      await finish();
    });

    it('fragt beim Anklicken des Tabs (visibilitychange UND focus) nur einmal', async () => {
      adopted();
      await svc.consumeIncoming();
      http.expectOne('/api/auth/session').flush(session);
      await settle();

      (svc as unknown as { lastCheck: number }).lastCheck = 0;
      visible();
      document.dispatchEvent(new Event('visibilitychange'));
      window.dispatchEvent(new Event('focus'));
      http.expectOne('/api/auth/session').flush(session);
      await finish();
    });

    it('ein eingelöster Code hängt an der geteilten Anmeldung, wenn der Tausch das Cookie anlegte', async () => {
      history.replaceState({}, '', `${location.pathname}?h=EINMAL`);

      const done = svc.consumeIncoming();
      http.expectOne('/api/auth/handoff/exchange').flush(session);
      await settle();
      http.expectOne('/api/auth/session').flush(session);

      expect(await done).toBeTrue();
      expect(auth.currentUser?.adopted).toBeTrue();
      await finish();
    });

    it('ohne Elterndomäne bleibt ein eingelöster Code eine gewöhnliche Anmeldung', async () => {
      // localhost, Dev über HTTP: dort gibt es nie ein Cookie, der Abgleich antwortete immer 204 —
      // und meldete sonst jeden ab, der per Sprung gekommen ist.
      history.replaceState({}, '', `${location.pathname}?h=EINMAL`);

      const done = svc.consumeIncoming();
      http.expectOne('/api/auth/handoff/exchange').flush(session);
      await settle();
      http.expectOne('/api/auth/session').flush(null, noContent);

      expect(await done).toBeTrue();
      expect(auth.currentUser?.adopted).toBeFalsy();
      expect(await svc.verifyAdoptedSession()).toBeFalse();
      http.expectNone('/api/auth/session');
      await finish();
    });
  });
});
