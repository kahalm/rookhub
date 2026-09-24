import { BoardArrow } from '../../shared/pgn-viewer/chess-board.component';
import { normalizeCastlingUci } from '../analysis/castling-uci.util';
import { uciLineToSan } from '../analysis/engine-lines.util';
import { GameEvals, formatEval } from './game-review.util';

/**
 * „Computer-Linien" auf der Partieseite: die Kandidaten der eigenen Partie-Analyse für die Stellung, die
 * gerade auf dem Brett steht — rein aus `GET …/evals`, keine Engine im Browser. Rein und ohne Angular.
 *
 * Varianten gibt es erst für Analysen ab 0.521.0 (`GameEvalCandidate.pv`); ältere zeigen je Kandidat nur
 * den Zug mit seiner Bewertung — das ist dieselbe Zeile, nur ohne Fortsetzung.
 */
export interface ComputerLine {
  /** Bewertung in Weiß-Sicht, wie die Kurve („+0.35", „#3"). */
  evalText: string;
  /** Weiß steht nach dieser Linie besser oder gleich (für die Farbe des Kästchens). */
  whiteBetter: boolean;
  /** Die Linie in SAN mit Zugnummern („22... gxf4 23. Bxd5 Qd6"). */
  san: string;
  /** Diesen Zug hat die Partie gespielt. */
  played: boolean;
}

/** So viele Halbzüge einer Variante werden gezeigt — mehr liest am Brett niemand mit. */
export const COMPUTER_LINE_PLIES = 10;

/**
 * Die Linien der Stellung NACH dem Halbzug `currentIndex` (−1 = Startstellung), wie `currentMoveIndex`.
 * Leer, wenn diese Stellung nicht gerechnet ist — auch für die Endstellung: dort wird nicht mehr gezogen,
 * die Analyse rechnet sie nicht.
 */
export function computerLinesAt(evals: GameEvals | null | undefined, fens: readonly string[], currentIndex: number): ComputerLine[] {
  const ply = currentIndex + 1;
  const fen = fens[ply];
  const row = evals?.plies?.find(p => p.ply === ply);
  if (!row || !fen) return [];
  return (row.candidates ?? []).map(c => {
    const line = c.pv?.length ? normalizeCastlingUci(fen, c.pv) : [c.uci];
    return {
      evalText: formatEval(c),
      whiteBetter: c.mate != null ? c.mate > 0 : (c.cp ?? 0) >= 0,
      san: uciLineToSan(fen, line, COMPUTER_LINE_PLIES),
      played: c.uci.toLowerCase() === (row.playedUci ?? '').toLowerCase(),
    };
  }).filter(l => l.san !== '');
}

/** Pfeil für den besten Zug dieser Stellung; `null`, wenn sie nicht gerechnet ist. */
export function bestMoveArrowAt(evals: GameEvals | null | undefined, currentIndex: number): BoardArrow | null {
  const row = evals?.plies?.find(p => p.ply === currentIndex + 1);
  const uci = row?.bestUci ?? '';
  return uci.length >= 4 ? { from: uci.slice(0, 2), to: uci.slice(2, 4) } : null;
}
