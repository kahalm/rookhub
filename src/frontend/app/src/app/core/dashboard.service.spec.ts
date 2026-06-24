import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DashboardService } from './dashboard.service';

describe('DashboardService', () => {
  let service: DashboardService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [DashboardService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DashboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getRepertoires GETs /api/repertoires', () => {
    service.getRepertoires().subscribe();
    expect(httpMock.expectOne('/api/repertoires').request.method).toBe('GET');
  });

  it('getSubscriptions GETs /api/subscriptions', () => {
    service.getSubscriptions().subscribe();
    expect(httpMock.expectOne('/api/subscriptions').request.method).toBe('GET');
  });

  it('getFriends GETs /api/friends', () => {
    service.getFriends().subscribe();
    expect(httpMock.expectOne('/api/friends').request.method).toBe('GET');
  });

  it('getPuzzleStats GETs /api/puzzles/stats', () => {
    service.getPuzzleStats().subscribe();
    expect(httpMock.expectOne('/api/puzzles/stats').request.method).toBe('GET');
  });
});
