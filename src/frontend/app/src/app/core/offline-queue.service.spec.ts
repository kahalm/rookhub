import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { OfflineQueueService, OFFLINE_QUEUE_KEY, OFFLINE_QUEUE_THROTTLE_MS } from './offline-queue.service';

describe('OfflineQueueService', () => {
  let svc: OfflineQueueService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    svc = TestBed.inject(OfflineQueueService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => { localStorage.clear(); });

  it('merkt einen Request im localStorage vor', () => {
    svc.enqueue('POST', '/api/puzzles/5/attempt', { solved: true });
    expect(svc.pendingCount()).toBe(1);
    const raw = JSON.parse(localStorage.getItem(OFFLINE_QUEUE_KEY)!);
    expect(raw[0].url).toBe('/api/puzzles/5/attempt');
    expect(raw[0].method).toBe('POST');
  });

  it('spielt vorgemerkte Requests bei flush ein und leert die Queue bei Erfolg', fakeAsync(() => {
    svc.enqueue('POST', '/api/puzzles/5/attempt', { solved: true });
    svc.enqueue('POST', '/api/courses/2/results', { bookPuzzleId: 9, solved: true });
    svc.flush();

    const r1 = http.expectOne('/api/puzzles/5/attempt');
    expect(r1.request.method).toBe('POST');
    expect(r1.request.body).toEqual({ solved: true });
    r1.flush({});

    // Der nächste Eintrag geht erst nach dem Drosselabstand raus (Rate-Limit-Schutz).
    http.expectNone('/api/courses/2/results');
    tick(OFFLINE_QUEUE_THROTTLE_MS);
    const r2 = http.expectOne('/api/courses/2/results');
    r2.flush({});

    expect(svc.pendingCount()).toBe(0);
  }));

  it('behält den Eintrag bei Netzwerkfehler (Status 0)', () => {
    svc.enqueue('POST', '/api/puzzles/5/attempt', { solved: false });
    svc.flush();
    const req = http.expectOne('/api/puzzles/5/attempt');
    req.error(new ProgressEvent('error'), { status: 0, statusText: 'offline' });
    expect(svc.pendingCount()).toBe(1);
  });

  it('verwirft den Eintrag bei dauerhaftem 4xx-Fehler', () => {
    svc.enqueue('POST', '/api/puzzles/5/attempt', { solved: true });
    svc.flush();
    const req = http.expectOne('/api/puzzles/5/attempt');
    req.flush({ message: 'gone' }, { status: 404, statusText: 'Not Found' });
    expect(svc.pendingCount()).toBe(0);
  });

  it('verwirft NICHTS bei 429 (Rate-Limit) und pausiert den Nachlauf', () => {
    svc.enqueue('POST', '/api/puzzles/5/attempt', { solved: true });
    svc.enqueue('POST', '/api/puzzles/6/attempt', { solved: true });
    svc.flush();
    http.expectOne('/api/puzzles/5/attempt')
      .flush({ message: 'rate limit' }, { status: 429, statusText: 'Too Many Requests' });
    expect(svc.pendingCount()).toBe(2);            // gemerkte Lösungen bleiben erhalten
    http.expectNone('/api/puzzles/6/attempt');     // Rest erst nach dem Backoff-Retry
  });

  it('respektiert Retry-After des 429 und sendet danach erneut', fakeAsync(() => {
    svc.enqueue('POST', '/api/puzzles/5/attempt', { solved: true });
    svc.flush();
    http.expectOne('/api/puzzles/5/attempt').flush(
      { message: 'rate limit' },
      { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '5' } },
    );
    tick(4999);
    http.expectNone('/api/puzzles/5/attempt');
    tick(1);
    http.expectOne('/api/puzzles/5/attempt').flush({});
    expect(svc.pendingCount()).toBe(0);
  }));

  it('flush ohne Einträge löst keinen Request aus', () => {
    svc.flush();
    http.expectNone(() => true);
    expect(svc.pendingCount()).toBe(0);
  });

  // ── User-Stempel: Cross-User-Schutz auf geteiltem Gerät ──────────────────────
  function login(userId: number): void {
    localStorage.setItem('rookhub_user', JSON.stringify({ token: 't', username: 'u' + userId, userId, isAdmin: false }));
  }

  it('stempelt Einträge mit der aktuellen User-Id', () => {
    login(7);
    svc.enqueue('POST', '/api/puzzles/5/attempt', { solved: true });
    const raw = JSON.parse(localStorage.getItem(OFFLINE_QUEUE_KEY)!);
    expect(raw[0].userId).toBe(7);
  });

  it('anonyme Einträge (kein Login) tragen userId null', () => {
    svc.enqueue('POST', '/api/book-puzzles/5/attempt/anonymous', { solved: true, sessionId: 's' });
    const raw = JSON.parse(localStorage.getItem(OFFLINE_QUEUE_KEY)!);
    expect(raw[0].userId).toBeNull();
  });

  it('flusht NICHT die Einträge eines anderen Users (bleiben liegen)', () => {
    login(7);
    svc.enqueue('POST', '/api/puzzles/5/attempt', { solved: true });   // gehört User 7
    login(9);                                                          // Nutzerwechsel auf demselben Gerät
    svc.flush();
    http.expectNone('/api/puzzles/5/attempt');   // A's Lösung geht NICHT unter B's Bearer raus
    expect(svc.pendingCount()).toBe(1);          // bleibt für User 7 erhalten
  });

  it('sendet gemischt: eigenen + anonymen Eintrag, fremden überspringen', fakeAsync(() => {
    login(7);
    svc.enqueue('POST', '/api/a', { x: 1 });        // User 7
    login(9);
    svc.enqueue('POST', '/api/b', { x: 2 });        // User 9
    localStorage.removeItem('rookhub_user');
    svc.enqueue('POST', '/api/c/anonymous', { x: 3 }); // anonym
    login(9);
    svc.flush();
    http.expectNone('/api/a');                       // fremd (User 7) → übersprungen
    http.expectOne('/api/b').flush({});              // eigener
    tick(OFFLINE_QUEUE_THROTTLE_MS);
    http.expectOne('/api/c/anonymous').flush({});    // anonym
    expect(svc.pendingCount()).toBe(1);              // nur User-7-Eintrag bleibt
  }));

  it('flusht eigene Einträge nach Wieder-Login', () => {
    login(7);
    svc.enqueue('POST', '/api/puzzles/5/attempt', { solved: true });
    login(9); svc.flush();
    http.expectNone('/api/puzzles/5/attempt');
    login(7); svc.flush();                            // User 7 kommt zurück
    http.expectOne('/api/puzzles/5/attempt').flush({});
    expect(svc.pendingCount()).toBe(0);
  });

  afterEach(() => http.verify());
});
