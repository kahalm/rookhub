import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TrainingVideosService, trainingGameLabel } from './training-videos.service';

describe('TrainingVideosService', () => {
  let svc: TrainingVideosService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    svc = TestBed.inject(TrainingVideosService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('ruft Liste, Stunde, Suche, Kommentare und Fragen-Satz unter /api/training-videos', () => {
    svc.list().subscribe();
    http.expectOne(r => r.method === 'GET' && r.url === '/api/training-videos').flush([]);
    svc.get(5).subscribe();
    http.expectOne('/api/training-videos/5').flush({});
    svc.searchPosition('8/8/8/8/8/8/8/K6k w - - 0 1').subscribe();
    const pos = http.expectOne(r => r.url === '/api/training-videos/search/position');
    expect(pos.request.params.get('fen')).toBe('8/8/8/8/8/8/8/K6k w - - 0 1');
    pos.flush({ hits: [], truncated: false });
    svc.searchText('Läufer opfer').subscribe();
    expect(http.expectOne(r => r.url === '/api/training-videos/search/text').request.params.get('q')).toBe('Läufer opfer');
    svc.gameComments(42).subscribe();
    http.expectOne('/api/training-videos/games/42/comments').flush({ items: [], unmatched: 0 });
    svc.questions(true).subscribe();
    expect(http.expectOne(r => r.url === '/api/training-videos/questions').request.params.get('onlyWrong')).toBe('true');
  });

  it('speichert den Stand per PUT', () => {
    svc.saveProgress(3, 17, 'wrong', 2).subscribe();
    const req = http.expectOne('/api/training-videos/3/segments/17/progress');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ status: 'wrong', attempts: 2 });
    req.flush({ status: 'wrong', attempts: 2, plays: 1, updatedAt: '2026-11-16T10:00:00Z' });
  });

  it('holt den Video-Ausweis einmal, teilt parallele Anfragen und baut die Adresse', () => {
    const got: string[] = [];
    svc.accessToken().subscribe(t => got.push(t));
    svc.accessToken().subscribe(t => got.push(t));
    http.expectOne('/api/training-videos/access-token').flush({ token: 'a/b', expiresAt: new Date(Date.now() + 12 * 3600_000).toISOString() });
    svc.accessToken().subscribe(t => got.push(t));   // aus dem Speicher
    http.expectNone('/api/training-videos/access-token');
    expect(got).toEqual(['a/b', 'a/b', 'a/b']);
    expect(svc.videoUrl(4, 'a/b')).toBe('/api/training-videos/4/video?t=a%2Fb');
  });

  it('holt einen neuen Ausweis, wenn der alte bald abläuft oder vergessen wurde', () => {
    svc.accessToken().subscribe();
    http.expectOne('/api/training-videos/access-token').flush({ token: 'x', expiresAt: new Date(Date.now() + 60_000).toISOString() });
    svc.accessToken().subscribe();
    http.expectOne('/api/training-videos/access-token').flush({ token: 'y', expiresAt: new Date(Date.now() + 12 * 3600_000).toISOString() });
    svc.forgetToken();
    svc.accessToken().subscribe();
    http.expectOne('/api/training-videos/access-token').flush({ token: 'z', expiresAt: new Date(Date.now() + 12 * 3600_000).toISOString() });
  });

  it('trainingGameLabel ersetzt fehlende Namen und PGN-„?“', () => {
    expect(trainingGameLabel({ white: 'Anna', black: 'Ben' }, '–')).toBe('Anna – Ben');
    expect(trainingGameLabel({ white: '?', black: null }, 'Unbekannt')).toBe('Unbekannt – Unbekannt');
  });
});
