/**
 * Rating-Fenster für den Standard-Puzzle-Modus (geteilt von PuzzleComponent und dem
 * App-Start-Prefetch, damit der Offline-Pool dasselbe Fenster nutzt wie das Live-Spiel).
 */

// Schwierigkeit → Elo-Offset des Fenster-Zentrums; Fenster ±RATING_WINDOW um (Elo + Offset).
export const DIFFICULTY_OFFSET: Record<string, number> = {
  sehr_leicht: -600, leicht: -300, normal: 0, schwer: 300, sehr_schwer: 600,
};
export const RATING_WINDOW = 100;

/** Fenster aus Puzzle-Elo + Schwierigkeits-Offset (±RATING_WINDOW), geklemmt in den DB-Rating-Bereich. */
export function puzzleWindow(
  puzzleElo: number,
  difficulty: string,
  bounds: { min: number; max: number } | null
): { min: number; max: number } {
  let center = puzzleElo + (DIFFICULTY_OFFSET[difficulty] ?? 0);
  if (bounds && bounds.max > bounds.min) {
    center = Math.min(Math.max(center, bounds.min + RATING_WINDOW), bounds.max - RATING_WINDOW);
  }
  return { min: Math.max(0, center - RATING_WINDOW), max: center + RATING_WINDOW };
}
