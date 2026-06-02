import { PuzzleDto } from './puzzle.service';

export interface RatingWindow { minRating: number; maxRating: number; }

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
