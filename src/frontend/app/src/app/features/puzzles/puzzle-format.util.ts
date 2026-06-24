/**
 * Gemeinsame Anzeige-Formatierung für die Puzzle-Modi (Standard/Buch/Endless) und die
 * präsentationalen Karten. Vermeidet die mehrfach identisch kopierte `formatTime`-Logik.
 */

/** Lösezeit kompakt: „1:05" ab einer Minute, sonst „45s". */
export function formatPuzzleTime(seconds: number): string {
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return m > 0 ? `${m}:${s.toString().padStart(2, '0')}` : `${s}s`;
}
