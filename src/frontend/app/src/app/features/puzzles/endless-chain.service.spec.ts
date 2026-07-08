import { of } from 'rxjs';
import { EndlessChainService, ChainBlockRequest } from './endless-chain.service';
import { buildChainWindows, ENDLESS_CHAIN_BLOCK, CHAIN_T2_INDEX } from './endless-prefetch.util';
import { PuzzleDto } from './puzzle.service';

/** Fake PuzzleService: nur getRandomBatch, als Spy der die Argumente festhält. */
function makeService() {
  const batch: PuzzleDto[] = [{ id: 1, lichessId: 'a', fen: '', moves: '', rating: 700 }];
  const getRandomBatch = jasmine.createSpy('getRandomBatch').and.returnValue(of(batch));
  const puzzles: any = { getRandomBatch };
  return { svc: new EndlessChainService(puzzles), getRandomBatch, batch };
}

const REQ: ChainBlockRequest = {
  startElo: 700, avgFirst: 1100, avgSecond: 1800, ratingMax: 3000,
  startIndex: 0, firstRun: false,
};

describe('EndlessChainService', () => {
  it('chainWindows delegiert an buildChainWindows (identische Fenster, default count)', () => {
    const { svc } = makeService();
    const windows = svc.chainWindows(REQ);
    expect(windows.length).toBe(ENDLESS_CHAIN_BLOCK);
    // Muss exakt der puren Util entsprechen (Start 700 ±20, T2 1800 ±20).
    expect(windows).toEqual(buildChainWindows(700, 1100, 1800, 3000, ENDLESS_CHAIN_BLOCK, 0, false));
    expect(windows[0]).toEqual({ minRating: 680, maxRating: 720 });
    expect(windows[CHAIN_T2_INDEX]).toEqual({ minRating: 1780, maxRating: 1820 });
  });

  it('chainWindows respektiert count/startIndex/firstRun', () => {
    const { svc } = makeService();
    const windows = svc.chainWindows({ ...REQ, count: 5, startIndex: 10, firstRun: true });
    expect(windows).toEqual(buildChainWindows(700, 1100, 1800, 3000, 5, 10, true));
    expect(windows.length).toBe(5);
  });

  it('fetchBatch reicht Fenster + Themen (excludeSolved=false, themesAny) an getRandomBatch durch', () => {
    const { svc, getRandomBatch, batch } = makeService();
    const windows = svc.chainWindows(REQ);
    let got: PuzzleDto[] | undefined;
    svc.fetchBatch(windows, 'fork', 'fork pin').subscribe(p => (got = p));
    expect(getRandomBatch).toHaveBeenCalledWith(windows, 'fork', false, 'fork pin');
    expect(got).toBe(batch);
  });

  it('fetchBlock baut die Fenster aus dem Request und ruft getRandomBatch damit', () => {
    const { svc, getRandomBatch } = makeService();
    svc.fetchBlock(REQ, undefined, undefined).subscribe();
    expect(getRandomBatch).toHaveBeenCalledWith(svc.chainWindows(REQ), undefined, false, undefined);
  });

  it('liest getRandomBatch dynamisch (spätere Neuzuweisung wirkt durch)', () => {
    const puzzles: any = { getRandomBatch: () => of([]) };
    const svc = new EndlessChainService(puzzles);
    const late = jasmine.createSpy('late').and.returnValue(of([{ id: 9 }]));
    puzzles.getRandomBatch = late;   // nach Konstruktion neu gesetzt (wie im Component-Spec)
    svc.fetchBlock(REQ).subscribe();
    expect(late).toHaveBeenCalled();
  });
});
