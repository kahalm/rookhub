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
  return classifyMoveAt(fen, toks, solverIdx);
}

/**
 * Klassifiziert den Zug an absolutem Index `index` (0-basiert in `moves`) — spielt `moves[0..index-1]`
 * ab `fen` nach und klassifiziert `moves[index]`. Damit gibt es gestufte Tipps zu JEDEM Löserzug,
 * nicht nur dem ersten (der Solver ruft es mit dem aktuell erwarteten Zug-Index auf). `null` bei
 * ungültigem Index oder nicht spielbarem Zug.
 */
export function classifyMoveAt(fen: string, moves: string[], index: number): FirstMoveHint | null {
  if (index < 0 || index >= moves.length || !moves[index]) return null;
  try {
    const chess = new Chess(fen);
    for (let i = 0; i < index; i++) applyUci(chess, moves[i]);   // Vorspiel bis zum Zug
    return classifyMoveFromFen(chess.fen(), moves[index]);
  } catch {
    return null;
  }
}

/**
 * Klassifiziert EINEN Zug (`uci`) aus einer Stellung (`fen`) für die gestuften Tipps — für Tipps zu
 * JEDEM Löserzug (nicht nur dem ersten): der Solver ruft das mit der AKTUELLEN Brettstellung und dem
 * aktuell erwarteten Zug auf. `null`, wenn der Zug in der Stellung nicht spielbar ist (z. B. off-path).
 */
export function classifyMoveFromFen(fen: string, uci: string): FirstMoveHint | null {
  if (!uci) return null;
  try {
    const chess = new Chess(fen);
    const piece = chess.get(uci.substring(0, 2) as never);
    const mv = applyUci(chess, uci);
    if (!mv || !piece) return null;
    const check = mv.san.includes('+') || mv.san.includes('#');
    const capture = mv.san.includes('x');
    return { type: check ? 'check' : capture ? 'capture' : 'quiet', pieceType: piece.type, san: mv.san };
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

/**
 * Baut die 3 gestuften Tipp-Strings (Stufe 1 Zugtyp Schach/Schlag/ruhig → Stufe 2 ziehende Figur →
 * Stufe 3 SAN) aus einer Zug-Klassifikation. Identisch von Standard-/Endless-/Buch-Solver genutzt.
 * `t` ist die Übersetzungsfunktion der Komponente (`(key, params?) => translate.instant(key, params)`),
 * damit die Util frei von Angular-Abhängigkeiten bleibt. Leere Liste bei fehlender Klassifikation.
 */
export function buildStagedHints(
  hint: FirstMoveHint | null,
  t: (key: string, params?: object) => string,
): string[] {
  if (!hint) return [];
  const tier1 = hint.type === 'check' ? t('puzzles.hints.t1Check')
    : hint.type === 'capture' ? t('puzzles.hints.t1Capture')
    : t('puzzles.hints.t1Quiet');
  const PIECE: Record<string, string> = { p: 'pawn', n: 'knight', b: 'bishop', r: 'rook', q: 'queen', k: 'king' };
  const piece = t('puzzles.hints.pieces.' + (PIECE[hint.pieceType] ?? 'piece'));
  return [tier1, t('puzzles.hints.t2Piece', { piece }), t('puzzles.hints.t3Move', { move: hint.san })];
}
