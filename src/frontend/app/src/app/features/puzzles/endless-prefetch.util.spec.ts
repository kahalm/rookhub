import {
  computeRunSize, buildRunWindows, takeFromPool, takeNearestFromPool,
  autoFasttrackThresholds, fasttrackSteps, stepForSolved, buildEndlessRunWindows
} from './endless-prefetch.util';
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

describe('takeNearestFromPool', () => {
  it('removes and returns the puzzle whose rating is closest to the center', () => {
    const pool = [p(1, 800), p(2, 1500), p(3, 1180)];
    const got = takeNearestFromPool(pool, 1200);
    expect(got?.id).toBe(3);                       // 1180 ist am nächsten an 1200
    expect(pool.map(x => x.id)).toEqual([1, 2]);   // entfernt
  });

  it('returns null for an empty pool', () => {
    expect(takeNearestFromPool([], 1200)).toBeNull();
  });
});

describe('autoFasttrackThresholds', () => {
  it('uses startElo+400/+800 defaults without mistake history', () => {
    expect(autoFasttrackThresholds({ startElo: 700 }, [])).toEqual({ first: 1100, second: 1500 });
  });

  it('averages recent mistake ratings but keeps the minimum defaults', () => {
    // Mittelwerte (1300 / 1700) liegen über den Defaults (1100/1500) → werden genommen
    const hist = [{ mistakeAtRatings: [1300, 1700] }, { mistakeAtRatings: [1300, 1700] }];
    expect(autoFasttrackThresholds({ startElo: 700 }, hist)).toEqual({ first: 1300, second: 1700 });
  });

  it('never drops below the startElo+400/+800 floor', () => {
    const hist = [{ mistakeAtRatings: [800, 900] }];   // unter Defaults
    expect(autoFasttrackThresholds({ startElo: 700 }, hist)).toEqual({ first: 1100, second: 1500 });
  });
});

describe('fasttrackSteps', () => {
  it('derives per-phase steps from thresholds (min 10)', () => {
    expect(fasttrackSteps(700, 1100, 1500)).toEqual({ phase1Step: 80, phase2Step: 80 });
    expect(fasttrackSteps(700, 720, 740)).toEqual({ phase1Step: 10, phase2Step: 10 });   // geklemmt auf 10
  });
});

describe('stepForSolved', () => {
  const steps = { phase1Step: 80, phase2Step: 50 };
  it('maps solved count to the right phase step', () => {
    expect(stepForSolved(steps, 3)).toBe(80);    // Phase 1 (≤5)
    expect(stepForSolved(steps, 8)).toBe(50);    // Phase 2 (≤10)
    expect(stepForSolved(steps, 15)).toBe(20);   // Phase 3
  });
});

describe('buildEndlessRunWindows', () => {
  it('concatenates windows for the requested number of runs', () => {
    const config = { startElo: 700 };
    const oneRun = buildEndlessRunWindows(config, [], 3000, 1);
    const twoRuns = buildEndlessRunWindows(config, [], 3000, 2);
    expect(oneRun.length).toBeGreaterThan(0);
    expect(twoRuns.length).toBe(oneRun.length * 2);
    expect(twoRuns[0].minRating).toBe(700);   // startet bei startElo
  });

  it('honours config threshold overrides', () => {
    // Override macht Phase 1 sehr steil → erstes Fenster startet trotzdem bei startElo
    const w = buildEndlessRunWindows({ startElo: 700, fasttrackThreshold1: 1200 }, [], 3000, 1);
    expect(w[0]).toEqual({ minRating: 700, maxRating: 740 });
  });
});
