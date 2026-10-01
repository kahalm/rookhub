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

/**
 * Derselbe Knopf heißt in allen drei Lösern gleich (Codereview F2-012): Endless sagte auf Deutsch und
 * Kroatisch „Reset", „Mouseslip", „Give Up", „Show Eval", das Buch „Mausverrutscher", während dieselbe
 * Leiste auf /puzzles „Zurücksetzen", „Mausrutscher", „Aufgeben" zeigte. Was eine Aktion im Modus
 * kostet (Endless: ein Leben), erklärt dessen Hilfe, nicht die Beschriftung. Die Tabelle je Modus
 * bleibt, damit die Aufrufer weiter `[mode]` nachschlagen.
 */
const SHARED_ACTION_KEYS = {
  reset: 'puzzles.actions.reset', mouseslip: 'puzzles.actions.mouseslip', giveUp: 'puzzles.actions.giveUp',
} as const;

export const SOLVER_ACTION_KEYS: Readonly<Record<PuzzleSolverMode, typeof SHARED_ACTION_KEYS>> = {
  standard: SHARED_ACTION_KEYS,
  endless: SHARED_ACTION_KEYS,
  book: SHARED_ACTION_KEYS,
};

/** Bewertungs-Knopf und -Zeile im „Your turn"-Panel — ebenfalls ein Satz Keys für alle Modi. */
const SHARED_EVAL_KEYS = {
  show: 'puzzles.eval.show', hide: 'puzzles.eval.hide', start: 'puzzles.eval.start', now: 'puzzles.eval.now',
} as const;

export const SOLVER_EVAL_KEYS: Readonly<Record<PuzzleSolverMode, typeof SHARED_EVAL_KEYS>> = {
  standard: SHARED_EVAL_KEYS,
  endless: SHARED_EVAL_KEYS,
  book: SHARED_EVAL_KEYS,
};

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
