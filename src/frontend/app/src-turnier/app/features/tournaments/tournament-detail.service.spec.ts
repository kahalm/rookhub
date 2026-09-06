import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TournamentDetailService } from './tournament-detail.service';

describe('TournamentDetailService', () => {
  let service: TournamentDetailService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [TournamentDetailService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TournamentDetailService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getPairings hängt die Runde an die URL', () => {
    service.getPairings('42', 3).subscribe();
    const req = httpMock.expectOne('/api/tournaments/42/pairings?round=3');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('subscribe POSTet crawlerTournamentId + tournamentName', () => {
    service.subscribe('42', 'Open 2026').subscribe();
    const req = httpMock.expectOne('/api/subscriptions');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ crawlerTournamentId: '42', tournamentName: 'Open 2026' });
    req.flush({});
  });

  it('startCrawl schickt jobType Full', () => {
    service.startCrawl('cr-1').subscribe();
    const req = httpMock.expectOne('/api/tournaments/crawl');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ chessResultsId: 'cr-1', jobType: 'Full' });
    req.flush({});
  });

  it('saveFavoriteSettings PUTet das showFavoritesOnly-Flag', () => {
    service.saveFavoriteSettings('42', true).subscribe();
    const req = httpMock.expectOne('/api/tournament-favorites/settings/42');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ showFavoritesOnly: true });
    req.flush({});
  });

  it('removePlayerFavorite DELETEt die by-player-Route', () => {
    service.removePlayerFavorite('42', 7).subscribe();
    const req = httpMock.expectOne('/api/tournament-favorites/by-player/42/7');
    expect(req.request.method).toBe('DELETE');
    req.flush({});
  });

  it('stopMonitor DELETEt den Monitor', () => {
    service.stopMonitor('42').subscribe();
    const req = httpMock.expectOne('/api/tournament-monitors/42');
    expect(req.request.method).toBe('DELETE');
    req.flush({});
  });
});
