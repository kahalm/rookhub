import { Chess } from 'chess.js';
import { EvalScore, GameEvals, GameReview, MoveClass } from './game-review.util';
import { uciOf } from './move-tactics.util';

/**
 * „Eigene Fehler nachspielen" — die Auswahl der Stellungen, analog zu Lichess' „Aus deinen Fehlern
 * lernen". Rein: kein Angular, kein HTTP. Quelle ist ausschliesslich RookHubs eigene Partie-Analyse
 * (Klassifizierung aus `game-review.util`, Bestzug aus `GameEvalPly.bestUci`).
 */

/**
 * Die drei Stufen, die auch Lichess abfragt — Ungenauigkeit, Fehler, grober Fehler. Geprüft wird die
 * GRUNDklasse (`ReviewedMove.base`), nicht das Etikett: ein als Miss ausgewiesener Zug ist ein Fehler,
 * dem zusätzlich eine Gelegenheit entgangen ist, und Lichess kennt dieses Etikett gar nicht.
 */
export const TRAINED_CLASSES: readonly MoveClass[] = ['inaccuracy', 'mistake', 'blunder'];
const TRAINED = new Set<MoveClass>(TRAINED_CLASSES);

/** Ein Zug der Partie, so wie ihn der PGN-Viewer fuehrt (chess.js-`Move`). */
export interface PlayedMove {
  san: string;
  from: string;
  to: string;
  promotion?: string | null;
}

/** Eine abzufragende Stellung: die Lage VOR dem Fehler, der gespielte Zug und der bessere. */
export interface Mistake {
  ply: number;
  white: boolean;
  cls: MoveClass;
  fenBefore: string;
  playedSan: string;
  playedUci: string;
  bestUci: string;
  bestSan: string;
  evalBefore: EvalScore;
  evalAfter: EvalScore;
  /** Verlorene Gewinnchance in Prozentpunkten, aus Sicht des Ziehenden — nie negativ. */
  lostPercent: number;
}

export interface MistakesBySide {
  white: Mistake[];
  black: Mistake[];
}

export const NO_MISTAKES: MistakesBySide = { white: [], black: [] };

/** SAN eines UCI-Zugs in einer Stellung; `null`, wenn er dort nicht geht (oder die FEN unlesbar ist). */
export function sanOfUci(fen: string, uci: string): string | null {
  if (!fen || !uci || uci.length < 4) return null;
  try {
    const chess = new Chess(fen);
    const move = chess.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci.length > 4 ? uci[4] : undefined });
    return move ? move.san : null;
  } catch {
    return null;
  }
}

/**
 * Die Fehler beider Seiten in Partie-Reihenfolge. Uebersprungen wird, was sich nicht abfragen laesst:
 * ohne Bestzug gibt es keine Loesung, ein Bestzug GLEICH dem gespielten Zug ist kein Lehrstueck (er
 * kommt bei diesen Klassen nicht vor, waere aber eine Aufgabe ohne Antwort), und ein Bestzug, der in
 * der Stellung gar nicht geht, wird nicht vorgefuehrt — eine unspielbare „Loesung" ist schlimmer als
 * eine ausgelassene Aufgabe.
 */
export function collectMistakes(
  review: GameReview | null | undefined,
  evals: GameEvals | null | undefined,
  fens: readonly string[],
  moves: readonly PlayedMove[],
): MistakesBySide {
  const out: MistakesBySide = { white: [], black: [] };
  if (!review || !evals) return out;
  const rows = new Map((evals.plies ?? []).map(p => [p.ply, p]));

  for (const m of review.moves) {
    if (!m || !TRAINED.has(m.base)) continue;
    const best = (rows.get(m.ply)?.bestUci || '').toLowerCase();
    const fenBefore = fens[m.ply];
    const played = moves[m.ply];
    if (!best || !fenBefore || !played) continue;
    const playedUci = uciOf(played).toLowerCase();
    if (best === playedUci) continue;
    const bestSan = sanOfUci(fenBefore, best);
    if (!bestSan) continue;
    (m.white ? out.white : out.black).push({
      ply: m.ply, white: m.white, cls: m.base, fenBefore,
      playedSan: played.san, playedUci, bestUci: best, bestSan,
      evalBefore: m.evalBefore, evalAfter: m.evalAfter,
      lostPercent: Math.max(0, m.winBefore - m.winAfter),
    });
  }
  return out;
}

/** Zusammen, fuer den Zaehler am Knopf. */
export function mistakeCount(bySide: MistakesBySide): number {
  return bySide.white.length + bySide.black.length;
}

/**
 * Welche Seite trainieren, wenn die Partie keinem Konto zuzuordnen ist (`ownerSide` fehlt)? Die mit
 * den meisten Fehlern; bei Gleichstand Weiss. Eine Seite zu RATEN waere schlechter als diese Regel,
 * weil der Trainer die Wahl sichtbar anbietet.
 */
export function sideWithMoreMistakes(bySide: MistakesBySide): 'white' | 'black' {
  return bySide.black.length > bySide.white.length ? 'black' : 'white';
}
