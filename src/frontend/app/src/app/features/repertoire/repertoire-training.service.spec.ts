import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RepertoireTrainingService, ReviewCardRequest } from './repertoire-training.service';

describe('RepertoireTrainingService', () => {
  let service: RepertoireTrainingService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [RepertoireTrainingService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RepertoireTrainingService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getPgn lädt das kombinierte PGN als Text', () => {
    service.getPgn(12).subscribe();
    const req = httpMock.expectOne('/api/repertoires/12/pgn');
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('text');
    req.flush('1. e4 *');
  });

  it('getCards lädt die SM-2-Kartenzustände', () => {
    service.getCards(12).subscribe();
    const req = httpMock.expectOne('/api/repertoires/12/training/cards');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('review POSTet die Bewertung an die training/review-Route', () => {
    const body: ReviewCardRequest = { cardKey: 'k1', expectedMove: 'e4', grade: 2 };
    service.review(12, body).subscribe();
    const req = httpMock.expectOne('/api/repertoires/12/training/review');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });
});
