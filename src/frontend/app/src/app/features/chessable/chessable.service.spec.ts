import { TestBed } from '@angular/core/testing';
import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChessableService } from './chessable.service';

describe('ChessableService', () => {
  let service: ChessableService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [ChessableService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ChessableService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getUserCoursesAdmin adds refresh=true only when requested', () => {
    const url = '/api/chessable/admin/users/42/courses';
    service.getUserCoursesAdmin(42).subscribe();
    const plain = httpMock.expectOne((r: HttpRequest<unknown>) => r.url === url);
    expect(plain.request.params.has('refresh')).toBeFalse();
    plain.flush({ courses: [], cachedAt: null });

    service.getUserCoursesAdmin(42, true).subscribe();
    const refreshed = httpMock.expectOne((r: HttpRequest<unknown>) => r.url === url);
    expect(refreshed.request.params.get('refresh')).toBe('true');
    refreshed.flush({ courses: [], cachedAt: null });
  });

  it('importForUserAdmin targets the admin user route', () => {
    service.importForUserAdmin(42, 'bid9', 'repertoire', 'Rep').subscribe();
    const req = httpMock.expectOne('/api/chessable/admin/users/42/import/bid9');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ target: 'repertoire', name: 'Rep' });
    req.flush({ id: 2 });
  });

  it('admin active-imports endpoint is wired correctly', () => {
    service.getActiveImportsAdmin().subscribe();
    httpMock.expectOne('/api/chessable/admin/active').flush([]);
  });

  it('cancelImportAdmin POSTs to the admin cancel route (works with the Chessable switch off)', () => {
    service.cancelImportAdmin(7).subscribe();
    const req = httpMock.expectOne('/api/chessable/admin/imports/7/cancel');
    expect(req.request.method).toBe('POST');
    req.flush({ id: 7, status: 'cancelled' });
  });

  it('testUser POSTs to the admin per-user test route', () => {
    service.testUser(42).subscribe();
    const req = httpMock.expectOne('/api/chessable/admin/users/42/test');
    expect(req.request.method).toBe('POST');
    req.flush({ uid: 'u', courseCount: 3 });
  });
});
