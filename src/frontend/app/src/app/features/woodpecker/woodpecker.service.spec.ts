import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WoodpeckerService } from './woodpecker.service';

/**
 * 0.993.0: der Server zählt „heute bearbeitet“ (`todayAttempted`) für den lokalen Kalendertag — dafür muss JEDER
 * Aufruf, der einen Plan zurückgibt, die Zeitzone des Geräts mitschicken (`utcOffsetMinutes`, Minuten östlich von UTC,
 * wie schon `/stats`). Die Liste unten ist bewusst literal: kommt ein Plan-Endpunkt dazu, gehört er hier hinein.
 */
describe('WoodpeckerService (Zeitzone an allen Plan-Aufrufen)', () => {
  let svc: WoodpeckerService;
  let http: HttpTestingController;
  const offset = String(-new Date().getTimezoneOffset());

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    svc = TestBed.inject(WoodpeckerService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  const CALLS: { name: string; method: string; url: string; run: (s: WoodpeckerService) => unknown }[] = [
    { name: 'list', method: 'GET', url: '/api/woodpecker', run: s => s.list().subscribe() },
    { name: 'get', method: 'GET', url: '/api/woodpecker/3', run: s => s.get(3).subscribe() },
    { name: 'create', method: 'POST', url: '/api/woodpecker', run: s => s.create([7]).subscribe() },
    { name: 'archive', method: 'POST', url: '/api/woodpecker/3/archive', run: s => s.archive(3).subscribe() },
    { name: 'unarchive', method: 'POST', url: '/api/woodpecker/3/unarchive', run: s => s.unarchive(3).subscribe() },
    { name: 'updateStart', method: 'PUT', url: '/api/woodpecker/3/start', run: s => s.updateStart(3, '2026-11-01T00:00:00Z').subscribe() },
    { name: 'finishSet', method: 'POST', url: '/api/woodpecker/3/finish-set', run: s => s.finishSet(3).subscribe() },
    { name: 'startNextCycle', method: 'POST', url: '/api/woodpecker/3/next-cycle', run: s => s.startNextCycle(3).subscribe() },
    { name: 'next', method: 'GET', url: '/api/woodpecker/3/next', run: s => s.next(3).subscribe() },
    { name: 'recordAttempt', method: 'POST', url: '/api/woodpecker/3/attempts', run: s => s.recordAttempt(3, 9, true, 12, 1).subscribe() },
    { name: 'stats', method: 'GET', url: '/api/woodpecker/3/stats', run: s => s.stats(3).subscribe() },
  ];

  for (const c of CALLS) {
    it(`${c.name}: ${c.method} ${c.url} schickt utcOffsetMinutes dieses Geräts`, () => {
      c.run(svc);
      const req = http.expectOne(r => r.url === c.url && r.method === c.method);
      expect(req.request.params.get('utcOffsetMinutes')).toBe(offset);
      req.flush(c.name === 'list' ? [] : {});
    });
  }

  it('next: die Sprache bleibt neben der Zeitzone erhalten; ohne Sprache kein lang-Parameter', () => {
    svc.next(3, 'de').subscribe();
    const withLang = http.expectOne(r => r.url === '/api/woodpecker/3/next');
    expect(withLang.request.params.get('lang')).toBe('de');
    expect(withLang.request.params.get('utcOffsetMinutes')).toBe(offset);
    withLang.flush({});
    svc.next(3, null).subscribe();
    const plain = http.expectOne(r => r.url === '/api/woodpecker/3/next');
    expect(plain.request.params.has('lang')).toBeFalse();
    plain.flush({});
  });

  it('recordAttempt: der Rumpf bleibt, wie die Offline-Warteschlange ihn nachsendet', () => {
    svc.recordAttempt(3, 9, false, 40, 2).subscribe();
    const req = http.expectOne(r => r.url === '/api/woodpecker/3/attempts');
    expect(req.request.body).toEqual({ bookPuzzleId: 9, solved: false, timeSeconds: 40, hintsUsed: 2 });
    req.flush({});
  });

  it('delete gibt keinen Plan zurück und braucht keine Zeitzone', () => {
    svc.delete(3).subscribe();
    const req = http.expectOne('/api/woodpecker/3');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
