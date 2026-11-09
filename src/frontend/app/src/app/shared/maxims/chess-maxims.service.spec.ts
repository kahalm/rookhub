import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { ChessMaxim, ChessMaximsService, ChessMaximsState } from './chess-maxims.service';

const ONE: ChessMaxim[] = [{ id: 'a', text: 'A.', category: 'K', when: '…', sources: [] }];

/** Codereview F3-003: die Antwort hängt am Aufrufer (accessible) — je Konto laden, Fehler nicht behalten. */
describe('ChessMaximsService (F3-003)', () => {
  let user$: BehaviorSubject<{ userId: number } | null>;
  let svc: ChessMaximsService;
  let http: HttpTestingController;

  beforeEach(() => {
    user$ = new BehaviorSubject<{ userId: number } | null>(null);
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: { currentUser$: user$ } }],
    });
    svc = TestBed.inject(ChessMaximsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lädt nach dem Anmelden und nach dem Kontowechsel neu, nicht bei gleichem Konto', () => {
    const seen: ChessMaxim[][] = [];
    svc.all$.subscribe(l => seen.push(l));
    http.expectOne('/api/maxims').flush(ONE);
    user$.next({ userId: 5 });
    http.expectOne('/api/maxims').flush([]);
    user$.next({ userId: 5 });            // Token-Auffrischung: dasselbe Konto
    http.expectNone('/api/maxims');
    user$.next({ userId: 8 });
    http.expectOne('/api/maxims').flush(ONE);
    user$.next(null);                     // Abmelden
    http.expectOne('/api/maxims').flush(ONE);
    expect(seen.length).toBe(4);
  });

  it('ein Fehler wird nicht behalten: das nächste Abonnieren fragt erneut', () => {
    const first: ChessMaximsState[] = [];
    svc.state$.subscribe(s => first.push(s));
    http.expectOne('/api/maxims').flush('x', { status: 502, statusText: 'Bad Gateway' });
    expect(first).toEqual([undefined, 'error']);

    const second: ChessMaximsState[] = [];
    svc.state$.subscribe(s => second.push(s));
    http.expectOne('/api/maxims').flush(ONE);
    expect(second[second.length - 1]).toEqual(ONE);
  });

  it('all$ liefert bei Fehler eine leere Liste (still, für die (i)-Hinweise)', () => {
    const seen: ChessMaxim[][] = [];
    svc.all$.subscribe(l => seen.push(l));
    http.expectOne('/api/maxims').flush('x', { status: 500, statusText: 'err' });
    expect(seen).toEqual([[]]);
  });
});
