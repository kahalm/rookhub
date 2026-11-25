import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { claimGuestCourseWork } from './guest-work-claim';

/** Ohne Anmeldung Erarbeitetes beim Anmelden ins Konto (1.020.3) — vorher am Gerät, beim nächsten Abmelden weg. */
describe('claimGuestCourseWork', () => {
  const SOLVED = 'rookhub_course_local_solved_5';
  const CALC = 'rookhub_calc_local_9';
  let http: HttpTestingController;
  let client: HttpClient;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
  });
  afterEach(() => { http.verify(); localStorage.removeItem(SOLVED); localStorage.removeItem(CALC); });

  it('übernimmt gelöste Linien und Kalkulationen und räumt das Gerät erst nach Erfolg', () => {
    localStorage.setItem(SOLVED, JSON.stringify([11, 12]));
    localStorage.setItem(CALC, JSON.stringify({ v: 1, entries: {
      '31': { tree: '{"n":1}', updatedAt: null, chosenSan: 'Nf3', chosenUci: 'g1f3', secondsSpent: 40, grade: 2, touchedAt: 1 },
    } }));
    let total = -1;
    claimGuestCourseWork(client).subscribe(n => total = n);
    const solved = http.expectOne('/api/courses/5/claim-local-solved');
    expect(solved.request.body).toEqual({ ids: [11, 12] });
    const calc = http.expectOne('/api/calculations/claim-local');
    expect(calc.request.body).toEqual([{ bookPuzzleId: 31, treeJson: '{"n":1}', chosenSan: 'Nf3', chosenUci: 'g1f3', secondsSpent: 40, grade: 2 }]);
    solved.flush({ claimed: 2 });
    calc.flush({ claimed: 1 });
    expect(total).toBe(3);
    expect(localStorage.getItem(SOLVED)).toBeNull();
    expect(localStorage.getItem(CALC)).toBeNull();
  });

  it('Fehler: der Gerätestand bleibt für den nächsten Versuch', () => {
    localStorage.setItem(SOLVED, JSON.stringify([11]));
    claimGuestCourseWork(client).subscribe();
    http.expectOne('/api/courses/5/claim-local-solved').flush('x', { status: 503, statusText: 'down' });
    expect(localStorage.getItem(SOLVED)).not.toBeNull();
  });

  it('nichts am Gerät: keine Anfrage', () => {
    let total = -1;
    claimGuestCourseWork(client).subscribe(n => total = n);
    expect(total).toBe(0);
  });
});
