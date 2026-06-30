import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PuzzleService } from './puzzle.service';

describe('PuzzleService', () => {
  let service: PuzzleService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [PuzzleService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PuzzleService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => { httpMock.verify(); localStorage.clear(); });

  it('getRandom lässt nicht gesetzte optionale Parameter weg', () => {
    service.getRandom().subscribe();
    const req = httpMock.expectOne(r => r.url === '/api/puzzles/random');
    expect(req.request.params.keys().length).toBe(0);
    req.flush({});
  });

  it('getRandom setzt nur die übergebenen Parameter', () => {
    service.getRandom(1200, 1600, undefined, true, 'fork pin').subscribe();
    const req = httpMock.expectOne(r => r.url === '/api/puzzles/random');
    expect(req.request.params.get('minRating')).toBe('1200');
    expect(req.request.params.get('maxRating')).toBe('1600');
    expect(req.request.params.get('excludeSolved')).toBe('true');
    expect(req.request.params.get('themesAny')).toBe('fork pin');
    expect(req.request.params.has('themes')).toBeFalse();
    req.flush({});
  });

  it('getWorstThemes mappt die Theme-Stats auf reine Theme-Namen', () => {
    let result: string[] | undefined;
    service.getWorstThemes(2, 5).subscribe(r => (result = r));
    const req = httpMock.expectOne(r => r.url === '/api/puzzles/stats/worst-themes');
    expect(req.request.params.get('count')).toBe('2');
    expect(req.request.params.get('minAttempts')).toBe('5');
    req.flush([{ theme: 'fork', attempts: 10, solved: 2 }, { theme: 'pin', attempts: 8, solved: 1 }]);
    expect(result).toEqual(['fork', 'pin']);
  });

  it('recordAttempt schickt Solve-Daten + Bildschirmgröße', () => {
    service.recordAttempt(5, true, 42, 'log', 2, true, 1, 3).subscribe();
    const req = httpMock.expectOne('/api/puzzles/5/attempt');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.solved).toBeTrue();
    expect(req.request.body.timeSpentSeconds).toBe(42);
    expect(req.request.body.visualizationLevel).toBe(2);
    expect(req.request.body.hintsUsed).toBe(3);
    expect(req.request.body.screenWidth).toBe(window.innerWidth);
    req.flush({});
  });

  it('ensureSessionId erzeugt eine stabile UUID und persistiert sie', () => {
    const id1 = service.ensureSessionId();
    const id2 = service.ensureSessionId();
    expect(id1).toBe(id2);
    expect(localStorage.getItem('rookhub_puzzle_session')).toBe(id1);
  });

  it('getAnonymousStats hängt die Session-Id als Parameter an', () => {
    const id = service.ensureSessionId();
    service.getAnonymousStats().subscribe();
    const req = httpMock.expectOne(r => r.url === '/api/puzzles/stats/anonymous');
    expect(req.request.params.get('sessionId')).toBe(id);
    req.flush({});
  });

  it('getDailyPuzzle baut die Datums-Route', () => {
    service.getDailyPuzzle('20260630').subscribe();
    httpMock.expectOne('/api/book-puzzles/daily/20260630').flush({});
  });
});
