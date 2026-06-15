import { PuzzleDto } from './puzzle.service';

export interface RatingWindow { minRating: number; maxRating: number; }

export const FASTTRACK_SESSION_COUNT = 10;
export const ENDLESS_RATING_WINDOW = 40;

/** Kette (Gauntlet): Indizes der beiden Schwellen + Blockgröße. */
export const CHAIN_T1_INDEX = 10;         // T1 (Ø erster Fehler) nach ~10 Puzzles erreicht
export const CHAIN_T2_INDEX = 25;         // T2 (Ø Max der letzten 5 Runs) nach ~25 Puzzles erreicht
export const CHAIN_FLAT_STEP = 15;        // ab T2: leichtes Plus je Puzzle (oberhalb des eigenen Maximums)
export const ENDLESS_CHAIN_BLOCK = 30;    // Puzzles je generiertem Block

/**
 * Erster Lauf (keine Historie): bewusst steile Kurve, damit der allererste Puzzle-Rush
 * relativ schnell sicher tödlich wird — bevor genug Daten für die adaptive Kurve da sind.
 * Anker: 2000 nach 15 Puzzles, 3000 nach 30 Puzzles (linear, danach weiter mit CHAIN_FLAT_STEP).
 */
export const FIRST_RUN_ANCHOR1_INDEX = 15;
export const FIRST_RUN_ANCHOR1_RATING = 2000;
export const FIRST_RUN_ANCHOR2_INDEX = 30;
export const FIRST_RUN_ANCHOR2_RATING = 3000;

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
 * Steile Kurve für den ERSTEN Lauf (linear durch die festen Anker, danach flach weiter):
 * startElo → {@link FIRST_RUN_ANCHOR1_RATING} (bei {@link FIRST_RUN_ANCHOR1_INDEX})
 *          → {@link FIRST_RUN_ANCHOR2_RATING} (bei {@link FIRST_RUN_ANCHOR2_INDEX}), dann + CHAIN_FLAT_STEP/Puzzle.
 */
export function firstRunRatingAt(n: number, startElo: number): number {
  if (n <= 0) return startElo;
  if (n < FIRST_RUN_ANCHOR1_INDEX)
    return Math.round(startElo + (FIRST_RUN_ANCHOR1_RATING - startElo) * (n / FIRST_RUN_ANCHOR1_INDEX));
  if (n < FIRST_RUN_ANCHOR2_INDEX)
    return Math.round(FIRST_RUN_ANCHOR1_RATING + (FIRST_RUN_ANCHOR2_RATING - FIRST_RUN_ANCHOR1_RATING)
      * ((n - FIRST_RUN_ANCHOR1_INDEX) / (FIRST_RUN_ANCHOR2_INDEX - FIRST_RUN_ANCHOR1_INDEX)));
  return Math.round(FIRST_RUN_ANCHOR2_RATING + (n - FIRST_RUN_ANCHOR2_INDEX) * CHAIN_FLAT_STEP);
}

/**
 * Rating des n-ten Ketten-Puzzles (0-basiert) entlang der ~logarithmischen Kurve:
 * startElo → T1 (bei {@link CHAIN_T1_INDEX}) → T2 (bei {@link CHAIN_T2_INDEX}), danach leicht über T2.
 * `firstRun` schaltet auf die bewusst steile Erst-Lauf-Kurve ({@link firstRunRatingAt}) um.
 */
export function chainRatingAt(n: number, startElo: number, t1: number, t2: number, firstRun = false): number {
  if (firstRun) return firstRunRatingAt(n, startElo);
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
  firstRun = false,
): RatingWindow[] {
  const half = Math.round(ENDLESS_RATING_WINDOW / 2);
  const windows: RatingWindow[] = [];
  for (let i = 0; i < count; i++) {
    const r = Math.min(chainRatingAt(startIndex + i, startElo, t1, t2, firstRun), ratingMax);
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
