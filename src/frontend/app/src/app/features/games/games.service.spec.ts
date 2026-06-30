import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { GamesService } from './games.service';

describe('GamesService', () => {
  let service: GamesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [GamesService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(GamesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list hängt das take-Limit an', () => {
    service.list(50).subscribe();
    const req = httpMock.expectOne('/api/games?take=50');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('get lädt eine einzelne Partie', () => {
    service.get(7).subscribe();
    const req = httpMock.expectOne('/api/games/7');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('delete DELETEt die Partie-Route', () => {
    service.delete(3).subscribe();
    const req = httpMock.expectOne('/api/games/3');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('getShared kodiert den Token in der URL', () => {
    service.getShared('a b/c').subscribe();
    const req = httpMock.expectOne('/api/games/shared/a%20b%2Fc');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('shareUrl baut die absolute /g/-Teilen-URL', () => {
    expect(service.shareUrl('tok123')).toBe(`${window.location.origin}/g/tok123`);
  });
});
