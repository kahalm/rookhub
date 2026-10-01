import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { SnackbarService } from '@rh/core/snackbar.service';
import { OpenTournamentService } from './open-tournament.service';

/**
 * Ein noch nicht geholtes Turnier oeffnen: nachsehen, Holen-Auftrag einreihen, nachfragen.
 * Codereview 2026-09-29, I2-009: lief fuer das Turnier schon ein Auftrag (409), brach das Oeffnen
 * mit „konnte nicht geholt werden" ab, statt mitzuwarten.
 */
describe('OpenTournamentService', () => {
  let service: OpenTournamentService;
  let http: HttpTestingController;
  let navigate: jasmine.Spy;
  let warn: jasmine.Spy;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    });
    service = TestBed.inject(OpenTournamentService);
    http = TestBed.inject(HttpTestingController);
    navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const snackbar = TestBed.inject(SnackbarService);
    warn = spyOn(snackbar, 'warn');
    spyOn(snackbar, 'info');
  });

  afterEach(() => http.verify());

  function notFetchedYet(id: string) {
    http.expectOne(`/api/tournaments/${id}`).flush('nicht geholt', { status: 404, statusText: 'Not Found' });
  }

  it('wartet mit, wenn für das Turnier schon ein Holen-Auftrag läuft (409)', fakeAsync(() => {
    service.open('1457129');
    notFetchedYet('1457129');
    http.expectOne({ method: 'POST', url: '/api/tournaments/crawl' })
      .flush({ error: "A crawl job for '1457129' is already running." }, { status: 409, statusText: 'Conflict' });

    expect(warn).not.toHaveBeenCalled();
    expect(service.opening()).toBe('1457129');

    tick(0);
    http.expectOne('/api/tournaments/1457129').flush({ id: 12, chessResultsId: '1457129', name: 'Open' });

    expect(navigate).toHaveBeenCalledWith(['/tournaments', 12]);
    expect(service.opening()).toBeNull();
  }));

  it('bricht bei einem echten Fehler beim Einreihen ab', fakeAsync(() => {
    service.open('1457129');
    notFetchedYet('1457129');
    http.expectOne({ method: 'POST', url: '/api/tournaments/crawl' })
      .flush('kaputt', { status: 502, statusText: 'Bad Gateway' });

    expect(warn).toHaveBeenCalledWith('turnier.history.fetchFailed');
    expect(service.opening()).toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  }));

  it('öffnet ein schon geholtes Turnier direkt', fakeAsync(() => {
    service.open('1457129');
    http.expectOne('/api/tournaments/1457129').flush({ id: 12, chessResultsId: '1457129', name: 'Open' });

    expect(navigate).toHaveBeenCalledWith(['/tournaments', 12]);
    expect(service.opening()).toBeNull();
  }));
});
