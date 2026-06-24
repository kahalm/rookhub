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

  it('acceptDisclaimer POSTs to /disclaimer', () => {
    service.acceptDisclaimer().subscribe();
    const req = httpMock.expectOne('/api/chessable/disclaimer');
    expect(req.request.method).toBe('POST');
    req.flush({ accepted: true });
  });

  it('saveCredentials POSTs the bearer', () => {
    service.saveCredentials('JWT123').subscribe();
    const req = httpMock.expectOne('/api/chessable/credentials');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ bearer: 'JWT123' });
    req.flush({ hasCredentials: true, maskedBearer: '…123' });
  });

  it('getCourses adds refresh=true only when requested', () => {
    service.getCourses().subscribe();
    const plain = httpMock.expectOne((r: HttpRequest<unknown>) => r.url === '/api/chessable/courses');
    expect(plain.request.params.has('refresh')).toBeFalse();
    plain.flush({ courses: [] });

    service.getCourses(true).subscribe();
    const refreshed = httpMock.expectOne((r: HttpRequest<unknown>) => r.url === '/api/chessable/courses');
    expect(refreshed.request.params.get('refresh')).toBe('true');
    refreshed.flush({ courses: [] });
  });

  it('startImport posts target+name and URL-encodes the bid', () => {
    service.startImport('a/b 1', 'book', 'My Course').subscribe();
    const req = httpMock.expectOne('/api/chessable/courses/a%2Fb%201/import');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ target: 'book', name: 'My Course' });
    req.flush({ id: 1, bid: 'a/b 1', status: 'running' });
  });

  it('importForUserAdmin targets the admin user route', () => {
    service.importForUserAdmin(42, 'bid9', 'repertoire', 'Rep').subscribe();
    const req = httpMock.expectOne('/api/chessable/admin/users/42/import/bid9');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ target: 'repertoire', name: 'Rep' });
    req.flush({ id: 2 });
  });

  it('pause/resume/cancel hit the right import sub-routes', () => {
    service.pauseImport(5).subscribe();
    httpMock.expectOne('/api/chessable/imports/5/pause').flush({});
    service.resumeImport(5).subscribe();
    httpMock.expectOne('/api/chessable/imports/5/resume').flush({});
    service.cancelImport(5).subscribe();
    httpMock.expectOne('/api/chessable/imports/5/cancel').flush({});
  });

  it('admin list endpoints are wired correctly', () => {
    service.getAllImportsAdmin().subscribe();
    httpMock.expectOne('/api/chessable/admin/imports').flush([]);
    service.getActiveImportsAdmin().subscribe();
    httpMock.expectOne('/api/chessable/admin/active').flush([]);
  });
});
