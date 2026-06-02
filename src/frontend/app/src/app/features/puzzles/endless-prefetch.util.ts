import { PuzzleDto } from './puzzle.service';

export interface RatingWindow { minRating: number; maxRating: number; }

export const FASTTRACK_SESSION_COUNT = 10;
export const ENDLESS_RATING_WINDOW = 40;

/** Strukturelle Minimalformen (entkoppelt von EndlessConfig/EndlessSession). */
interface FasttrackConfig { startElo: number; fasttrackThreshold1?: number; fasttrackThreshold2?: number; }
interface FasttrackSession { mistakeAtRatings: number[]; }

export interface FasttrackSteps { phase1Step: number; phase2Step: number; }

/**
 * Auto-Schwellen der beiden Fasttrack-Phasen aus der Fehler-Historie (Mittel der letzten Runs,
 * mindestens startElo+400 / +800). Identisch zur Live-Berechnung im Endless-Component.
 */
export function autoFasttrackThresholds(
  config: FasttrackConfig,
  history: FasttrackSession[]
): { first: number; second: number } {
  const defaultFirst = config.startElo + 400;
  const defaultSecond = config.startElo + 800;
  const withMistakes = (history ?? []).filter(s => s.mistakeAtRatings.length > 0).slice(-FASTTRACK_SESSION_COUNT);
  if (withMistakes.length === 0) return { first: defaultFirst, second: defaultSecond };
  const avgFirst = Math.round(withMistakes.reduce((sum, s) => sum + s.mistakeAtRatings[0], 0) / withMistakes.length);
  const withSecond = withMistakes.filter(s => s.mistakeAtRatings.length >= 2);
  const avgSecond = withSecond.length > 0
    ? Math.round(withSecond.reduce((sum, s) => sum + s.mistakeAtRatings[1], 0) / withSecond.length)
    : avgFirst + 100;
  return { first: Math.max(defaultFirst, avgFirst), second: Math.max(defaultSecond, avgSecond) };
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
