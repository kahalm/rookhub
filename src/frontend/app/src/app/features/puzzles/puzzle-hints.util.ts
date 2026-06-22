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
 * Klassifiziert den ersten Löserzug eines Standard-(Lichess-)Puzzles für die gestuften Tipps.
 * Lichess-Konvention: `moves[0]` ist der Gegner-/Setup-Zug, `moves[1]` der erste Löserzug.
 * Schach hat Vorrang vor Schlag (Check-Capture-Threat: zuerst Schachgebote prüfen).
 * @returns null bei ungültigen Daten (Tipps werden dann einfach nicht angezeigt).
 */
export function classifyStandardFirstMove(fen: string, movesStr: string): FirstMoveHint | null {
  const toks = (movesStr || '').trim().split(/\s+/);
  if (toks.length < 2) return null;
  try {
    const chess = new Chess(fen);
    applyUci(chess, toks[0]);                 // Gegner-/Setup-Zug
    const piece = chess.get(toks[1].substring(0, 2) as never);
    const mv = applyUci(chess, toks[1]);      // erster Löserzug
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
