import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TournamentListService } from './tournament-list.service';

describe('TournamentListService', () => {
  let service: TournamentListService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [TournamentListService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TournamentListService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getTournaments requests the list with pageSize', () => {
    service.getTournaments().subscribe();
    const req = httpMock.expectOne('/api/tournaments?pageSize=200');
    expect(req.request.method).toBe('GET');
    req.flush({ items: [], totalCount: 0 });
  });

  it('subscribe POSTs crawlerTournamentId + tournamentName', () => {
    service.subscribe('123', 'Open 2026').subscribe();
    const req = httpMock.expectOne('/api/subscriptions');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ crawlerTournamentId: '123', tournamentName: 'Open 2026' });
    req.flush({ id: 1 });
  });

  it('unsubscribe DELETEs the subscription', () => {
    service.unsubscribe(5).subscribe();
    const req = httpMock.expectOne('/api/subscriptions/5');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('startCrawl POSTs a Full crawl job', () => {
    service.startCrawl('999').subscribe();
    const req = httpMock.expectOne('/api/tournaments/crawl');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ chessResultsId: '999', jobType: 'Full' });
    req.flush({ id: 7, status: 'Queued' });
  });

  it('getCrawlJob GETs the job by id', () => {
    service.getCrawlJob(7).subscribe();
    const req = httpMock.expectOne('/api/tournaments/crawl/7');
    expect(req.request.method).toBe('GET');
    req.flush({ id: 7, status: 'Completed' });
  });
});
