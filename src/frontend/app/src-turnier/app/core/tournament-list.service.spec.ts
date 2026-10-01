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

  // Codereview 2026-09-29, F6-008: „schon gemerkt" (409) ist das Ziel, kein Fehler — sonst
  // meldete ein veralteter Knopf „Merken fehlgeschlagen", obwohl das Turnier gemerkt war.
  it('bookmarkAndImport treats 409 "already subscribed" as success without a second crawl', () => {
    let result: unknown;
    let failed = false;
    service.bookmarkAndImport('123', 'Open 2026').subscribe({
      next: r => (result = r),
      error: () => (failed = true),
    });
    httpMock.expectOne('/api/subscriptions').flush(
      { message: 'Already subscribed to this tournament.' }, { status: 409, statusText: 'Conflict' });

    expect(failed).toBeFalse();
    expect(result).toEqual({ subscription: null, job: null });
    httpMock.expectNone('/api/tournaments/crawl');
  });

  it('bookmarkAndImport still fails on other subscription errors', () => {
    let failed = false;
    service.bookmarkAndImport('123', 'Open 2026').subscribe({ error: () => (failed = true) });
    httpMock.expectOne('/api/subscriptions').flush('kaputt', { status: 500, statusText: 'Server Error' });

    expect(failed).toBeTrue();
    httpMock.expectNone('/api/tournaments/crawl');
  });

  it('bookmarkAndImport subscribes, then queues the crawl', () => {
    let result: unknown;
    service.bookmarkAndImport('123', 'Open 2026').subscribe(r => (result = r));
    httpMock.expectOne('/api/subscriptions').flush({ id: 1 });
    httpMock.expectOne('/api/tournaments/crawl').flush({ id: 7, status: 'Queued' });

    expect(result).toEqual({ subscription: jasmine.objectContaining({ id: 1 }), job: jasmine.objectContaining({ id: 7 }) });
  });

  it('getCrawlJob GETs the job by id', () => {
    service.getCrawlJob(7).subscribe();
    const req = httpMock.expectOne('/api/tournaments/crawl/7');
    expect(req.request.method).toBe('GET');
    req.flush({ id: 7, status: 'Completed' });
  });
});
