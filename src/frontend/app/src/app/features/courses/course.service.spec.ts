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
    expect(req.request.body).toEqual({ bookPuzzleId: 55, solved: true, mode: 'sequential' });
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
});
