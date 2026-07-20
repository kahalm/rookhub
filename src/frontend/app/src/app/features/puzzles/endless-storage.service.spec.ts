import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EndlessStorageService, EndlessConfig } from './endless-storage.service';
import { AuthService } from '../../core/auth.service';
import { ENDLESS_POOL_KEY } from '../../core/offline.service';

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

describe('EndlessStorageService offline pool shares ENDLESS_POOL_KEY with OfflineService', () => {
  let svc: EndlessStorageService;

  beforeEach(() => {
    localStorage.removeItem(ENDLESS_POOL_KEY);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
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
