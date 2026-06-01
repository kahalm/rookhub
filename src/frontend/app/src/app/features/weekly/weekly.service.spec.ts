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

  it('deletes a post', () => {
    svc.delete(3).subscribe();
    const req = http.expectOne('/api/admin/weekly-posts/3');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
