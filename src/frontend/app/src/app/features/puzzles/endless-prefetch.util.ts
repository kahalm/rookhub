import { PuzzleDto } from './puzzle.service';

export interface RatingWindow { minRating: number; maxRating: number; }

export const FASTTRACK_SESSION_COUNT = 10;
export const ENDLESS_RATING_WINDOW = 40;

/** Kette (Gauntlet): Indizes der beiden Schwellen + Blockgröße. */
export const CHAIN_T1_INDEX = 5;          // T1 (Ø erster Fehler) nach ~5 Puzzles erreicht
export const CHAIN_T2_INDEX = 20;         // T2 (Ø Max der letzten 5 Runs) nach ~20 Puzzles erreicht
export const CHAIN_FLAT_STEP = 15;        // ab T2: leichtes Plus je Puzzle (oberhalb des eigenen Maximums)
export const ENDLESS_CHAIN_BLOCK = 30;    // Puzzles je generiertem Block

/** Strukturelle Minimalformen (entkoppelt von EndlessConfig/EndlessSession). */
interface FasttrackConfig { startElo: number; fasttrackThreshold1?: number; fasttrackThreshold2?: number; }
interface FasttrackSession { mistakeAtRatings: number[]; maxRating?: number; }

export interface FasttrackSteps { phase1Step: number; phase2Step: number; }

/**
 * Schwellen der Ketten-Kurve aus der Historie:
 * - T1 (`first`)  = Ø des ERSTEN Fehler-Ratings der letzten Runs (wie bisher), min. startElo+400.
 * - T2 (`second`) = Ø des MAXIMAL-Ratings der letzten 5 Runs (das eigene Niveau), min. T1+200.
 * Ohne (genügend) Historie: startElo+400 / +800.
 */
export function autoFasttrackThresholds(
  config: FasttrackConfig,
  history: FasttrackSession[]
): { first: number; second: number } {
  const defaultFirst = config.startElo + 400;
  const defaultSecond = config.startElo + 800;
  const withMistakes = (history ?? []).filter(s => s.mistakeAtRatings.length > 0).slice(-FASTTRACK_SESSION_COUNT);
  const avgFirst = withMistakes.length
    ? Math.round(withMistakes.reduce((sum, s) => sum + s.mistakeAtRatings[0], 0) / withMistakes.length)
    : defaultFirst;
  const first = Math.max(defaultFirst, avgFirst);
  // T2: Ø des Maximal-Ratings der letzten 5 Runs.
  const last5Max = (history ?? []).filter(s => (s.maxRating ?? 0) > 0).slice(-5);
  const avgMax = last5Max.length
    ? Math.round(last5Max.reduce((sum, s) => sum + (s.maxRating ?? 0), 0) / last5Max.length)
    : defaultSecond;
  return { first, second: Math.max(first + 200, avgMax) };
}

/** Konkave Easing-Funktion [0,1]→[0,1] (schnell hoch, dann abflachend) — „annähernd logarithmisch". */
function logEase(x: number): number {
  if (x <= 0) return 0;
  if (x >= 1) return 1;
  return Math.log(1 + x * (Math.E - 1));
}

/**
 * Rating des n-ten Ketten-Puzzles (0-basiert) entlang der ~logarithmischen Kurve:
 * startElo → T1 (bei ~{@link CHAIN_T1_INDEX}) → T2 (bei ~{@link CHAIN_T2_INDEX}), danach leicht über T2.
 */
export function chainRatingAt(n: number, startElo: number, t1: number, t2: number): number {
  if (n <= 0) return startElo;
  if (n < CHAIN_T1_INDEX)
    return Math.round(startElo + (t1 - startElo) * logEase(n / CHAIN_T1_INDEX));
  if (n < CHAIN_T2_INDEX)
    return Math.round(t1 + (t2 - t1) * logEase((n - CHAIN_T1_INDEX) / (CHAIN_T2_INDEX - CHAIN_T1_INDEX)));
  return Math.round(t2 + (n - CHAIN_T2_INDEX) * CHAIN_FLAT_STEP);
}

/**
 * Baut die Rating-Fenster eines Ketten-Blocks (Gauntlet) für die absoluten Puzzle-Indizes
 * [startIndex, startIndex+count). Jedes Fenster ist um die Kurven-Stelle zentriert (±Window/2),
 * auf [0, ratingMax] geklemmt. Wird an getRandomBatch übergeben.
 */
export function buildChainWindows(
  startElo: number,
  t1: number,
  t2: number,
  ratingMax: number,
  count = ENDLESS_CHAIN_BLOCK,
  startIndex = 0,
): RatingWindow[] {
  const half = Math.round(ENDLESS_RATING_WINDOW / 2);
  const windows: RatingWindow[] = [];
  for (let i = 0; i < count; i++) {
    const r = Math.min(chainRatingAt(startIndex + i, startElo, t1, t2), ratingMax);
    windows.push({ minRating: Math.max(0, r - half), maxRating: r + half });
  }
  return windows;
}

/** Schrittweite je Phase aus den (ggf. manuell überschriebenen) Schwellen. */
export function fasttrackSteps(startElo: number, avgFirst: number, avgSecond: number): FasttrackSteps {
  return {
    phase1Step: Math.max(10, Math.round((avgFirst - startElo) / 5)),
    phase2Step: Math.max(10, Math.round((avgSecond - avgFirst) / 5)),
  };
}

/** Step für den n-ten gelösten Puzzle (Phase 1 ≤5, Phase 2 ≤10, danach 20). */
export function stepForSolved(steps: FasttrackSteps, solvedCount: number): number {
  if (solvedCount <= 5) return steps.phase1Step;
  if (solvedCount <= 10) return steps.phase2Step;
  return 20;
}

/**
 * Vollständige Rating-Fenster für `runs` Endless-Runs aus gespeicherter Config + Historie —
 * für das Offline-Vorab-Laden (App-Start und Modus-Eintritt nutzen dieselbe Logik).
 */
export function buildEndlessRunWindows(
  config: FasttrackConfig & { fasttrackThreshold1?: number; fasttrackThreshold2?: number },
  history: (FasttrackSession & { totalSolved: number })[],
  ratingMax: number,
  runs: number
): RatingWindow[] {
  const auto = autoFasttrackThresholds(config, history);
  const avgFirst = config.fasttrackThreshold1 ?? auto.first;
  const avgSecond = config.fasttrackThreshold2 ?? auto.second;
  const steps = fasttrackSteps(config.startElo, avgFirst, avgSecond);
  const runSize = computeRunSize(history);
  let windows: RatingWindow[] = [];
  for (let r = 0; r < Math.max(1, runs); r++) {
    windows = windows.concat(
      buildRunWindows(config.startElo, runSize, n => stepForSolved(steps, n), ratingMax, ENDLESS_RATING_WINDOW)
    );
  }
  return windows;
}

/**
 * Run-Größe für das Offline-Vorab-Laden: Maximum der gelösten Puzzles der letzten 5 Runs + 10.
 * Ohne (genügend) Historie wird eine Basis von 20 angenommen (→ 30 Puzzles).
 */
export function computeRunSize(history: { totalSolved: number }[]): number {
  const last5 = (history ?? []).slice(-5);
  const base = last5.length ? Math.max(...last5.map(s => s.totalSolved || 0)) : 20;
  return base + 10;
}

/**
 * Simuliert die Rating-Fenster eines Endless-Runs (analog zur Live-Progression):
 * Fenster i beginnt bei `cur`, danach `cur += stepForSolved(i+1)`.
 */
export function buildRunWindows(
  startElo: number,
  runSize: number,
  stepForSolved: (solvedCount: number) => number,
  ratingMax: number,
  ratingWindow = 40
): RatingWindow[] {
  const windows: RatingWindow[] = [];
  let cur = startElo;
  for (let i = 0; i < runSize; i++) {
    if (cur > ratingMax) break;
    windows.push({ minRating: cur, maxRating: cur + ratingWindow });
    cur += Math.max(1, Math.round(stepForSolved(i + 1)));
  }
  return windows;
}

/**
 * Nimmt (und entfernt) ein Puzzle aus dem Offline-Pool, dessen Rating ins Fenster [min,max] passt.
 * `null`, wenn kein passendes vorhanden ist.
 */
export function takeFromPool(pool: PuzzleDto[], min: number, max: number): PuzzleDto | null {
  const idx = pool.findIndex(p => p.rating >= min && p.rating <= max);
  return idx < 0 ? null : pool.splice(idx, 1)[0];
}

/**
 * Fallback, wenn kein Puzzle ins Fenster passt: das im Rating dem Zentrum NÄCHSTGELEGENE nehmen
 * (statt blind das erste/niedrigste) — vermeidet z.B. ein viel zu leichtes Puzzle bei hohem Rating.
 */
export function takeNearestFromPool(pool: PuzzleDto[], center: number): PuzzleDto | null {
  if (pool.length === 0) return null;
  let bestIdx = 0;
  let bestDist = Infinity;
  for (let i = 0; i < pool.length; i++) {
    const d = Math.abs(pool[i].rating - center);
    if (d < bestDist) { bestDist = d; bestIdx = i; }
  }
  return pool.splice(bestIdx, 1)[0];
}
