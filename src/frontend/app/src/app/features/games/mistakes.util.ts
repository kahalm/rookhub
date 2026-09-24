import { Chess } from 'chess.js';
import { CLASS_LIMITS, EvalScore, GameEvalPly, GameEvals, GameReview, MoveClass, winPercent } from './game-review.util';
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
  /** Alle Züge, die als gefunden zählen — der Bestzug zuerst, dahinter die gleichwertigen
   *  (siehe {@link EQUIVALENT_LIMIT}). Mindestens der Bestzug. */
  acceptUci: string[];
  acceptSan: string[];
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

/**
 * Wann ein anderer Zug der Engine so gut ist wie ihr bester: höchstens so viele PROZENTPUNKTE
 * Gewinnchance dahinter, aus Sicht des Ziehenden. Dieselbe Grenze, bis zu der der Rückblick einen Zug
 * „Exzellent" nennt (`CLASS_LIMITS.excellent`) — ein Zug, den die Kurve nicht als Verlust führt, darf
 * im Training nicht als daneben gelten. Gewünscht 2026-09-24: vorher zählte NUR der Bestzug, und wer
 * einen gleich guten spielte, bekam „daneben".
 */
export const EQUIVALENT_LIMIT = CLASS_LIMITS.excellent;

/**
 * Die als gefunden geltenden Züge einer Stellung: der Bestzug und jeder Kandidat derselben Suche, der
 * höchstens {@link EQUIVALENT_LIMIT} Punkte hinter ihm liegt. Der gespielte Fehlzug ist nie dabei, und
 * ein Zug, der in der Stellung nicht geht, auch nicht. Ohne Kandidatenliste (älterer Server) bleibt es
 * beim Bestzug. Züge ausserhalb der fünf Kandidaten lassen sich nicht beurteilen und zählen nicht.
 */
export function acceptedMoves(
  row: GameEvalPly, fenBefore: string, white: boolean, bestUci: string, playedUci: string,
): { uci: string; san: string }[] {
  const bestSan = sanOfUci(fenBefore, bestUci);
  const out = bestSan ? [{ uci: bestUci, san: bestSan }] : [];
  const mover = (s: EvalScore): number | null => {
    const w = winPercent(s);
    return w == null ? null : white ? w : 100 - w;
  };
  const candidates = row.candidates ?? [];
  const bestRow = candidates.find(c => c.uci.toLowerCase() === bestUci) ?? candidates[0];
  const top = bestRow ? mover(bestRow) : null;
  if (top == null) return out;
  for (const c of candidates) {
    const uci = c.uci.toLowerCase();
    if (uci === bestUci || uci === playedUci || out.some(o => o.uci === uci)) continue;
    const win = mover(c);
    if (win == null || top - win > EQUIVALENT_LIMIT) continue;
    const san = sanOfUci(fenBefore, uci);
    if (san) out.push({ uci, san });
  }
  return out;
}

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
    const row = rows.get(m.ply);
    const best = (row?.bestUci || '').toLowerCase();
    const fenBefore = fens[m.ply];
    const played = moves[m.ply];
    if (!best || !fenBefore || !played) continue;
    const playedUci = uciOf(played).toLowerCase();
    if (best === playedUci) continue;
    const bestSan = sanOfUci(fenBefore, best);
    if (!bestSan || !row) continue;
    const accepted = acceptedMoves(row, fenBefore, m.white, best, playedUci);
    (m.white ? out.white : out.black).push({
      ply: m.ply, white: m.white, cls: m.base, fenBefore,
      playedSan: played.san, playedUci, bestUci: best, bestSan,
      acceptUci: accepted.map(a => a.uci), acceptSan: accepted.map(a => a.san),
      evalBefore: m.evalBefore, evalAfter: m.evalAfter,
      lostPercent: Math.max(0, m.winBefore - m.winAfter),
    });
  }
  return out;
}

/** Die Aufgaben EINER Seite. */
export function mistakesOf(bySide: MistakesBySide, side: 'white' | 'black'): Mistake[] {
  return side === 'white' ? bySide.white : bySide.black;
}

/**
 * Welche Seite der Trainer abfragt — und damit auch, was der Knopf zaehlt: die des Besitzers, wenn die
 * Partie ihm zuzuordnen ist, sonst `sideWithMoreMistakes`. Knopf und Dialog MUESSEN dieselbe Seite
 * meinen. Bis 0.517.0 zaehlte der Knopf beide Seiten zusammen, der Dialog oeffnete aber auf der des
 * Besitzers — gemeldet 2026-09-24: „Eigene Fehler nachspielen (1)", und der Dialog fand nichts, weil der
 * einzige bis dahin gefundene Fehler (die Analyse lief noch) der des Gegners war. Ohne Fehler auf der
 * zweiten Seite gab es im Dialog nicht einmal den Umschalter.
 */
export function trainingSide(bySide: MistakesBySide, ownerSide?: 'white' | 'black' | null): 'white' | 'black' {
  return ownerSide ?? sideWithMoreMistakes(bySide);
}

/**
 * Welche Seite trainieren, wenn die Partie keinem Konto zuzuordnen ist (`ownerSide` fehlt)? Die mit
 * den meisten Fehlern; bei Gleichstand Weiss. Eine Seite zu RATEN waere schlechter als diese Regel,
 * weil der Trainer die Wahl sichtbar anbietet.
 */
export function sideWithMoreMistakes(bySide: MistakesBySide): 'white' | 'black' {
  return bySide.black.length > bySide.white.length ? 'black' : 'white';
}
