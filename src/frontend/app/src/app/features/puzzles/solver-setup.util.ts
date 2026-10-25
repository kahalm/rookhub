import { Chess, Square } from 'chess.js';
import { Color } from 'chessground/types';
import { applyUci } from './puzzle-move.util';

/**
 * Reine Regeln für das Aufsetzen eines Puzzles im Lös-Automaten (Zerlegung Z23), von {@link BasePuzzleSolver}
 * in `setupSolver` und `previewSetup` gleich angewandt.
 *
 * `startPly`: -1 = FEN ist die Trainingsstellung (lösen ab moves[0]); 0 = klassisch (moves[0] ist der Setup-Zug,
 * lösen ab moves[1]); k = Vorspiel bis moves[k].
 */

/** Vorspiel-Halbzug klemmen: zu groß (kein Löserzug mehr übrig) → 0, kleiner als -1 → -1. */
export function clampStartPly(startPly: number, moveCount: number): number {
  if (startPly > moveCount - 2) return 0;
  if (startPly < -1) return -1;
  return startPly;
}

/** Blickrichtung des Lösers: bei `startPly < 0` die Seite am Zug, sonst die Gegenseite dessen, der den
 *  Setup-Zug `moves[startPly]` spielt (`chess` steht auf der Stellung VOR dem Setup-Zug). */
export function solverOrientation(chess: Chess, moves: string[], startPly: number): Color {
  if (startPly < 0) return chess.turn() === 'w' ? 'white' : 'black';
  return chess.get(moves[startPly].substring(0, 2) as Square)?.color === 'w' ? 'black' : 'white';
}

/** Stellung zur Vorschau (Vorspiel gespielt, Setup-Zug noch nicht); null, wenn chess.js FEN oder Vorspiel ablehnt. */
export function previewPosition(fen: string, movesStr: string, startPly: number):
    { fen: string; orientation: Color; turnColor: Color } | null {
  const moves = movesStr.split(' ');
  const sp = clampStartPly(startPly, moves.length);
  try {
    const chess = new Chess(fen);
    for (let i = 0; i < sp; i++) applyUci(chess, moves[i]);
    return { fen: chess.fen(), orientation: solverOrientation(chess, moves, sp), turnColor: chess.turn() === 'w' ? 'white' : 'black' };
  } catch {
    return null;
  }
}
