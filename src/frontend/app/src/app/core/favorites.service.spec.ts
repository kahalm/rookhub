import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FavoritesService } from './favorites.service';

describe('FavoritesService', () => {
  let svc: FavoritesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    svc = TestBed.inject(FavoritesService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('list uses the take param', () => {
    svc.list(50).subscribe();
    http.expectOne('/api/favorites?take=50').flush([]);
  });

  it('count unwraps { count }', () => {
    let out: number | undefined;
    svc.count().subscribe(c => out = c);
    http.expectOne('/api/favorites/count').flush({ count: 7 });
    expect(out).toBe(7);
  });

  it('contains unwraps { favorited } and passes source+puzzleId', () => {
    let out: boolean | undefined;
    svc.contains('book', 42).subscribe(v => out = v);
    http.expectOne('/api/favorites/contains?source=book&puzzleId=42').flush({ favorited: true });
    expect(out).toBeTrue();
  });

  it('add POSTs the body and unwraps { favorited }', () => {
    let out: boolean | undefined;
    svc.add('standard', 9).subscribe(v => out = v);
    const req = http.expectOne('/api/favorites');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ source: 'standard', puzzleId: 9 });
    req.flush({ favorited: true });
    expect(out).toBeTrue();
  });

  it('remove DELETEs with query params', () => {
    let out: boolean | undefined;
    svc.remove('book', 5).subscribe(v => out = v);
    const req = http.expectOne('/api/favorites?source=book&puzzleId=5');
    expect(req.request.method).toBe('DELETE');
    req.flush({ favorited: false });
    expect(out).toBeFalse();
  });
});
