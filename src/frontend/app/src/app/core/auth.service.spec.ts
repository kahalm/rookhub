import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { OfflineService } from './offline.service';
import { ANON_PUZZLE_SESSION_KEY, getOrCreateAnonSessionId } from './anon-session';

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

  it('tauscht bei der Passwortänderung das gespeicherte Token gegen das frische', () => {
    // Der Server rotiert dabei den Security-Stamp und entwertet damit auch das Token DIESER Sitzung.
    // Ohne Austausch flog man eine Minute später kommentarlos raus (der Interceptor loggt bei 401 aus).
    svc.login('u', 'p').subscribe();
    http.expectOne('/api/auth/login').flush({ token: jwt(3600), username: 'u', userId: 1, isAdmin: false });
    const oldToken = JSON.parse(localStorage.getItem('rookhub_user')!).token;

    svc.changePassword('alt', 'neu').subscribe();
    const fresh = jwt(7200);
    http.expectOne('/api/auth/change-password').flush({ token: fresh, username: 'u', userId: 1, isAdmin: false });

    const stored = JSON.parse(localStorage.getItem('rookhub_user')!);
    expect(stored.token).toBe(fresh);
    expect(stored.token).not.toBe(oldToken);
    expect(svc.isLoggedIn).toBeTrue();
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

describe('AuthService: andere Tabs desselben Browsers (F1-005)', () => {
  // Gemeldet im Codereview 2026-09-29 (F1-005): jeder Tab hielt seine Sitzung im Speicher, ohne Abgleich
  // über das storage-Ereignis. Stieg der Admin in Tab 1 aus einer Impersonation aus, arbeitete Tab 2 mit
  // dem fremden Token weiter — und weil isImpersonating die (geteilte) Admin-Sicherung verlangte, ohne
  // roten Streifen. Ebenso blieb Tab 2 nach einem Abmelden in Tab 1 angemeldet.
  const admin = { token: jwt(7200), username: 'admin', userId: 1, isAdmin: true };
  const imp = { token: jwt(3600), username: 'x', userId: 9, isAdmin: false, impersonating: true, impersonatorUsername: 'admin' };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  });
  afterEach(() => localStorage.clear());

  /** Was der ANDERE Tab tut: in den Speicher schreiben — das Ereignis feuert nur hier, nicht dort. */
  function otherTab(write: () => void, key: string | null = 'rookhub_user'): void {
    write();
    window.dispatchEvent(new StorageEvent('storage', { key, newValue: key ? localStorage.getItem(key) : null }));
  }

  it('folgt dem Aussteigen aus einer Impersonation in einem anderen Tab', () => {
    localStorage.setItem('rookhub_admin_user', JSON.stringify(admin));
    localStorage.setItem('rookhub_user', JSON.stringify(imp));
    const svc = TestBed.inject(AuthService);
    expect(svc.isImpersonating).toBeTrue();

    otherTab(() => {   // stopImpersonation in Tab 1
      localStorage.setItem('rookhub_user', JSON.stringify(admin));
      localStorage.removeItem('rookhub_admin_user');
    });

    expect(svc.token).toBe(admin.token);
    expect(svc.currentUser?.username).toBe('admin');
    expect(svc.isImpersonating).toBeFalse();
    TestBed.inject(HttpTestingController).match('/api/profile').forEach(r => r.flush({}));
  });

  it('folgt einem Abmelden in einem anderen Tab — ohne erneutes Ende der geteilten Anmeldung', () => {
    localStorage.setItem('rookhub_user', JSON.stringify(admin));
    const svc = TestBed.inject(AuthService);
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    expect(svc.isLoggedIn).toBeTrue();

    otherTab(() => localStorage.removeItem('rookhub_user'));

    expect(svc.isLoggedIn).toBeFalse();
    expect(svc.token).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login']);
    TestBed.inject(HttpTestingController).expectNone('/api/auth/rh-session/end');
  });

  it('lässt fremde Schlüssel und unveränderte Sitzungen in Ruhe', () => {
    localStorage.setItem('rookhub_user', JSON.stringify(admin));
    const svc = TestBed.inject(AuthService);
    const navigate = spyOn(TestBed.inject(Router), 'navigate');

    otherTab(() => localStorage.setItem('rookhub_lang', 'hr'), 'rookhub_lang');
    otherTab(() => localStorage.setItem('rookhub_user', JSON.stringify(admin)));

    expect(svc.token).toBe(admin.token);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('zeigt die Impersonation auch ohne Admin-Sicherung an, und der Ausstieg meldet dann ab', () => {
    // Das Token entscheidet, nicht die geteilte Sicherung: fehlt sie, darf der Streifen nicht verschwinden,
    // während das fremde Token weiter gilt. Ohne Sicherung gibt es keinen Rücksprung — dann abmelden.
    localStorage.setItem('rookhub_user', JSON.stringify(imp));
    const svc = TestBed.inject(AuthService);
    expect(svc.isImpersonating).toBeTrue();

    svc.stopImpersonation();

    expect(svc.isLoggedIn).toBeFalse();
    expect(localStorage.getItem('rookhub_user')).toBeNull();
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

describe('AuthService logout clears offline content', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  });
  afterEach(() => localStorage.clear());

  it('removes downloaded offline content on logout but keeps the (user-stamped) queue + unrelated keys', () => {
    const svc = TestBed.inject(AuthService);
    // Offline-Inhalte eines Nutzers
    localStorage.setItem('rookhub_repertoire_offline_3', JSON.stringify({ meta: { id: 3 } }));
    localStorage.setItem('rookhub_book_offline_x', '[]');
    localStorage.setItem('rookhub_courses_cache', '[]');
    localStorage.setItem('rookhub_daily_offline', '{}');
    // Diese sollen ERHALTEN bleiben:
    localStorage.setItem('rookhub_offline_queue', '[{"id":"1","method":"POST","url":"/api/x","body":{},"ts":0,"userId":7}]');
    localStorage.setItem('rookhub_offline_settings', '{"puzzleCount":30}');
    localStorage.setItem('rookhub_lang', 'de');

    svc.logout();

    expect(localStorage.getItem('rookhub_repertoire_offline_3')).toBeNull();
    expect(localStorage.getItem('rookhub_book_offline_x')).toBeNull();
    expect(localStorage.getItem('rookhub_courses_cache')).toBeNull();
    expect(localStorage.getItem('rookhub_daily_offline')).toBeNull();
    // Queue (user-gestempelt → sicher) + Einstellungen + Sprache bleiben
    expect(localStorage.getItem('rookhub_offline_queue')).not.toBeNull();
    expect(localStorage.getItem('rookhub_offline_settings')).not.toBeNull();
    expect(localStorage.getItem('rookhub_lang')).toBe('de');
  });

  it('beendet auch die geteilte Anmeldung der Schwesterseite', () => {
    // Sie liegt als Cookie auf der Elterndomaene — der einzige Teil dieser Sitzung, den
    // localStorage.removeItem nicht erreicht. Bliebe sie stehen, holte sich die Seite beim
    // nächsten Aufruf genau die Anmeldung zurück, die man gerade beendet hat.
    const svc = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);

    svc.logout();

    http.expectOne({ method: 'POST', url: '/api/auth/rh-session/end' }).flush(null, { status: 204, statusText: 'No Content' });
    http.verify();
  });

  it('räumt beim Wechsel auf die geteilte Anmeldung eines anderen Kontos auf wie logout — ohne session/end', () => {
    // Nutzerwechsel am Gerät (HandoffService.verifyAdoptedSession): das Cookie gehört jetzt dem neuen
    // Konto — session/end löschte es gleich mit. Die lokalen Spuren des vorigen müssen trotzdem weg.
    const svc = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);
    localStorage.setItem('rookhub_courses_cache', '[]');
    localStorage.setItem('rookhub_endless_highscore', '42');
    localStorage.setItem('rookhub_admin_user', '{}');
    localStorage.setItem('rookhub_lang', 'de');

    svc.switchToSharedSession({ token: jwt(3600), username: 'b', userId: 8, isAdmin: false });

    expect(localStorage.getItem('rookhub_courses_cache')).toBeNull();
    expect(localStorage.getItem('rookhub_endless_highscore')).toBeNull();
    expect(localStorage.getItem('rookhub_admin_user')).toBeNull();
    expect(localStorage.getItem('rookhub_lang')).toBe('de');
    expect(svc.currentUser?.userId).toBe(8);
    expect(svc.currentUser?.adopted).toBeTrue();
    http.expectNone('/api/auth/rh-session/end');
  });

  it('meldet trotzdem ab, wenn der Server dabei nicht mitspielt', () => {
    const svc = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);

    svc.logout();
    http.expectOne('/api/auth/rh-session/end').flush('weg', { status: 500, statusText: 'Server Error' });

    expect(localStorage.getItem('rookhub_user')).toBeNull();
    http.verify();
  });

  it('merkt sich ein Abmelden ohne Netz und holt das Ende der geteilten Anmeldung nach, sobald das Netz zurück ist (F1-004)', async () => {
    // Gemeldet im Codereview 2026-09-29 (F1-004): das Ende war ein einmaliger Versuch, Fehler wurden
    // verschluckt. Im Flugmodus blieb das 30-Tage-Cookie gültig — der nächste Start ohne Sitzung holte
    // sich genau die Anmeldung zurück, die eben beendet wurde.
    const svc = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);
    const settle = () => new Promise<void>(r => setTimeout(r));

    svc.logout();
    http.expectOne('/api/auth/rh-session/end').error(new ProgressEvent('error'));   // offline: Status 0
    await settle();
    expect(svc.isLoggedIn).toBeFalse();
    expect(svc.sessionEndPending).toBeTrue();

    window.dispatchEvent(new Event('online'));
    http.expectOne({ method: 'POST', url: '/api/auth/rh-session/end' }).flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    expect(svc.sessionEndPending).toBeFalse();
    http.verify();
  });

  it('eine neue Anmeldung räumt ein ausstehendes Abmelden ab — kein nachgeschobenes Ende für das neue Cookie', async () => {
    const svc = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);
    localStorage.setItem(AuthService.SessionEndPendingKey, '1');

    svc.login('u', 'p').subscribe();
    http.expectOne('/api/auth/login').flush({ token: jwt(3600), username: 'u', userId: 1, isAdmin: false });
    http.match('/api/profile').forEach(r => r.flush({}));

    expect(svc.sessionEndPending).toBeFalse();
    window.dispatchEvent(new Event('online'));
    http.expectNone('/api/auth/rh-session/end');
  });
});

describe('AuthService: Sitzungsende ohne Abmelden (Ablauf, Kontowechsel)', () => {
  // Gemeldet im Codereview 2026-09-29 (F1-006): Aufgeräumt wurde nur im ausdrücklichen logout().
  // Lief das Token ab (der Normalfall jeder nicht beendeten Sitzung) oder meldete sich jemand über
  // /login?switch=1 mit einem anderen Konto an, blieben Offline-Inhalte, Endless-Läufe samt Highscore,
  // die anonyme Sitzungs-Id und das Admin-Backup liegen — der Endless-Modus schob die Läufe von A beim
  // ersten Öffnen ins Konto von B, bis in die Bestenliste.
  const traces = ['rookhub_courses_cache', 'rookhub_book_offline_x', 'rookhub_endless_history',
    'rookhub_endless_highscore', 'rookhub_calc_local_1', 'rookhub_puzzle_session', 'rookhub_admin_user'];
  const userA = (token: string) => ({ token, username: 'a', userId: 1, isAdmin: false });

  function seedTraces(): void {
    for (const k of traces) localStorage.setItem(k, '1');
    localStorage.setItem('rookhub_lang', 'de');                      // Geräte-Einstellung, bleibt
  }
  function expectTraces(present: boolean): void {
    for (const k of traces)
      expect(localStorage.getItem(k) !== null).withContext(k).toBe(present);
    expect(localStorage.getItem('rookhub_lang')).toBe('de');
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  });
  afterEach(() => localStorage.clear());

  it('räumt beim Start mit abgelaufenem Token auf wie beim Abmelden — ohne session/end', () => {
    // session/end bewusst nicht: das Cookie kann längst einer anderen, aktiven Anmeldung gehören.
    localStorage.setItem('rookhub_user', JSON.stringify(userA(jwt(-60))));
    seedTraces();

    const svc = TestBed.inject(AuthService);

    expect(svc.isLoggedIn).toBeFalse();
    expect(localStorage.getItem('rookhub_user')).toBeNull();
    expectTraces(false);
    TestBed.inject(HttpTestingController).expectNone('/api/auth/rh-session/end');
  });

  it('räumt auf, wenn das Token mitten in der Sitzung abläuft', () => {
    jasmine.clock().install();
    try {
      jasmine.clock().mockDate(new Date(2026, 8, 30, 12, 0, 0));
      localStorage.setItem('rookhub_user', JSON.stringify(userA(jwt(60))));
      const svc = TestBed.inject(AuthService);
      expect(svc.isLoggedIn).toBeTrue();
      seedTraces();

      jasmine.clock().tick(120_000);                                // Token jetzt abgelaufen

      expect(svc.isLoggedIn).toBeFalse();
      expect(localStorage.getItem('rookhub_user')).toBeNull();
      expectTraces(false);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('lässt beim Ablauf die Anmeldung stehen, die ein anderer Tab inzwischen gespeichert hat', () => {
    // Dieser Tab hält A noch im Speicher, ein anderer hat B angemeldet: dessen Sitzung und
    // Offline-Inhalte gehören nicht diesem Tab.
    jasmine.clock().install();
    try {
      jasmine.clock().mockDate(new Date(2026, 8, 30, 12, 0, 0));
      localStorage.setItem('rookhub_user', JSON.stringify(userA(jwt(60))));
      const svc = TestBed.inject(AuthService);
      expect(svc.isLoggedIn).toBeTrue();
      const b = JSON.stringify({ token: jwt(3600), username: 'b', userId: 8, isAdmin: false });
      localStorage.setItem('rookhub_user', b);
      seedTraces();

      jasmine.clock().tick(120_000);

      expect(svc.isLoggedIn).toBeFalse();
      expect(localStorage.getItem('rookhub_user')).toBe(b);
      expectTraces(true);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('räumt beim Kontowechsel über die Anmeldemaske (?switch=1) die Spuren des vorigen Kontos ab', () => {
    localStorage.setItem('rookhub_user', JSON.stringify(userA(jwt(3600))));
    const svc = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);
    seedTraces();

    svc.login('b', 'p').subscribe();
    http.expectOne('/api/auth/login').flush({ token: jwt(3600), username: 'b', userId: 8, isAdmin: false });

    expectTraces(false);
    expect(JSON.parse(localStorage.getItem('rookhub_user')!).userId).toBe(8);
    expect(svc.currentUser?.userId).toBe(8);
    // Das Cookie hat der Login eben für B geschrieben — session/end löschte es gleich wieder.
    http.expectNone('/api/auth/rh-session/end');
  });

  it('behält beim erneuten Anmelden mit demselben Konto die Offline-Inhalte', () => {
    // Genau dafür gibt es ?switch=1: Konto bestätigen, ohne die Downloads zu verlieren.
    localStorage.setItem('rookhub_user', JSON.stringify(userA(jwt(3600))));
    const svc = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);
    seedTraces();

    svc.login('a', 'p').subscribe();
    http.expectOne('/api/auth/login').flush(userA(jwt(7200)));

    expectTraces(true);
    expect(svc.currentUser?.userId).toBe(1);
  });

  it('räumt bei einer Anmeldung ohne vorige Sitzung nichts ab (anonyme Läufe wandern ins neue Konto)', () => {
    const svc = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);
    localStorage.setItem('rookhub_endless_history', '[]');
    localStorage.setItem('rookhub_puzzle_session', 'anon-1');

    svc.register('neu', null, 'pw').subscribe();
    http.expectOne('/api/auth/register').flush({ token: jwt(3600), username: 'neu', userId: 9, isAdmin: false });

    expect(localStorage.getItem('rookhub_endless_history')).toBe('[]');
    expect(localStorage.getItem('rookhub_puzzle_session')).toBe('anon-1');
  });

  it('übernimmt nach der Registrierung die anonymen Punktepartie-Durchläufe dieses Browsers (N11-003)', async () => {
    // Eigene Kennung, auch ohne Puzzle-Sitzung: ein Besucher kann nur geraten haben.
    const svc = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);
    const anon = '11111111-2222-3333-4444-555555555555';
    localStorage.setItem('rookhub_guess_session', anon);

    svc.register('neu', null, 'pw').subscribe();
    http.expectOne('/api/auth/register').flush({ token: jwt(3600), username: 'neu', userId: 9, isAdmin: false });

    // Der Dienst kommt per dynamischem Import (Zirkelbezug) — die Anfrage folgt erst danach.
    let claim = http.match('/api/guess-sessions/claim-session');
    for (let i = 0; i < 50 && claim.length === 0; i++) {
      await new Promise<void>(r => setTimeout(r, 10));
      claim = http.match('/api/guess-sessions/claim-session');
    }
    expect(claim.length).toBe(1);
    expect(claim[0].request.body).toEqual({ sessionId: anon });
    claim[0].flush({ claimed: 1 });
    expect(localStorage.getItem('rookhub_guess_session')).toBeNull();
  });
});

describe('AuthService: voller Browser-Speicher', () => {
  let svc: AuthService;
  let http: HttpTestingController;
  let offline: OfflineService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    svc = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
    offline = TestBed.inject(OfflineService);
  });

  afterEach(() => localStorage.clear());

  function quotaError(): Error {
    const e = new Error('QuotaExceededError');
    e.name = 'QuotaExceededError';
    return e;
  }

  function loginOk(): void {
    svc.login('u', 'p').subscribe();
    http.expectOne('/api/auth/login').flush({ token: jwt(3600), username: 'u', userId: 1, isAdmin: false });
  }

  it('räumt bei vollem Speicher die Offline-Caches und speichert die Anmeldung danach', () => {
    // Vorher blieb die Anmeldung im Tab hängen: nach jedem Neuladen stand die Maske, ohne Hinweis.
    const original = Storage.prototype.setItem;
    let failures = 1;
    spyOn(Storage.prototype, 'setItem').and.callFake(function (this: Storage, key: string, value: string) {
      if (key === 'rookhub_user' && failures-- > 0) throw quotaError();
      return original.call(this, key, value);
    });
    const clear = spyOn(offline, 'clearAll').and.callThrough();

    loginOk();

    expect(clear).toHaveBeenCalled();
    expect(JSON.parse(localStorage.getItem('rookhub_user')!).username).toBe('u');
    expect(svc.storageFull).toBeFalse();
    expect(svc.isLoggedIn).toBeTrue();
  });

  it('hält die Sitzung im Speicher des Tabs und markiert storageFull, wenn auch das Räumen nicht reicht', () => {
    spyOn(Storage.prototype, 'setItem').and.callFake((key: string) => {
      if (key === 'rookhub_user') throw quotaError();
    });

    loginOk();

    expect(svc.storageFull).toBeTrue();
    expect(svc.isLoggedIn).toBeTrue();          // die Anmeldung gilt trotzdem — nur nicht über ein Neuladen hinaus
    expect(localStorage.getItem('rookhub_user')).toBeNull();
  });

  it('setzt storageFull zurück, sobald das Speichern wieder klappt', () => {
    spyOn(Storage.prototype, 'setItem').and.throwError(quotaError());
    loginOk();
    expect(svc.storageFull).toBeTrue();

    (Storage.prototype.setItem as jasmine.Spy).and.callThrough();
    svc.adoptSession({ token: jwt(3600), username: 'u', userId: 1, isAdmin: false });
    expect(svc.storageFull).toBeFalse();
    expect(localStorage.getItem('rookhub_user')).not.toBeNull();
  });
});

describe('AuthService Live-Rechte (0.589.0)', () => {
  let svc: AuthService;
  let http: HttpTestingController;
  const tokenWith = (perms: string[]) =>
    `h.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600, perm: perms }))}.s`;

  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('rookhub_user', JSON.stringify({ token: tokenWith(['league.view']), username: 'fm', userId: 136, isAdmin: false }));
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    svc = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => { http.verify(); localStorage.clear(); });

  it('ohne Live-Stand gelten die Claims des Tokens, danach der Server — eine entzogene wie eine neue Rolle sofort', async () => {
    expect(svc.has('league.view')).toBeTrue();                 // Token von gestern
    expect(svc.has('league.contribute')).toBeFalse();
    const done = svc.refreshPermissions();
    http.expectOne('/api/auth/permissions').flush({ isAdmin: false, permissions: ['league.contribute'] });
    await done;
    expect(svc.has('league.contribute')).toBeTrue();           // neue Gruppenrolle, ohne neu anzumelden
    expect(svc.has('league.view')).toBeFalse();                // entzogen — der Claim zählt nicht mehr
  });

  it('ein Fehler beim Holen lässt den bisherigen Stand stehen', async () => {
    const done = svc.refreshPermissions();
    http.expectOne('/api/auth/permissions').flush(null, { status: 503, statusText: 'x' });
    await done;
    expect(svc.has('league.view')).toBeTrue();
  });
});

/**
 * UX-031: „Passwort vergessen“ schickt die Seite (feste Liste) und die Sprache mit — die Mail verlinkt dann die Seite,
 * von der die Anfrage kam, und kommt in deren Sprache. Ohne beide bleibt der Rumpf wie bisher (nur die E-Mail).
 */
describe('AuthService forgotPassword (UX-031)', () => {
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

  afterEach(() => { http.verify(); localStorage.clear(); });

  it('schickt Seite und Sprache mit', () => {
    svc.forgotPassword('a@b.co', 'kidhub', 'hu').subscribe();
    const req = http.expectOne({ method: 'POST', url: '/api/auth/forgot-password' });
    expect(JSON.parse(JSON.stringify(req.request.body))).toEqual({ email: 'a@b.co', site: 'kidhub', lang: 'hu' });
    req.flush({});
  });

  it('ohne Seite und Sprache bleibt der Rumpf wie bisher — nur die E-Mail', () => {
    svc.forgotPassword('a@b.co', null, null).subscribe();
    const req = http.expectOne('/api/auth/forgot-password');
    expect(JSON.stringify(req.request.body)).toBe('{"email":"a@b.co"}');
    req.flush({});
    svc.forgotPassword('a@b.co').subscribe();
    const again = http.expectOne('/api/auth/forgot-password');
    expect(JSON.stringify(again.request.body)).toBe('{"email":"a@b.co"}');
    again.flush({});
  });
});

describe('AuthService: gesperrter Browser-Speicher (F1-013)', () => {
  // Chrome mit „Cookies blockieren": jeder Zugriff auf den localStorage wirft. Die Sitzung gilt dann nur im
  // Tab (persistSession fängt das ab) — aber Anmelden und Abmelden dürfen daran nicht reißen.
  let svc: AuthService;
  let http: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    svc = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => localStorage.clear());

  function blockStorage(): void {
    const blocked = () => { const e = new Error('SecurityError'); e.name = 'SecurityError'; throw e; };
    spyOn(Storage.prototype, 'getItem').and.callFake(blocked);
    spyOn(Storage.prototype, 'setItem').and.callFake(blocked);
    spyOn(Storage.prototype, 'removeItem').and.callFake(blocked);
  }

  it('die Anmeldung gelingt — der Login-Stream reißt nicht am Nachlauf (anonyme Sitzung übernehmen)', () => {
    blockStorage();
    const next = jasmine.createSpy('next');
    const error = jasmine.createSpy('error');

    svc.login('u', 'p').subscribe({ next, error });
    http.expectOne('/api/auth/login').flush({ token: jwt(3600), username: 'u', userId: 1, isAdmin: false });

    expect(error).not.toHaveBeenCalled();
    expect(next).toHaveBeenCalled();
    expect(svc.isLoggedIn).toBeTrue();
    expect(svc.storageFull).toBeTrue();
  });

  it('Abmelden wirkt: Sitzung weg, Ende der geteilten Anmeldung, zurück zur Anmeldung', () => {
    svc.adoptSession({ token: jwt(3600), username: 'u', userId: 1, isAdmin: false });
    blockStorage();
    const nav = spyOn(router, 'navigate').and.resolveTo(true);

    expect(() => svc.logout()).not.toThrow();

    expect(svc.isLoggedIn).toBeFalse();
    expect(nav).toHaveBeenCalledWith(['/login']);
    http.expectOne({ method: 'POST', url: '/api/auth/rh-session/end' }).flush(null, { status: 204, statusText: 'No Content' });
  });

  it('ohne schreibbare Admin-Sicherung beginnt keine Impersonation (sonst gäbe es keinen Rücksprung)', () => {
    svc.adoptSession({ token: jwt(3600), username: 'admin', userId: 1, isAdmin: true });
    blockStorage();

    expect(svc.impersonate({ token: jwt(3600), username: 'opfer', userId: 2, isAdmin: false })).toBeFalse();

    expect(svc.currentUser?.username).toBe('admin');
    expect(svc.isImpersonating).toBeFalse();
  });

  it('Speicher voll: räumt für die Admin-Sicherung erst die Offline-Caches, dann gelingt der Einstieg (F1-013)', () => {
    svc.adoptSession({ token: jwt(3600), username: 'admin', userId: 1, isAdmin: true });
    const realSetItem = Storage.prototype.setItem;
    let quotaFull = true;
    spyOn(Storage.prototype, 'setItem').and.callFake(function (this: Storage, key: string, value: string) {
      if (quotaFull && key === 'rookhub_admin_user') {
        const e = new Error('QuotaExceededError'); e.name = 'QuotaExceededError'; throw e;
      }
      realSetItem.call(this, key, value);
    });
    const clearAll = spyOn(TestBed.inject(OfflineService), 'clearAll').and.callFake(() => { quotaFull = false; });

    expect(svc.impersonate({ token: jwt(3600), username: 'opfer', userId: 2, isAdmin: false })).toBeTrue();

    expect(clearAll).toHaveBeenCalledTimes(1);
    expect(svc.currentUser?.username).toBe('opfer');
    expect(svc.isImpersonating).toBeTrue();
    expect(JSON.parse(localStorage.getItem('rookhub_admin_user')!).username).toBe('admin');
  });

  it('übernimmt beim Anmelden die anonymen Puzzle-Versuche, die unter der Speicher-Kennung liefen (F2-018)', async () => {
    blockStorage();
    const anon = getOrCreateAnonSessionId(ANON_PUZZLE_SESSION_KEY);   // wie PuzzleService bei jedem anonymen Versuch

    svc.login('u', 'p').subscribe();
    http.expectOne('/api/auth/login').flush({ token: jwt(3600), username: 'u', userId: 1, isAdmin: false });

    // Der Dienst kommt per dynamischem Import (Zirkelbezug) — die Anfrage folgt erst danach.
    let claim = http.match('/api/puzzles/claim-session');
    for (let i = 0; i < 50 && claim.length === 0; i++) {
      await new Promise<void>(r => setTimeout(r, 10));
      claim = http.match('/api/puzzles/claim-session');
    }
    expect(claim.length).toBe(1);
    expect(claim[0].request.body).toEqual({ sessionId: anon });
  });
});
