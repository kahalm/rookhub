/**
 * Sichtbarkeits-Regeln + Übersetzungs-Keys der drei Löse-Aktionen (Zurücksetzen, Mausrutscher,
 * Aufgeben). EINE Quelle für beide Darstellungen: die Aktionszeile im „Your turn"-Panel
 * (`puzzle-your-turn.component`) und die kompakte Icon-Leiste im Brett-Vollbild
 * (`board-fs-actions.component`) — sonst driften die Bedingungen auseinander und ein Knopf
 * erscheint an einer Stelle, an der er nichts tut.
 */

export type PuzzleSolverMode = 'standard' | 'endless' | 'book';

/** Löse-States, in denen die Aktionen überhaupt sinnvoll sind. */
export const SOLVING_STATES = ['AWAITING_USER_MOVE', 'THINKING', 'PLAYING'] as const;

export const SOLVER_ACTION_KEYS = {
  standard: { reset: 'puzzles.actions.reset', mouseslip: 'puzzles.actions.mouseslip', giveUp: 'puzzles.actions.giveUp' },
  endless: { reset: 'endless.game.reset', mouseslip: 'endless.game.mouseslip', giveUp: 'endless.game.giveUp' },
  book: { reset: 'book.actions.reset', mouseslip: 'book.actions.mouseslip', giveUp: 'book.actions.giveUp' },
} as const;

/** Wird gerade gelöst (Aktionen sichtbar)? */
export function isSolvingState(state: string): boolean {
  return (SOLVING_STATES as readonly string[]).includes(state);
}

/** Zurücksetzen lohnt erst, wenn etwas zurückzusetzen ist (eigener Zug gemacht oder Partie läuft). */
export function canReset(state: string, hasMadeFirstMove: boolean): boolean {
  return hasMadeFirstMove || state !== 'AWAITING_USER_MOVE';
}

/**
 * Mausrutscher rückgängig: `showMouseslip` bringt der Aufrufer mit (`!mouseslipUsed && …`),
 * hier kommt nur die Zustands-Bedingung dazu.
 */
export function canMouseslip(
  state: string, showMouseslip: boolean, hasMadeFirstMove: boolean, showMouseslipInThinking: boolean,
): boolean {
  if (!showMouseslip) return false;
  return hasMadeFirstMove || state === 'PLAYING' || (state === 'THINKING' && showMouseslipInThinking);
}
