import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WeeklyService } from './weekly.service';

describe('WeeklyService', () => {
  let svc: WeeklyService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    svc = TestBed.inject(WeeklyService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists weekly posts', () => {
    svc.getAll().subscribe(res => expect(res.length).toBe(1));
    const req = http.expectOne('/api/weekly-posts');
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 1, title: 'W1', fileName: 'a.pgn', fileSize: 10,
      scheduledAt: '2026-06-08T19:00:00', createdAt: '', updatedAt: '' }]);
  });

  it('loads a post detail with pgn', () => {
    svc.getById(5).subscribe(d => expect(d.pgnContent).toContain('1. e4'));
    const req = http.expectOne('/api/weekly-posts/5');
    expect(req.request.method).toBe('GET');
    req.flush({ id: 5, title: 'W', fileName: 'a.pgn', fileSize: 10,
      scheduledAt: '2026-06-08T19:00:00', createdAt: '', updatedAt: '', pgnContent: '1. e4 e5 *' });
  });

  it('creates a post via multipart with scheduledAt + title', () => {
    const file = new File(['[Event "x"]\n\n1. e4 *'], 'g.pgn', { type: 'application/octet-stream' });
    svc.create(file, '2026-06-08T19:00:00', 'Titel').subscribe();
    const req = http.expectOne('/api/admin/weekly-posts');
    expect(req.request.method).toBe('POST');
    const body = req.request.body as FormData;
    expect(body.get('scheduledAt')).toBe('2026-06-08T19:00:00');
    expect(body.get('title')).toBe('Titel');
    expect(body.get('file')).toBeTruthy();
    req.flush({ id: 1, title: 'Titel', fileName: 'g.pgn', fileSize: 5,
      scheduledAt: '2026-06-08T19:00:00', createdAt: '', updatedAt: '' });
  });

  it('updates a post', () => {
    svc.update(3, { title: 'Neu', scheduledAt: '2026-06-15T19:00:00' }).subscribe();
    const req = http.expectOne('/api/admin/weekly-posts/3');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ title: 'Neu', scheduledAt: '2026-06-15T19:00:00' });
    req.flush({ id: 3, title: 'Neu', fileName: 'a.pgn', fileSize: 10,
      scheduledAt: '2026-06-15T19:00:00', createdAt: '', updatedAt: '' });
  });

  it('loads the play sequence (puzzles)', () => {
    svc.getPlay(7).subscribe(p => {
      expect(p.title).toBe('Woche 1');
      expect(p.puzzles.length).toBe(2);
    });
    const req = http.expectOne('/api/weekly-posts/7/puzzles');
    expect(req.request.method).toBe('GET');
    req.flush({ id: 7, title: 'Woche 1', puzzles: [
      { id: 0, lineId: 'a:1', bookFileName: 'a.pgn', round: '1', fen: '8/8/8/8/8/8/8/K6k w - - 0 1', moves: 'a1a2', startPly: -1 },
      { id: 1, lineId: 'a:2', bookFileName: 'a.pgn', round: '2', fen: '8/8/8/8/8/8/8/K6k w - - 0 1', moves: 'a1b1', startPly: -1 },
    ] });
  });

  it('deletes a post', () => {
    svc.delete(3).subscribe();
    const req = http.expectOne('/api/admin/weekly-posts/3');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
