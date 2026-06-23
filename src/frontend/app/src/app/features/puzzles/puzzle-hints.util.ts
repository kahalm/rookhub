import { Chess } from 'chess.js';
import { applyUci } from './puzzle-move.util';

/** Klassifikation des ersten Löserzugs für die on-the-fly-Tipps der Standard-Puzzles. */
export interface FirstMoveHint {
  /** Typ des ersten Löserzugs: forcierendes Schach, Schlagzug oder ruhiger Zug. */
  type: 'check' | 'capture' | 'quiet';
  /** chess.js-Figurtyp des ziehenden Steins (p/n/b/r/q/k) — für Tipp-Stufe 2. */
  pieceType: string;
  /** SAN des ersten Löserzugs (z. B. „Rxe4", „Qh3#") — für Tipp-Stufe 3. */
  san: string;
}

/**
 * Klassifiziert den ersten Löserzug eines Puzzles für die gestuften Tipps. `startPly` legt — wie in
 * {@link BasePuzzleSolver.setupSolver} — fest, wo der Löserzug steht: `-1` = die FEN IST die
 * Trainingsstellung, gelöst wird ab `moves[0]`; `0` = klassische Lichess-Konvention (`moves[0]` ist
 * der Gegner-/Setup-Zug, gelöst ab `moves[1]`); `k` = Vorspiel `moves[0..k]`, gelöst ab `moves[k+1]`.
 * So beschreibt der Tipp IMMER genau den Zug, den der Solver als ersten erwartet.
 * Schach hat Vorrang vor Schlag (Check-Capture-Threat: zuerst Schachgebote prüfen).
 * @returns null bei ungültigen Daten (Tipps werden dann einfach nicht angezeigt).
 */
export function classifyFirstSolverMove(fen: string, movesStr: string, startPly = 0): FirstMoveHint | null {
  const toks = (movesStr || '').trim().split(/\s+/);
  // Index des ersten Löserzugs: bei startPly<0 ist es moves[0], sonst nach Setup/Vorspiel moves[startPly+1].
  const solverIdx = startPly < 0 ? 0 : startPly + 1;
  if (!toks[solverIdx]) return null;
  try {
    const chess = new Chess(fen);
    for (let i = 0; i < solverIdx; i++) applyUci(chess, toks[i]);   // Vorspiel/Setup
    const solverUci = toks[solverIdx];
    const piece = chess.get(solverUci.substring(0, 2) as never);
    const mv = applyUci(chess, solverUci);                          // erster Löserzug
    if (!mv || !piece) return null;
    const check = mv.san.includes('+') || mv.san.includes('#');
    const capture = mv.san.includes('x');
    return {
      type: check ? 'check' : capture ? 'capture' : 'quiet',
      pieceType: piece.type,
      san: mv.san
    };
  } catch {
    return null;
  }
}

/**
 * Standard-(Lichess-)Puzzle: `moves[0]` Setup, `moves[1]` erster Löserzug.
 * Dünner Wrapper um {@link classifyFirstSolverMove} mit `startPly = 0`.
 */
export function classifyStandardFirstMove(fen: string, movesStr: string): FirstMoveHint | null {
  return classifyFirstSolverMove(fen, movesStr, 0);
}
