import {
  takeFromPool, takeNearestFromPool, autoFasttrackThresholds, fasttrackSteps, buildBatchThemes,
  chainRatingAt, firstRunRatingAt, buildChainWindows, CHAIN_T1_INDEX, CHAIN_T2_INDEX, ENDLESS_CHAIN_BLOCK,
  FIRST_RUN_ANCHOR1_INDEX, FIRST_RUN_ANCHOR2_INDEX
} from './endless-prefetch.util';
import { PuzzleDto } from './puzzle.service';

function p(id: number, rating: number): PuzzleDto {
  return { id, lichessId: 'x' + id, fen: '', moves: '', rating };
}

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

  it('T1 = Ø erster Fehler, T2 = Ø Max-Rating der letzten 5 Runs', () => {
    const hist = [
      { mistakeAtRatings: [1300], maxRating: 1700 },
      { mistakeAtRatings: [1300], maxRating: 1700 },
    ];
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

describe('chainRatingAt (Gauntlet-Kurve)', () => {
  const start = 700, t1 = 1100, t2 = 1800;

  it('trifft die Anker: Start bei 0, T1 bei ~5, T2 bei ~20', () => {
    expect(chainRatingAt(0, start, t1, t2)).toBe(start);
    expect(chainRatingAt(CHAIN_T1_INDEX, start, t1, t2)).toBe(t1);
    expect(chainRatingAt(CHAIN_T2_INDEX, start, t1, t2)).toBe(t2);
  });

  it('steigt monoton und konkav (schnell, dann abflachend)', () => {
    let prev = -1;
    const deltas: number[] = [];
    for (let n = 0; n <= CHAIN_T2_INDEX; n++) {
      const r = chainRatingAt(n, start, t1, t2);
      expect(r).toBeGreaterThanOrEqual(prev);
      if (n > 0) deltas.push(r - prev);
      prev = r;
    }
    // früher Schritt (Puzzle 1) deutlich größer als später (Richtung T2) → konkav
    expect(deltas[0]).toBeGreaterThan(deltas[deltas.length - 1]);
  });

  it('drückt nach T2 nur noch leicht höher', () => {
    expect(chainRatingAt(CHAIN_T2_INDEX + 2, start, t1, t2)).toBeGreaterThan(t2);
    expect(chainRatingAt(CHAIN_T2_INDEX + 2, start, t1, t2)).toBeLessThan(t2 + 100);
  });
});

describe('firstRunRatingAt (steile Erst-Lauf-Kurve)', () => {
  it('trifft die Anker exakt: 2000 nach 15, 3000 nach 30 Puzzles', () => {
    expect(firstRunRatingAt(0, 700)).toBe(700);
    expect(firstRunRatingAt(FIRST_RUN_ANCHOR1_INDEX, 700)).toBe(2000);
    expect(firstRunRatingAt(FIRST_RUN_ANCHOR2_INDEX, 700)).toBe(3000);
  });

  it('steigt monoton und drückt nach dem 2. Anker nur noch leicht höher', () => {
    let prev = -1;
    for (let n = 0; n <= FIRST_RUN_ANCHOR2_INDEX; n++) {
      const r = firstRunRatingAt(n, 700);
      expect(r).toBeGreaterThanOrEqual(prev);
      prev = r;
    }
    expect(firstRunRatingAt(FIRST_RUN_ANCHOR2_INDEX + 2, 700)).toBeGreaterThan(3000);
    expect(firstRunRatingAt(FIRST_RUN_ANCHOR2_INDEX + 2, 700)).toBeLessThan(3100);
  });

  it('chainRatingAt(firstRun=true) nutzt die Erst-Lauf-Kurve statt der adaptiven', () => {
    expect(chainRatingAt(FIRST_RUN_ANCHOR1_INDEX, 700, 1100, 1800, true)).toBe(2000);
    expect(chainRatingAt(FIRST_RUN_ANCHOR2_INDEX, 700, 1100, 1800, true)).toBe(3000);
    // ohne firstRun bleibt die adaptive Kurve deutlich niedriger
    expect(chainRatingAt(FIRST_RUN_ANCHOR1_INDEX, 700, 1100, 1800, false)).toBeLessThan(2000);
  });

  it('buildChainWindows(firstRun=true) zentriert die Fenster auf der steilen Kurve', () => {
    const w = buildChainWindows(700, 1100, 1800, 4000, ENDLESS_CHAIN_BLOCK, 0, true);
    expect(w[FIRST_RUN_ANCHOR1_INDEX]).toEqual({ minRating: 1980, maxRating: 2020 }); // 2000 ±20
    expect(w[0]).toEqual({ minRating: 680, maxRating: 720 });                          // Start 700 ±20
  });
});

describe('buildBatchThemes', () => {
  it('kein Filter → themesAny undefined', () => {
    expect(buildBatchThemes(false, [], '')).toEqual({ themesAny: undefined });
    expect(buildBatchThemes(false, ['fork'], '   ')).toEqual({ themesAny: undefined });
  });

  it('manuelles Themenfeld ODER-verknüpft (getrimmt), wenn worstTags aus', () => {
    expect(buildBatchThemes(false, ['ignored'], '  fork pin ')).toEqual({ themesAny: 'fork pin' });
  });

  it('worstTags hat Vorrang vor dem manuellen Feld (join der worstThemes)', () => {
    expect(buildBatchThemes(true, ['pin', 'fork'], 'endgame')).toEqual({ themesAny: 'pin fork' });
  });

  it('worstTags aktiv aber keine worstThemes → fällt auf das manuelle Feld zurück', () => {
    expect(buildBatchThemes(true, [], 'endgame')).toEqual({ themesAny: 'endgame' });
  });
});

describe('buildChainWindows', () => {
  it('liefert einen Block zentrierter Fenster + respektiert startIndex und ratingMax', () => {
    const w = buildChainWindows(700, 1100, 1800, 3000);
    expect(w.length).toBe(ENDLESS_CHAIN_BLOCK);
    expect(w[0]).toEqual({ minRating: 680, maxRating: 720 });          // Start 700 ±20
    expect(w[CHAIN_T2_INDEX]).toEqual({ minRating: 1780, maxRating: 1820 }); // T2 1800 ±20

    const next = buildChainWindows(700, 1100, 1800, 3000, ENDLESS_CHAIN_BLOCK, ENDLESS_CHAIN_BLOCK);
    expect(next[0].minRating).toBeGreaterThan(w[w.length - 1].minRating);  // Folgeblock setzt höher an

    const capped = buildChainWindows(2950, 3200, 3500, 3000);
    expect(capped.every(x => x.minRating <= 3000)).toBeTrue();             // auf ratingMax geklemmt
  });
});
