import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AdminService, DailyPuzzleInfo } from './admin.service';

/** Tests für die Admin-Tagespuzzle-Endpoints (laden + neu generieren). */
describe('AdminService daily puzzle', () => {
  let svc: AdminService;
  let http: HttpTestingController;

  const sample: DailyPuzzleInfo = {
    id: 42, lineId: 'book.pgn:7', bookFileName: 'book.pgn',
    title: 'Mate in 2', difficulty: 'medium', bookRating: 5,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    svc = TestBed.inject(AdminService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('GETs the daily puzzle for a date', () => {
    let result: DailyPuzzleInfo | undefined;
    svc.getDailyPuzzle('20260605').subscribe(p => (result = p));
    const req = http.expectOne('/api/book-puzzles/daily/20260605');
    expect(req.request.method).toBe('GET');
    req.flush(sample);
    expect(result?.id).toBe(42);
  });

  it('POSTs to regenerate the daily puzzle for a date', () => {
    let result: DailyPuzzleInfo | undefined;
    svc.regenerateDailyPuzzle('20260605').subscribe(p => (result = p));
    const req = http.expectOne('/api/admin/book-puzzles/daily/20260605/regenerate');
    expect(req.request.method).toBe('POST');
    req.flush({ ...sample, id: 99 });
    expect(result?.id).toBe(99);
  });
});
