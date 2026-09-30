import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpErrorResponse, HttpHeaders, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CATALOG_TTL_MS, KidsApiService, KidsLevel, RATE_LIMIT_WINDOW_MS, retryAfterMs } from './kids-api.service';

/**
 * Codereview 2026-09-29, A10-003: eine Schulklasse hinter EINER NAT-Adresse lief in die Drossel — jede
 * Startseite und jeder Stufenstart holte Stufen und Kurse neu, und ein 429 zeigte sofort das Fehlerbild.
 */
describe('KidsApiService', () => {
  const LEVELS: KidsLevel[] = [{ level: 1, theme: 'mate1', puzzleCount: 10 }, { level: 2, theme: 'promote', puzzleCount: 10 }];
  const tooMany = (retryAfter?: string) => ({
    status: 429, statusText: 'Too Many Requests',
    headers: retryAfter === undefined ? undefined : new HttpHeaders({ 'Retry-After': retryAfter }),
  });
  let api: KidsApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(KidsApiService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  describe('Stufen und Kurse zwischenspeichern', () => {
    it('Startseite, Stufenkarte und Stufenstart teilen sich EINE Abfrage der Leiter', () => {
      const seen: KidsLevel[][] = [];
      api.levels().subscribe(l => seen.push(l));
      api.levels().subscribe(l => seen.push(l));
      http.expectOne('/api/kids/levels').flush(LEVELS);
      api.levels().subscribe(l => seen.push(l));
      http.expectNone('/api/kids/levels');
      expect(seen).toEqual([LEVELS, LEVELS, LEVELS]);
    });

    it('nach der Frist, die der Server dem Browser erlaubt, fragt die Seite wieder', fakeAsync(() => {
      let answers = 0;
      api.levels().subscribe(() => answers++);
      http.expectOne('/api/kids/levels').flush(LEVELS);
      tick(CATALOG_TTL_MS - 1);
      api.levels().subscribe(() => answers++);
      http.expectNone('/api/kids/levels');
      tick(1);
      api.levels().subscribe(() => answers++);
      http.expectOne('/api/kids/levels').flush(LEVELS);
      expect(answers).toBe(3);
    }));

    it('eine leere Leiter (noch nicht aufgebaut) bleibt nicht liegen', () => {
      const seen: KidsLevel[][] = [];
      api.levels().subscribe(l => seen.push(l));
      http.expectOne('/api/kids/levels').flush([]);
      api.levels().subscribe(l => seen.push(l));
      http.expectOne('/api/kids/levels').flush(LEVELS);
      expect(seen).toEqual([[], LEVELS]);
    });

    it('ein Fehler bleibt nicht liegen — der naechste Aufruf fragt neu', () => {
      let failed = false;
      api.levels().subscribe({ error: () => failed = true });
      http.expectOne('/api/kids/levels').flush(null, { status: 500, statusText: 'Server Error' });
      expect(failed).toBeTrue();
      api.levels().subscribe();
      http.expectOne('/api/kids/levels').flush(LEVELS);
    });

    it('die Kursliste je Sprache', () => {
      const de = [{ bookId: 1, title: 'Matt in einem Zug', description: null, puzzleCount: 10 }];
      const en = [{ bookId: 1, title: 'Mate in one', description: null, puzzleCount: 10 }];
      const seen: string[] = [];
      api.courses('de').subscribe(c => seen.push(c[0].title));
      api.courses('en').subscribe(c => seen.push(c[0].title));
      http.expectOne('/api/kids/courses?lang=de').flush(de);
      http.expectOne('/api/kids/courses?lang=en').flush(en);
      api.courses('de').subscribe(c => seen.push(c[0].title));
      http.expectNone('/api/kids/courses?lang=de');
      expect(seen).toEqual(['Matt in einem Zug', 'Mate in one', 'Matt in einem Zug']);
    });
  });

  describe('429 einmal nachholen statt Fehlerbild', () => {
    it('wartet das Retry-After ab und liefert dann die Stufen', fakeAsync(() => {
      let levels = null as KidsLevel[] | null;
      let error: unknown = null;
      api.levels().subscribe({ next: l => levels = l, error: e => error = e });
      http.expectOne('/api/kids/levels').flush(null, tooMany('3'));
      expect(error).toBeNull();
      tick(2999);
      http.expectNone('/api/kids/levels');
      tick(1);
      http.expectOne('/api/kids/levels').flush(LEVELS);
      expect(levels!).toEqual(LEVELS);
      expect(error).toBeNull();
    }));

    it('ohne Retry-After (die API schickt heute keins) ein ganzes Minutenfenster', fakeAsync(() => {
      let done = false;
      api.level(4).subscribe({ next: () => done = true });
      http.expectOne('/api/kids/levels/4').flush(null, tooMany());
      tick(RATE_LIMIT_WINDOW_MS - 1);
      http.expectNone('/api/kids/levels/4');
      tick(1);
      http.expectOne('/api/kids/levels/4').flush({ level: 4, theme: 'fork', puzzles: [] });
      expect(done).toBeTrue();
    }));

    it('nur EINMAL: ein zweites 429 kommt als Fehler an', fakeAsync(() => {
      let error = null as HttpErrorResponse | null;
      api.courses('de').subscribe({ error: e => error = e });
      http.expectOne('/api/kids/courses?lang=de').flush(null, tooMany('1'));
      tick(1000);
      http.expectOne('/api/kids/courses?lang=de').flush(null, tooMany('1'));
      tick(RATE_LIMIT_WINDOW_MS);
      http.expectNone('/api/kids/courses?lang=de');
      expect(error!.status).toBe(429);
    }));

    it('auch die Aufgaben eines Kurses', fakeAsync(() => {
      let lines: unknown = null;
      api.coursePuzzles(339, 'de').subscribe(l => lines = l);
      http.expectOne('/api/kids/courses/339/puzzles?lang=de').flush(null, tooMany('2'));
      tick(2000);
      http.expectOne('/api/kids/courses/339/puzzles?lang=de').flush([]);
      expect(lines).toEqual([]);
    }));

    it('andere Fehler wiederholt diese Stelle nicht (das macht der retryInterceptor fuer 502/503/504/0)', () => {
      let error = null as HttpErrorResponse | null;
      api.level(2).subscribe({ error: e => error = e });
      http.expectOne('/api/kids/levels/2').flush(null, { status: 404, statusText: 'Not Found' });
      expect(error!.status).toBe(404);
    });
  });

  describe('retryAfterMs', () => {
    const err = (value?: string) => new HttpErrorResponse({
      status: 429, headers: value === undefined ? new HttpHeaders() : new HttpHeaders({ 'Retry-After': value }),
    });

    it('Sekunden, gedeckelt auf 1 s bis ein Minutenfenster', () => {
      expect(retryAfterMs(err('5'))).toBe(5000);
      expect(retryAfterMs(err('0'))).toBe(1000);
      expect(retryAfterMs(err('3600'))).toBe(RATE_LIMIT_WINDOW_MS);
    });

    it('HTTP-Datum', () => {
      const at = new Date(Date.now() + 10_000).toUTCString();
      const ms = retryAfterMs(err(at));
      expect(ms).toBeGreaterThan(8000);
      expect(ms).toBeLessThanOrEqual(10_000);
    });

    it('fehlend oder unlesbar: ein ganzes Minutenfenster', () => {
      expect(retryAfterMs(err())).toBe(RATE_LIMIT_WINDOW_MS);
      expect(retryAfterMs(err('bald'))).toBe(RATE_LIMIT_WINDOW_MS);
    });
  });
});
