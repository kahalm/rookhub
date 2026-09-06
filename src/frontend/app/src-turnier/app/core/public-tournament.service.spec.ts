import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PublicTournamentService } from './public-tournament.service';

describe('PublicTournamentService', () => {
  let service: PublicTournamentService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PublicTournamentService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PublicTournamentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('builds the tournament/players/teams routes', () => {
    service.getTournament(12).subscribe();
    const t = httpMock.expectOne('/api/tournaments/12');
    expect(t.request.method).toBe('GET');
    t.flush({});
    service.getPlayers(12).subscribe();
    httpMock.expectOne('/api/tournaments/12/players').flush([]);
    service.getTeams(12).subscribe();
    httpMock.expectOne('/api/tournaments/12/teams').flush([]);
  });

  it('getTeam targets the team-by-snr route', () => {
    service.getTeam(12, 4).subscribe();
    httpMock.expectOne('/api/tournaments/12/teams/4').flush({});
  });

  it('getPairings passes the round query param', () => {
    service.getPairings<unknown[]>(12, 3).subscribe();
    httpMock.expectOne('/api/tournaments/12/pairings?round=3').flush([]);
  });
});
