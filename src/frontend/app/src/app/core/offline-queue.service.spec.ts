import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { OfflineQueueService, OFFLINE_QUEUE_KEY } from './offline-queue.service';

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

  it('spielt vorgemerkte Requests bei flush ein und leert die Queue bei Erfolg', () => {
    svc.enqueue('POST', '/api/puzzles/5/attempt', { solved: true });
    svc.enqueue('POST', '/api/courses/2/results', { bookPuzzleId: 9, solved: true });
    svc.flush();

    const r1 = http.expectOne('/api/puzzles/5/attempt');
    expect(r1.request.method).toBe('POST');
    expect(r1.request.body).toEqual({ solved: true });
    r1.flush({});

    const r2 = http.expectOne('/api/courses/2/results');
    r2.flush({});

    expect(svc.pendingCount()).toBe(0);
  });

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

  it('flush ohne Einträge löst keinen Request aus', () => {
    svc.flush();
    http.expectNone(() => true);
    expect(svc.pendingCount()).toBe(0);
  });

  afterEach(() => http.verify());
});
