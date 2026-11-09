import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MoveComparison } from '../analysis/move-comparison.service';
import {
  ClubMistakeComparison, MistakeComparisonComponent, comparisonExplanation, comparisonPending, comparisonRows,
} from './mistake-comparison.component';

/** 1.e4 e5 2.Qh5?! — Weiß am Zug vor dem 2. Zug: Partiezug Qh5 gegen den besten Zug Nf3. */
const FEN = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2';

function entry(status: MoveComparison['status'] = 'done'): ClubMistakeComparison {
  return {
    ply: 2, playedUci: 'd1h5', analysisBestUci: 'g1f3',
    comparison: {
      id: 41, fen: FEN, title: null, depth: 35, status, error: null, whiteToMove: true, bestUci: 'g1f3', language: 'de',
      explanationsAvailable: true, pending: 0, total: 5, createdAt: '2026-11-08T20:00:00Z', finishedAt: null,
      candidates: [
        { uci: 'g1f3', san: 'Nf3', state: 'done', depth: 35, evalText: '+0.30', isBest: true, explanation: null, tests: [],
          replies: [{ uci: 'b8c6', san: 'Nc6', evalText: '+0.30', line: ['Nc6', 'Bb5', 'a6'] }] },
        { uci: 'd1h5', san: 'Qh5', state: 'done', depth: 35, evalText: '-0.10', isBest: false, tests: [],
          explanation: 'Nach Dh5 entwickelt dein Gegner mit Sc6 und Sf6 mit Tempo.',
          replies: [{ uci: 'b8c6', san: 'Nc6', evalText: '-0.10', line: ['Nc6', 'Bc4', 'g6'] }] },
      ],
    },
  };
}

describe('MistakeComparisonComponent', () => {
  function make(e: ClubMistakeComparison | null) {
    TestBed.configureTestingModule({
      imports: [MistakeComparisonComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(MistakeComparisonComponent);
    fixture.componentRef.setInput('entry', e);
    fixture.componentRef.setInput('clubGameId', 236);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('rows: game move and best move with eval and numbered engine line', () => {
    const rows = comparisonRows(entry(), 'en')!;
    expect(rows.map(r => r.kind)).toEqual(['played', 'best']);
    expect(rows[0]).toEqual(jasmine.objectContaining({ san: '2.Qh5', evalText: '-0.10', isBest: false, line: '2...Nc6 3.Bc4 g6' }));
    expect(rows[1]).toEqual(jasmine.objectContaining({ san: '2.Nf3', evalText: '+0.30', isBest: true, line: '2...Nc6 3.Bb5 a6' }));
    expect(comparisonExplanation(entry().comparison)).toContain('Sc6');
    expect(comparisonPending(entry('waiting').comparison)).toBeTrue();
    expect(comparisonPending(entry('explaining').comparison)).toBeTrue();
    expect(comparisonPending(entry().comparison)).toBeFalse();
  });

  it('done: shows both moves, the explanation and the link to the full comparison', () => {
    const el = make(entry());
    const text = el.textContent ?? '';
    expect(text).toContain('games.mistakes.compare.played');
    expect(text).toContain('2.Qh5');
    expect(text).toContain('-0.10');
    expect(text).toContain('Nach Dh5 entwickelt');
    const link = el.querySelector('a.open') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/analysis/compare/41?clubGame=236');
  });

  it('explanation: numbered moves are buttons that emit the line from the task position', () => {
    const e = entry();
    e.comparison.candidates[1].explanation = 'Nach 2.Dh5 Sc6 3.Lc4 g6 jagt Schwarz die Dame. Besser 2.Sf3.';
    TestBed.configureTestingModule({
      imports: [MistakeComparisonComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(MistakeComparisonComponent);
    fixture.componentRef.setInput('entry', e);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const buttons = Array.from(el.querySelectorAll('.expl button.mv')) as HTMLButtonElement[];
    expect(buttons.map(b => b.textContent)).toEqual(['2.Dh5', 'Sc6', '3.Lc4', 'g6', '2.Sf3']);
    expect(el.querySelector('.expl')!.textContent).toBe('Nach 2.Dh5 Sc6 3.Lc4 g6 jagt Schwarz die Dame. Besser 2.Sf3.');
    let got: { fen: string; line: { san: string }[]; at: number } | undefined;
    fixture.componentInstance.showLine.subscribe(v => got = v);
    buttons[2].click();
    expect(got!.fen).toBe(FEN);
    expect(got!.line.map(m => m.san)).toEqual(['Qh5', 'Nc6', 'Bc4', 'g6']);
    expect(got!.at).toBe(3);
  });

  it('without a game id (share link): no button to the full comparison page', () => {
    TestBed.configureTestingModule({
      imports: [MistakeComparisonComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    });
    const f = TestBed.createComponent(MistakeComparisonComponent);
    f.componentRef.setInput('entry', entry());
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Nach Dh5 entwickelt');
    expect(el.querySelector('a.open')).toBeNull();
  });

  it('pending: one quiet line, nothing else', () => {
    const el = make(entry('candidates'));
    expect(el.textContent).toContain('games.mistakes.compare.pending');
    expect(el.querySelector('.cmp')).toBeNull();
  });

  it('no comparison or failed: renders nothing', () => {
    expect((make(null).textContent ?? '').trim()).toBe('');
    TestBed.resetTestingModule();
    expect((make(entry('failed')).textContent ?? '').trim()).toBe('');
  });
});
