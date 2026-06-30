import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { LeaderboardService } from './leaderboard.service';

describe('LeaderboardService', () => {
  let service: LeaderboardService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [LeaderboardService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(LeaderboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('get übergibt period/top/around als Query-Parameter', () => {
    service.get('weekly', 10, 3).subscribe();
    const req = httpMock.expectOne(r => r.url === '/api/leaderboards');
    expect(req.request.params.get('period')).toBe('weekly');
    expect(req.request.params.get('top')).toBe('10');
    expect(req.request.params.get('around')).toBe('3');
    req.flush({ period: 'weekly', puzzles: [], endlessRuns: [], courseLines: [], dailyPuzzles: [] });
  });

  it('get nutzt die Default-Werte top=5 / around=2', () => {
    service.get('alltime').subscribe();
    const req = httpMock.expectOne(r => r.url === '/api/leaderboards');
    expect(req.request.params.get('top')).toBe('5');
    expect(req.request.params.get('around')).toBe('2');
    req.flush({ period: 'alltime', puzzles: [], endlessRuns: [], courseLines: [], dailyPuzzles: [] });
  });
});
