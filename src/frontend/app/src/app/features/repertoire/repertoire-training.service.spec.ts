import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RepertoireTrainingService, LineReviewRequest, SrLevel } from './repertoire-training.service';

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

  it('getLineStates lädt die Linien-SR-Zustände', () => {
    service.getLineStates(12).subscribe();
    const req = httpMock.expectOne('/api/repertoires/12/training/lines');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('reviewLine POSTet die Bewertung an die line-review-Route', () => {
    const body: LineReviewRequest = { lineKey: 'l1', label: '1.e4', correct: true };
    service.reviewLine(12, body).subscribe();
    const req = httpMock.expectOne('/api/repertoires/12/training/line-review');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  it('promote POSTet die Linien-Schlüssel', () => {
    service.promote(12, ['a', 'b']).subscribe();
    const req = httpMock.expectOne('/api/repertoires/12/training/promote');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ lineKeys: ['a', 'b'] });
    req.flush({ affected: 2 });
  });

  it('setPaused POSTet lineKeys + paused', () => {
    service.setPaused(12, ['a'], true).subscribe();
    const req = httpMock.expectOne('/api/repertoires/12/training/pause');
    expect(req.request.body).toEqual({ lineKeys: ['a'], paused: true });
    req.flush({ affected: 1 });
  });

  it('makeDue POSTet lineKeys (leer = ganzer Kurs)', () => {
    service.makeDue(12, []).subscribe();
    const req = httpMock.expectOne('/api/repertoires/12/training/make-due');
    expect(req.request.body).toEqual({ lineKeys: [] });
    req.flush({ affected: 5 });
  });

  it('getConfig lädt die effektive Konfiguration', () => {
    service.getConfig(12).subscribe();
    const req = httpMock.expectOne('/api/repertoires/12/training/config');
    expect(req.request.method).toBe('GET');
    req.flush({ effective: [], user: [], repertoire: null, source: 'default' });
  });

  it('setRepertoireConfig PUTtet die Stufen (null löscht den Override)', () => {
    const levels: SrLevel[] = [{ value: 4, unit: 'h' }];
    service.setRepertoireConfig(12, levels).subscribe();
    const req = httpMock.expectOne('/api/repertoires/12/training/config');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ levels });
    req.flush(null);
  });

  it('getUserConfig lädt die globalen Nutzer-Intervalle', () => {
    service.getUserConfig().subscribe();
    const req = httpMock.expectOne('/api/repertoires/training/sr-config');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('reset löscht die Zustände', () => {
    service.reset(12).subscribe();
    const req = httpMock.expectOne('/api/repertoires/12/training/reset');
    expect(req.request.method).toBe('DELETE');
    req.flush({ deleted: 3 });
  });
});
