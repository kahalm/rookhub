import { TestBed } from '@angular/core/testing';
import { StockfishResult, StockfishService } from '../puzzles/stockfish.service';
import { Mistake } from './mistakes.util';
import { MistakeJudgeService } from './mistake-judge.service';

describe('MistakeJudgeService', () => {
  const NACH_E4_E5 = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2';
  const mistake = { fenBefore: NACH_E4_E5, bestUci: 'g1f3', white: true } as Mistake;

  function setup(scores: Record<string, { cp?: number; mate?: number } | undefined>) {
    const calls: { fen: string; depth: number }[] = [];
    const fake = {
      getBestMove: (fen: string, depth: number): Promise<StockfishResult> => {
        calls.push({ fen, depth });
        const key = Object.keys(scores).find(k => fen.startsWith(k));
        return Promise.resolve({ move: 'a7a6', eval: '', score: key ? scores[key] : undefined });
      },
    };
    TestBed.configureTestingModule({ providers: [{ provide: StockfishService, useValue: fake }] });
    return { judge: TestBed.inject(MistakeJudgeService), calls };
  }

  it('rechnet Bestzug und eigenen Zug in DERSELBEN Tiefe und vergleicht aus Sicht des Ziehenden', async () => {
    // nach Nf3 +30, nach Nc3 +25 → 0,5 Punkte dahinter
    const { judge, calls } = setup({
      'rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2': { cp: 30 },
      'rnbqkbnr/pppp1ppp/8/4p3/4P3/2N5': { cp: 25 },
    });

    const ok = await judge.judge(mistake, 'rnbqkbnr/pppp1ppp/8/4p3/4P3/2N5/PPPP1PPP/R1BQKBNR b KQkq - 1 2');

    expect(ok).toBeTrue();
    expect(calls.length).toBe(2);
    expect(calls.every(c => c.depth === MistakeJudgeService.DEPTH)).toBeTrue();
  });

  it('ein deutlich schlechterer Zug ist nicht gleichwertig', async () => {
    const { judge } = setup({
      'rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2': { cp: 30 },
      'rnbqkbnr/pppp1ppp/8/4p3/4P3/P7': { cp: -40 },
    });

    expect(await judge.judge(mistake, 'rnbqkbnr/pppp1ppp/8/4p3/4P3/P7/1PPP1PPP/RNBQKBNR b KQkq - 0 2')).toBeFalse();
  });

  it('ohne Bewertung der Engine: nicht pruefbar statt geraten', async () => {
    const { judge } = setup({ 'rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2': { cp: 30 } });

    expect(await judge.judge(mistake, 'rnbqkbnr/pppp1ppp/8/4p3/4P3/P7/1PPP1PPP/RNBQKBNR b KQkq - 0 2')).toBeNull();
  });

  it('nach einem Matt fragt es die Engine gar nicht — sie haette keinen Zug', async () => {
    const vorMatt = { fenBefore: 'rnbqkbnr/pppp1ppp/8/4p3/6P1/5P2/PPPPP2P/RNBQKBNR b KQkq - 0 2', bestUci: 'd8h4', white: false } as Mistake;
    const { judge, calls } = setup({});

    const ok = await judge.judge(vorMatt, 'rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3');

    expect(ok).toBeTrue();
    expect(calls.length).toBe(0);
  });
});
