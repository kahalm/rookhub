import { TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { MoveComparisonComponent, arrowOf, play } from './move-comparison.component';
import { numberedLine } from './move-comparison.service';

/** 1.e4 d5 — Weiß am Zug: exd5 (bester) gegen Nc3. */
const SCANDI = 'rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2';

const comparison = (extra: object = {}) => ({
  id: 7, fen: SCANDI, title: null, depth: 22, status: 'done', error: null, whiteToMove: true, bestUci: 'e4d5',
  language: 'de', explanationsAvailable: true, pending: 0, total: 4, createdAt: '2026-09-29T18:00:00Z', finishedAt: null,
  candidates: [
    { uci: 'e4d5', san: 'exd5', state: 'done', depth: 22, evalText: '+0.45', isBest: true, explanation: null, tests: [],
      replies: [{ uci: 'd8d5', san: 'Qxd5', evalText: '+0.45', line: ['Qxd5', 'Nc3', 'Qa5'] }] },
    { uci: 'b1c3', san: 'Nc3', state: 'done', depth: 22, evalText: '+0.20', isBest: false,
      explanation: 'Nach Sc3 nimmt dein Gegner mit dxe4 den Bauern.',
      replies: [
        { uci: 'd5e4', san: 'dxe4', evalText: '+0.20', line: ['dxe4', 'Nxe4'] },
        { uci: 'g8f6', san: 'Nf6', evalText: '+0.35', line: ['Nf6', 'e5'] },
      ],
      tests: [
        { replyUci: 'd5e4', replySan: 'dxe4', state: 'illegal', depth: 0, evalText: null, line: [] },
        { replyUci: 'g8f6', replySan: 'Nf6', state: 'done', depth: 22, evalText: '+0.70', line: ['c4', 'c6'] },
      ] },
  ],
  ...extra,
});

describe('MoveComparisonComponent', () => {
  function make() {
    TestBed.configureTestingModule({
      imports: [MoveComparisonComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: '7' })) } }],
    });
    const fixture = TestBed.createComponent(MoveComparisonComponent);
    fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }

  it('helpers play moves, draw arrows and number lines', () => {
    expect(play(SCANDI, ['e4d5', 'Qxd5'])?.last).toEqual(['d8', 'd5']);
    expect(play(SCANDI, ['e4d5', 'dxe4'])).toBeNull();
    expect(arrowOf(SCANDI, 'Nc3', 'green')).toEqual({ from: 'b1', to: 'c3', brush: 'green' });
    expect(numberedLine(SCANDI, ['exd5', 'Qxd5', 'Nc3'])).toBe('2.exd5 Qxd5 3.Nc3');
    expect(numberedLine(play(SCANDI, ['b1c3'])!.fen, ['dxe4', 'Nxe4'])).toBe('2...dxe4 3.Nxe4');
  });

  it('shows the ranking, the explanation and why the reply fails against the best move', fakeAsync(() => {
    const { fixture, c, http } = make();
    http.expectOne('/api/move-comparisons/7').flush(comparison());
    http.expectOne('/api/move-comparisons').flush([]);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('moveCompare.page.best');
    expect(text).toContain('Nach Sc3 nimmt dein Gegner mit dxe4 den Bauern.');
    expect(text).toContain('moveCompare.page.notPossible');
    expect(text).toContain('3.c4 c6');
    expect(c.weaker().map(w => w.san)).toEqual(['Nc3']);
    // Ohne Auswahl: je Kandidat ein Pfeil, der beste grün
    expect(c.boardArrows()).toEqual([
      { from: 'e4', to: 'd5', brush: 'green' }, { from: 'b1', to: 'c3', brush: 'paleBlue' },
    ]);

    // Klick auf „Nf6 gegen exd5": Stellung nach 2.exd5 Nf6, Pfeil für die eigene Erwiderung c4
    const best = c.best()!;
    c.showTest(best, best === c.data()!.candidates[0] ? c.data()!.candidates[1].tests[1] : best.tests[0]);
    expect(c.boardFen()).toBe(play(SCANDI, ['e4d5', 'g8f6'])!.fen);
    expect(c.boardArrows()).toEqual([{ from: 'c2', to: 'c4', brush: 'green' }]);
    c.reset();
    expect(c.boardFen()).toBe(SCANDI);

    // Fertig: kein weiteres Nachfragen
    tick(10_000);
    http.verify();
    discardPeriodicTasks();
  }));

  it('polls while the comparison is running and stops once it is done', fakeAsync(() => {
    const { fixture, c, http } = make();
    http.expectOne('/api/move-comparisons/7').flush(comparison({ status: 'candidates', bestUci: null, pending: 2 }));
    http.expectOne('/api/move-comparisons').flush([]);
    fixture.detectChanges();
    expect(c.running()).toBeTrue();
    expect(c.weaker()).toEqual([]);

    tick(3_000);
    http.expectOne('/api/move-comparisons/7').flush(comparison());
    http.expectOne('/api/move-comparisons').flush([]);   // fertig geworden → Liste frisch
    fixture.detectChanges();
    expect(c.running()).toBeFalse();
    tick(9_000);
    http.verify();
    discardPeriodicTasks();
  }));

  it('says so when the comparison is gone', fakeAsync(() => {
    const { fixture, c, http } = make();
    http.expectOne('/api/move-comparisons/7').flush(null, { status: 404, statusText: 'Not Found' });
    http.expectOne('/api/move-comparisons').flush([]);
    fixture.detectChanges();
    expect(c.notFound()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('moveCompare.page.notFound');
    discardPeriodicTasks();
  }));
});
