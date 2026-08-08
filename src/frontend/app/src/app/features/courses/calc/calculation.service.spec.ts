import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CalculationService } from './calculation.service';

describe('CalculationService', () => {
  let svc: CalculationService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    svc = TestBed.inject(CalculationService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('holt das eingeloggte Buch vom nutzerbezogenen Endpoint', () => {
    svc.getBook(7).subscribe();
    expect(http.expectOne('/api/calculations/books/7').request.method).toBe('GET');
  });

  /**
   * Der einzige anonym erreichbare Weg in den Modus — und er ist rein LESEND. Ein EIGENER Pfad,
   * kein geöffneter Nutzer-Endpoint: der Server gated hier hart auf „Buch ist öffentlich
   * freigegeben" und liefert ein DTO ohne Nutzer-Felder.
   */
  it('holt das öffentliche Buch von seinem eigenen, anonymen Endpoint', () => {
    let got: { positions: unknown[] } | undefined;
    svc.getPublicBook(7).subscribe(res => (got = res));
    const req = http.expectOne('/api/calculations/books/7/public');
    expect(req.request.method).toBe('GET');
    req.flush({
      bookId: 7, displayName: 'B', isCalculation: true,
      positions: [{ id: 1, round: '1', title: null, chapter: null, fen: '8/8', setupMoves: '', comment: null }],
    });
    expect(got!.positions.length).toBe(1);
    // Es gibt kein Lösungs-Feld im Vertrag — der Modus liefert nie Züge aus.
    expect((got!.positions[0] as Record<string, unknown>)['moves']).toBeUndefined();
  });
});
