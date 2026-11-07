import { OpponentPositions, opponentMovesText, opponentNote } from '../../shared/player-card/opponent-positions';

/** Fertiger Text der Anzeige „aus Sicht seiner Partien" (0.892.0): `left` = ab hier keine Partien mehr (deutlich, bleibt stehen). */
export interface OpponentNoteView { text: string; moves: string | null; left: boolean }

export interface OpponentNoteOptions {
  /** Name des Gegners (Nachname); ohne → „des Gegners". */
  who: string | null;
  /** Lichess-Band der Schätzung; `null` = keine Schätzung. */
  band: string | null;
  /** Was nach dem Ende seiner Partien gilt: `repertoire` (Trainer) bzw. `engine` (Abweichungen). Vorgabe `repertoire`. */
  after?: 'repertoire' | 'engine';
  lang: string;
  t: (key: string, params?: Record<string, unknown>) => string;
}

/**
 * Text zur Stellung nach `ply` Halbzügen (Wunsch 07.11.: „rechts eine meldung ab wann ich von gespielten partien abgewichen bin"):
 * „In den Partien von Maras: 14× erreicht (zuletzt 2024)" + „er spielte hier 6…Sf6 9×, 6…e6 3×", bzw. ab dem ersten Halbzug ohne
 * seine Partien „Ab 7.Le3 keine Partien von Maras mehr — ab hier geschätzt (Lichess 2143–2443)" / „… — ab hier nur dein Repertoire".
 */
export function opponentNoteView(fens: readonly string[], sans: readonly string[], ply: number, positions: OpponentPositions | null,
                                 o: OpponentNoteOptions): OpponentNoteView | null {
  const note = opponentNote(fens, sans, ply, positions, o.lang);
  if (!note) return null;
  const who = o.who?.trim() || o.t('opponentNote.thisOpponent');
  if (note.kind === 'never') return { text: o.t('opponentNote.never', { who }), moves: null, left: true };
  if (note.kind === 'left') {
    const key = o.after === 'engine' ? 'opponentNote.leftEngine' : o.band ? 'opponentNote.leftEstimated' : 'opponentNote.leftRepertoire';
    return { text: o.t(key, { move: note.move, who, band: o.band }), moves: null, left: true };
  }
  const text = o.t(note.lastYear ? 'opponentNote.reached' : 'opponentNote.reachedNoYear', { who, n: note.reached, year: note.lastYear });
  const moves = note.moves.length
    ? o.t('opponentNote.played', { moves: opponentMovesText(note.moves, fens, sans, Math.min(ply, fens.length - 1), o.lang) })
    : null;
  return { text, moves, left: false };
}
