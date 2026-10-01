import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideTranslateService } from '@ngx-translate/core';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EndlessStorageService, EndlessConfig } from './endless-storage.service';
import { AuthService } from '../../core/auth.service';
import { ENDLESS_POOL_KEY } from '../../core/offline.service';
import { ANON_PUZZLE_SESSION_KEY, getOrCreateAnonSessionId } from '../../core/anon-session';

const CONFIG: EndlessConfig = {
  startElo: 1500, themes: '', stockfishDepth: 8,
};

describe('EndlessStorageService highscore sync', () => {
  let svc: EndlessStorageService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: true } },
      ],
    });
    svc = TestBed.inject(EndlessStorageService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('never sends a highscore lower than the server value', () => {
    svc.loadFromServer().subscribe();
    http.expectOne(r => r.method === 'GET' && r.url.endsWith('/progress')).flush({
      progress: { ...CONFIG, highscore: 1500, updatedAt: '' },
      sessions: [],
    });

    // Lokaler Save mit niedrigerem Highscore -> darf den 1500er nicht unterbieten.
    svc.saveProgressToServer(CONFIG, 1200, null);

    const put = http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/progress'));
    expect(put.request.body.highscore).toBe(1500);
    put.flush({});
  });
});

describe('EndlessStorageService per-identity migration flag', () => {
  let svc: EndlessStorageService;
  let http: HttpTestingController;
  const auth: any = { isLoggedIn: true, currentUser: { userId: 5, username: 'u5' } };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: auth },
      ],
    });
    svc = TestBed.inject(EndlessStorageService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => { http.verify(); localStorage.clear(); });

  it('migrates once per identity and again for a different user on the same browser', () => {
    // User 5: erste Migration -> ein PUT + identitaetsspezifischer Flag
    svc.migrateLocalToServer(CONFIG, 100, []);
    http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/progress')).flush({});
    expect(localStorage.getItem('rookhub_endless_synced:u5')).toBe('1');

    // User 5 erneut: bereits migriert -> kein weiterer Request
    svc.migrateLocalToServer(CONFIG, 100, []);
    http.expectNone(r => r.method === 'PUT' && r.url.endsWith('/progress'));

    // Anderer User (8) im selben Browser: migriert erneut -> ein PUT + eigener Flag
    auth.currentUser = { userId: 8, username: 'u8' };
    svc.migrateLocalToServer(CONFIG, 100, []);
    http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/progress')).flush({});
    expect(localStorage.getItem('rookhub_endless_synced:u8')).toBe('1');
  });
});

describe('EndlessStorageService anonyme Kennung (F2-018)', () => {
  // Vorher las Endless die Kennung roh: bei gesperrtem Speicher null -> Lauf und Fortschritt erreichten
  // den Server nie, während die Versuche desselben Laufs über PuzzleService (mit Rückfallebene) ankamen.
  let svc: EndlessStorageService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.removeItem(ANON_PUZZLE_SESSION_KEY);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: false } },
      ],
    });
    svc = TestBed.inject(EndlessStorageService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => { http.verify(); localStorage.removeItem(ANON_PUZZLE_SESSION_KEY); });

  it('meldet Lauf und Fortschritt bei gesperrtem Speicher unter derselben Kennung wie die Puzzle-Versuche', () => {
    spyOn(Storage.prototype, 'getItem').and.throwError('SecurityError');
    spyOn(Storage.prototype, 'setItem').and.throwError('SecurityError');
    const puzzleId = getOrCreateAnonSessionId(ANON_PUZZLE_SESSION_KEY);   // wie PuzzleService.ensureSessionId

    svc.loadFromServer().subscribe();
    const get = http.expectOne(r => r.method === 'GET' && r.url.endsWith('/progress/anonymous'));
    expect(get.request.params.get('sessionId')).toBe(puzzleId);
    get.flush({ progress: null, sessions: [] });

    svc.recordSessionToServer({
      timestamp: 1, totalSolved: 3, maxRating: 1600, durationSeconds: 60, config: CONFIG, mistakeAtRatings: [],
    }).subscribe();
    const post = http.expectOne(r => r.method === 'POST' && r.url.endsWith('/sessions/anonymous'));
    expect(post.request.body.sessionId).toBe(puzzleId);
    post.flush({ id: 7 });
  });

  it('legt eine fehlende Kennung an, wenn ein anonymer Lauf gemeldet wird — und übernimmt ohne Kennung nichts', () => {
    svc.claimEndlessSession().subscribe();
    http.expectNone(r => r.url.endsWith('/claim-session'));
    expect(localStorage.getItem(ANON_PUZZLE_SESSION_KEY)).toBeNull();

    svc.saveProgressImmediate(CONFIG, 100, null);
    const put = http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/progress/anonymous'));
    expect(put.request.body.sessionId).toBe(localStorage.getItem(ANON_PUZZLE_SESSION_KEY)!);
    put.flush({});
  });
});

describe('EndlessStorageService offline pool shares ENDLESS_POOL_KEY with OfflineService', () => {
  let svc: EndlessStorageService;

  beforeEach(() => {
    localStorage.removeItem(ENDLESS_POOL_KEY);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: false } },
      ],
    });
    svc = TestBed.inject(EndlessStorageService);
  });

  afterEach(() => localStorage.removeItem(ENDLESS_POOL_KEY));

  it('writes the offline pool under exactly ENDLESS_POOL_KEY', () => {
    svc.saveOfflinePool([{ id: 1 } as any]);
    expect(localStorage.getItem(ENDLESS_POOL_KEY)).toContain('"id":1');
  });

  it('loads a pool written directly under ENDLESS_POOL_KEY (shared key, not a private copy)', () => {
    localStorage.setItem(ENDLESS_POOL_KEY, JSON.stringify([{ id: 7 }]));
    expect(svc.loadOfflinePool()).toEqual([{ id: 7 } as any]);
  });
});

describe('EndlessStorageService Live-Zeitstand', () => {
  let svc: EndlessStorageService;
  const LIVE_KEY = 'rookhub_endless_live_elapsed';

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: false } },
      ],
    });
    svc = TestBed.inject(EndlessStorageService);
  });

  afterEach(() => localStorage.clear());

  it('save/load roundtrip', () => {
    expect(svc.loadLiveElapsed()).toBeNull();
    svc.saveLiveElapsed({ seed: 's1', chainIndex: 4, session: 120, puzzle: 7 });
    expect(svc.loadLiveElapsed()).toEqual({ seed: 's1', chainIndex: 4, session: 120, puzzle: 7 });
  });

  it('saveActiveGameLocal(null) räumt auch den Live-Zeitstand weg', () => {
    svc.saveLiveElapsed({ seed: 's1', chainIndex: 4, session: 120, puzzle: 7 });
    svc.saveActiveGameLocal({ lives: 2 });
    expect(svc.loadLiveElapsed()).not.toBeNull();   // aktiver Lauf → Live-Stand bleibt
    svc.saveActiveGameLocal(null);
    expect(svc.loadLiveElapsed()).toBeNull();       // Lauf beendet → Live-Stand obsolet
  });

  it('kaputter Storage-Inhalt liefert null statt zu werfen', () => {
    localStorage.setItem(LIVE_KEY, '{kaputt');
    expect(svc.loadLiveElapsed()).toBeNull();
  });
});

describe('EndlessStorageService mergeServerData', () => {
  let svc: EndlessStorageService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: true } },
      ],
    });
    svc = TestBed.inject(EndlessStorageService);
  });

  afterEach(() => localStorage.clear());

  it('Server-Felder gewinnen, das rein lokale worstTags bleibt erhalten (auch im Speicher)', () => {
    const local: EndlessConfig = { startElo: 1200, themes: 'fork pin', stockfishDepth: 16, worstTags: true };
    const res = svc.mergeServerData(local, 0, [], {
      progress: { startElo: 1500, themes: 'hangingPiece', stockfishDepth: 12, highscore: 1800, updatedAt: '' },
      sessions: [],
    });
    expect(res.config.startElo).toBe(1500);
    expect(res.config.themes).toBe('hangingPiece');
    expect(res.config.stockfishDepth).toBe(12);
    expect(res.config.worstTags).toBeTrue();
    expect(svc.loadConfig({ startElo: 700, themes: '', stockfishDepth: 16 }).worstTags).toBeTrue();
  });
});
