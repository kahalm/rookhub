import { WeeklyProgress } from '../weekly/weekly.service';
import { BookPuzzleDto } from './puzzle.service';

/**
 * Reine Helfer für die Tagespuzzle-Datums-Navigation, den Wochenpost-Einstiegs-Index und die
 * Kurs-/Wochenpost-Zeitanzeige. Aus `book-puzzle.component` herausgelöst (keine Verhaltensänderung).
 */

/** UTC-Datum als `yyyyMMdd` (Tagespuzzle-Routenparameter). */
export function formatUtcDate(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getUTCFullYear()}${p(d.getUTCMonth() + 1)}${p(d.getUTCDate())}`;
}

/** Verschiebt ein `yyyyMMdd`-Datum um `delta` Tage (UTC) und formatiert es wieder als `yyyyMMdd`. */
export function shiftDailyDate(date: string, delta: number): string {
  const y = +date.slice(0, 4), m = +date.slice(4, 6), d = +date.slice(6, 8);
  return formatUtcDate(new Date(Date.UTC(y, m - 1, d + delta)));
}

/**
 * Wochenpost-Einstiegs-Index: erstes noch NICHT gespieltes Puzzle (Sprung über bereits Gemachtes).
 * Bei 100 % gespielt → 0 (von vorne); ohne solches Puzzle → 0.
 */
export function weeklyStartIndex(puzzles: BookPuzzleDto[], p: WeeklyProgress): number {
  if (p.completed) return 0;
  const played = new Set(p.playedIndices ?? []);
  const idx = puzzles.findIndex(pz => !played.has(pz.id));
  return idx >= 0 ? idx : 0;
}

/** Sekunden als `m:ss` bzw. `h:mm:ss` (Kurs-/Wochenpost-Zeitanzeige). */
export function formatSecondsClock(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds));
  const sec = s % 60, m = Math.floor(s / 60) % 60, h = Math.floor(s / 3600);
  const p2 = (n: number) => n.toString().padStart(2, '0');
  return h > 0 ? `${h}:${p2(m)}:${p2(sec)}` : `${m}:${p2(sec)}`;
}
