import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CourseService } from './course.service';

describe('CourseService', () => {
  let svc: CourseService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    svc = TestBed.inject(CourseService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lädt ein Kapitel als PGN (leerer Name = „ohne Kapitel")', () => {
    svc.downloadChapterPgn(7, 'Kapitel 1').subscribe();
    const req = http.expectOne(r => r.url === '/api/courses/7/chapter-pgn');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('chapter')).toBe('Kapitel 1');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['x']));

    svc.downloadChapterPgn(7, null).subscribe();
    const req2 = http.expectOne(r => r.url === '/api/courses/7/chapter-pgn');
    expect(req2.request.params.get('chapter')).toBe('');
    req2.flush(new Blob(['x']));
  });

  it('lädt eine Linie als PGN', () => {
    svc.downloadLinePgn(7, 16867).subscribe();
    const req = http.expectOne('/api/courses/7/lines/16867/pgn');
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['x']));
  });

  it('lists courses', () => {
    svc.getCourses().subscribe(res => expect(res.length).toBe(1));
    const req = http.expectOne('/api/courses');
    expect(req.request.method).toBe('GET');
    req.flush([{ bookId: 1, fileName: 'a.pgn', displayName: 'A', difficulty: null, rating: null,
      tags: null, description: null, puzzleCount: 5, solvedCount: 2, progressPercent: 40, lastMode: null }]);
  });

  it('requests next with mode + after', () => {
    svc.getNext(7, 'sequential', 42).subscribe();
    const req = http.expectOne(r => r.url === '/api/courses/7/next');
    expect(req.request.params.get('mode')).toBe('sequential');
    expect(req.request.params.get('after')).toBe('42');
    req.flush({ puzzle: null, solvedCount: 0, total: 0, completed: true });
  });

  it('requests next random with exclude', () => {
    svc.getNext(7, 'random', undefined, 99).subscribe();
    const req = http.expectOne(r => r.url === '/api/courses/7/next');
    expect(req.request.params.get('mode')).toBe('random');
    expect(req.request.params.get('exclude')).toBe('99');
    expect(req.request.params.has('after')).toBeFalse();
    req.flush({ puzzle: null, solvedCount: 0, total: 0, completed: true });
  });

  it('records a solved result', () => {
    svc.recordResult(3, 55, true, 'sequential').subscribe();
    const req = http.expectOne('/api/courses/3/results');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ bookPuzzleId: 55, solved: true, mode: 'sequential', timeSeconds: 0, chapterIndex: undefined, hintsUsed: 0, solveMode: undefined });
    req.flush({ bookId: 3, solvedCount: 1, total: 10, progressPercent: 10, completed: false, lastMode: 'sequential' });
  });

  it('sends the elapsed time (for start/solve logging)', () => {
    svc.recordResult(3, 55, true, 'sequential', 42).subscribe();
    const req = http.expectOne('/api/courses/3/results');
    expect(req.request.body).toEqual({ bookPuzzleId: 55, solved: true, mode: 'sequential', timeSeconds: 42, chapterIndex: undefined, hintsUsed: 0, solveMode: undefined });
    req.flush({ bookId: 3, solvedCount: 1, total: 10, progressPercent: 10, completed: false, lastMode: 'sequential' });
  });

  it('reicht die Spielweise als solveMode durch (mode bleibt die Durchlaufart)', () => {
    svc.recordResult(3, 55, true, 'sequential', 42, undefined, 0, 'easy').subscribe();
    const req = http.expectOne('/api/courses/3/results');
    expect(req.request.body.solveMode).toBe('easy');
    expect(req.request.body.mode).toBe('sequential');
    req.flush({ bookId: 3, solvedCount: 1, total: 10, progressPercent: 10, completed: false, lastMode: 'sequential' });
  });

  it('resets a course', () => {
    svc.reset(3).subscribe();
    const req = http.expectOne('/api/courses/3/reset');
    expect(req.request.method).toBe('POST');
    req.flush({ bookId: 3, solvedCount: 0, total: 10, progressPercent: 0, completed: false, lastMode: null });
  });

  it('checks course access', () => {
    let hasAccess: boolean | undefined;
    svc.checkAccess().subscribe(r => hasAccess = r.hasAccess);
    const req = http.expectOne('/api/courses/access');
    expect(req.request.method).toBe('GET');
    req.flush({ hasAccess: true });
    expect(hasAccess).toBeTrue();
  });

  it('sets course themes (PUT) and returns effective keys', () => {
    let result: string[] | undefined;
    svc.setCourseThemes(7, ['tactics', 'endgame']).subscribe(r => result = r.themes);
    const req = http.expectOne('/api/courses/7/themes');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ themes: ['tactics', 'endgame'] });
    req.flush({ themes: ['tactics', 'endgame'] });
    expect(result).toEqual(['tactics', 'endgame']);
  });

  it('löst den öffentlichen Kurz-Alias samt Modus-Auskunft auf', () => {
    let got: { bookId: number; isCalculation?: boolean } | undefined;
    svc.resolvePublicSlug('noel').subscribe(res => (got = res));
    const req = http.expectOne('/api/courses/by-slug/noel');
    expect(req.request.method).toBe('GET');
    req.flush({ bookId: 9, isCalculation: true });
    expect(got).toEqual({ bookId: 9, isCalculation: true });
  });

  it('löst die Kapitel-Kurz-URL auf und kodiert den Kapitelnamen', () => {
    svc.resolvePublicSlugChapter('noel', 'KW 46/47').subscribe();
    const req = http.expectOne('/api/courses/by-slug/noel/KW%2046%2F47');
    expect(req.request.method).toBe('GET');
    req.flush({ bookId: 9, isCalculation: true, chapter: 'KW 46/47', chapterIndex: null });
  });

  // ---- Kurs-Übersetzung (Stufe C): `?lang=` an allen Kurs-Endpunkten aus Stufe A ----------------

  it('schickt die gewählte Sprache als lang mit (Kurs-Einstiege)', () => {
    svc.getNext(7, 'random', undefined, 3, 2, 'de').subscribe();
    const next = http.expectOne(r => r.url === '/api/courses/7/next');
    expect(next.request.params.get('lang')).toBe('de');
    expect(next.request.params.get('chapterIndex')).toBe('2');
    next.flush({ puzzle: null, solvedCount: 0, total: 0, completed: true });

    svc.getBookPuzzles(7, 'fr').subscribe();
    const all = http.expectOne(r => r.url === '/api/courses/7/puzzles');
    expect(all.request.params.get('lang')).toBe('fr');
    all.flush([]);

    svc.getPublicCourse(7, 0, 300, 'hr').subscribe();
    const pub = http.expectOne(r => r.url === '/api/courses/7/public');
    expect(pub.request.params.get('lang')).toBe('hr');
    expect(pub.request.params.get('take')).toBe('300');
    pub.flush([]);

    svc.getChapters(7, 'de').subscribe();
    const ch = http.expectOne(r => r.url === '/api/courses/7/chapters');
    expect(ch.request.params.get('lang')).toBe('de');
    ch.flush([]);

    svc.getDetail(7, 'de').subscribe();
    const det = http.expectOne(r => r.url === '/api/courses/7');
    expect(det.request.params.get('lang')).toBe('de');
    det.flush({});
  });

  it('ohne Sprache kein lang-Parameter — der Server liefert dann exakt das Original', () => {
    svc.getBookPuzzles(7).subscribe();
    const req = http.expectOne('/api/courses/7/puzzles');
    expect(req.request.params.has('lang')).toBeFalse();
    req.flush([]);
    svc.getNext(7, 'sequential', undefined, undefined, undefined, null).subscribe();
    const next = http.expectOne(r => r.url === '/api/courses/7/next');
    expect(next.request.params.has('lang')).toBeFalse();
    next.flush({ puzzle: null, solvedCount: 0, total: 0, completed: true });
  });

  it('Übersetzungen: lesen, anfordern (mit Status), zurückziehen', () => {
    svc.getTranslations(7).subscribe();
    const get = http.expectOne('/api/courses/7/translations');
    expect(get.request.method).toBe('GET');
    get.flush({ sourceLanguage: 'en', languages: [], jobs: [], available: true, canRequest: true });

    let status = 0;
    svc.requestTranslation(7, 'de').subscribe(res => status = res.status);
    const post = http.expectOne('/api/courses/7/translations');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ language: 'de' });
    post.flush({ id: 1 }, { status: 202, statusText: 'Accepted' });
    expect(status).toBe(202);

    svc.withdrawTranslation(7, 12).subscribe();
    const del = http.expectOne('/api/courses/7/translations/12');
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });
  });
});
