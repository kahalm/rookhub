import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { AuthResponse, AuthService } from '@rh/core/auth.service';
import { KidsProgressSync, SYNC_DEBOUNCE_MS } from './kids-progress-sync.service';
import { KidsProgressDto, KidsProgressStore } from './kids-progress.store';

describe('KidsProgressSync', () => {
  const KEY = 'rh-kids-progress-v1';
  const lena: AuthResponse = { token: 'e30.e30.x', username: 'lena', userId: 7, isAdmin: false };
  const tom: AuthResponse = { token: 'e30.e30.y', username: 'tom', userId: 8, isAdmin: false };
  const empty: KidsProgressDto = { levels: [], courses: [] };

  // Der Dienst liest nur `currentUser$` — die echte Anmeldung laedt Einstellungen per dynamischem Import,
  // und der laeuft unter fakeAsync in einen Chunk-Timeout.
  let user: BehaviorSubject<AuthResponse | null>;

  beforeEach(() => {
    localStorage.removeItem(KEY);
    user = new BehaviorSubject<AuthResponse | null>(null);
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: { currentUser$: user } }],
    });
  });
  afterEach(() => localStorage.removeItem(KEY));

  const http = () => TestBed.inject(HttpTestingController);
  const progressCalls = () => http().match(r => r.url === '/api/kids/progress');
  function start() {
    const sync = TestBed.inject(KidsProgressSync);
    TestBed.tick();
    return sync;
  }

  it('ohne Anmeldung fragt es nie', fakeAsync(() => {
    start();
    TestBed.inject(KidsProgressStore).completeRun(1);
    TestBed.tick();
    tick(SYNC_DEBOUNCE_MS * 2);
    expect(progressCalls().length).toBe(0);
  }));

  it('beim Anmelden geht der Stand ohne Konto hinauf, zurueck kommt der gemeinsame', fakeAsync(() => {
    const store = TestBed.inject(KidsProgressStore);
    store.completeRun(1);                                 // ohne Konto gespielt
    const sync = start();

    user.next(lena);
    const [req] = progressCalls();
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.levels[0]).toEqual(jasmine.objectContaining({ level: 1, stars: 3 }));
    req.flush({ levels: [{ level: 1, stars: 3, runIndex: 0, runMistakes: 0, runAt: 1 }, { level: 2, stars: 2, runIndex: 0, runMistakes: 0, runAt: 1 }], courses: [] });

    expect(store.level(2).stars).toBe(2);                 // vom anderen Geraet
    expect(store.owner()).toBe(7);
    expect(sync.state()).toBe('saved');
  }));

  it('Aenderungen gehen gebuendelt hinauf', fakeAsync(() => {
    start();
    user.next(lena);
    progressCalls()[0].flush(empty);

    const store = TestBed.inject(KidsProgressStore);
    store.recordSolved(1, 0);
    store.recordSolved(1, 1);
    TestBed.tick();
    tick(SYNC_DEBOUNCE_MS - 1);
    expect(progressCalls().length).toBe(0);
    tick(1);
    const calls = progressCalls();
    expect(calls.length).toBe(1);
    expect(calls[0].request.body.levels[0]).toEqual(jasmine.objectContaining({ level: 1, runIndex: 2, runMistakes: 1 }));
    calls[0].flush(calls[0].request.body);
  }));

  it('Abmelden leert den Stand des Kontos hier — Anmelden mit einem anderen Konto uebernimmt ihn nicht', fakeAsync(() => {
    const store = TestBed.inject(KidsProgressStore);
    start();
    user.next(lena);
    progressCalls()[0].flush({ levels: [{ level: 1, stars: 3, runIndex: 0, runMistakes: 0, runAt: 1 }], courses: [] });
    expect(store.totalStars()).toBe(3);

    user.next(null);                                      // abgemeldet
    expect(store.totalStars()).toBe(0);
    expect(store.owner()).toBeNull();

    // Ohne Abmelden direkt ein anderes Konto (z. B. per Sprung): Lenas Stand darf nicht zu Tom.
    user.next(lena);
    progressCalls()[0].flush({ levels: [{ level: 1, stars: 3, runIndex: 0, runMistakes: 0, runAt: 1 }], courses: [] });
    user.next(tom);
    const [req] = progressCalls();
    expect(req.request.body).toEqual(empty);
    req.flush(empty);
    expect(store.owner()).toBe(8);
  }));

  it('ohne Netz bleibt alles lokal und der Zustand sagt es', fakeAsync(() => {
    const store = TestBed.inject(KidsProgressStore);
    store.completeRun(1);
    const sync = start();
    user.next(lena);
    progressCalls()[0].flush('weg', { status: 503, statusText: 'Service Unavailable' });
    expect(sync.state()).toBe('offline');
    expect(store.level(1).stars).toBe(3);
  }));
});
