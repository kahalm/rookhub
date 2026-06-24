import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RepertoireService } from './repertoire.service';

describe('RepertoireService', () => {
  let service: RepertoireService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [RepertoireService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RepertoireService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list GETs /api/repertoires', () => {
    service.list().subscribe();
    const req = httpMock.expectOne('/api/repertoires');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('create POSTs the dto', () => {
    service.create({ name: 'Sicilian' }).subscribe();
    const req = httpMock.expectOne('/api/repertoires');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Sicilian' });
    req.flush({});
  });

  it('update PUTs to the id route', () => {
    service.update(7, { name: 'X' }).subscribe();
    const req = httpMock.expectOne('/api/repertoires/7');
    expect(req.request.method).toBe('PUT');
    req.flush({});
  });

  it('remove DELETEs the id route', () => {
    service.remove(9).subscribe();
    const req = httpMock.expectOne('/api/repertoires/9');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('downloadPgn requests a blob from the pgn route', () => {
    service.downloadPgn(3).subscribe();
    const req = httpMock.expectOne('/api/repertoires/3/pgn');
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['1. e4 *']));
  });
});
