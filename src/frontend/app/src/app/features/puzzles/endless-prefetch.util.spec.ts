import { computeRunSize, buildRunWindows, takeFromPool } from './endless-prefetch.util';
import { PuzzleDto } from './puzzle.service';

function p(id: number, rating: number): PuzzleDto {
  return { id, lichessId: 'x' + id, fen: '', moves: '', rating };
}

describe('computeRunSize', () => {
  it('uses max solved of the last 5 runs + 10', () => {
    const history = [{ totalSolved: 5 }, { totalSolved: 30 }, { totalSolved: 12 }, { totalSolved: 8 }, { totalSolved: 20 }];
    expect(computeRunSize(history)).toBe(40);   // max(30) + 10
  });

  it('only considers the last 5 runs', () => {
    const history = [{ totalSolved: 99 }, { totalSolved: 1 }, { totalSolved: 2 }, { totalSolved: 3 }, { totalSolved: 4 }, { totalSolved: 5 }];
    expect(computeRunSize(history)).toBe(15);   // letzte 5: max(5) + 10; die 99 zählt nicht
  });

  it('defaults to base 20 (→ 30) without history', () => {
    expect(computeRunSize([])).toBe(30);
  });
});

describe('buildRunWindows', () => {
  const step = () => 20;   // konstante Stufe für den Test

  it('builds runSize windows starting at startElo, advancing by step', () => {
    const w = buildRunWindows(800, 3, step, 3000, 40);
    expect(w).toEqual([
      { minRating: 800, maxRating: 840 },
      { minRating: 820, maxRating: 860 },
      { minRating: 840, maxRating: 880 },
    ]);
  });

  it('stops at the max rating', () => {
    const w = buildRunWindows(800, 100, step, 880, 40);
    // 800, 820, 840, 860, 880 → bei 900 (>880) Abbruch
    expect(w.length).toBe(5);
    expect(w[w.length - 1].minRating).toBe(880);
  });

  it('respects a progressive step function', () => {
    const progressive = (n: number) => (n <= 1 ? 10 : 50);
    const w = buildRunWindows(1000, 3, progressive, 3000, 40);
    expect(w.map(x => x.minRating)).toEqual([1000, 1010, 1060]);
  });
});

describe('takeFromPool', () => {
  it('removes and returns a puzzle within the window', () => {
    const pool = [p(1, 800), p(2, 850), p(3, 900)];
    const got = takeFromPool(pool, 840, 880);
    expect(got?.id).toBe(2);
    expect(pool.map(x => x.id)).toEqual([1, 3]);   // entfernt
  });

  it('returns null when nothing fits the window', () => {
    const pool = [p(1, 800)];
    expect(takeFromPool(pool, 1000, 1100)).toBeNull();
    expect(pool.length).toBe(1);
  });
});
