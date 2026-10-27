import { Chess } from 'chess.js';
import { AnalysisNode } from './analysis-tree';
import { EvalScore, MoveClass, classify, formatEval, winPercent } from '../games/game-review.util';
import { whiteToMove } from '../../shared/chess/fen-side';

/**
 * „Ohne Engine analysieren" (0.855.0) — reine Regeln ohne Angular: die Knoten in der Reihenfolge des gespeicherten Baums,
 * Bewertungen an die Knoten hängen, und der Vergleich „wo wich deine Linie vom besten Zug ab und was kostete das".
 */

/** Bewertung einer Stellung (Weiß-Sicht) samt Bestzug — wie `LineStudyNodeEval` ohne Index. */
export interface NodeEval extends EvalScore {
  bestUci?: string | null;
}

/** Die Knoten in der Reihenfolge von `toDto` (Tiefensuche, Fortsetzung vor Varianten) — der Server zählt seine Indizes so. */
export function flatNodes(root: AnalysisNode): AnalysisNode[] {
  const out: AnalysisNode[] = [];
  const walk = (node: AnalysisNode) => {
    for (const child of node.children) { out.push(child); walk(child); }
  };
  walk(root);
  return out;
}

/** Die Knoten der gespielten Partie: vom Anfang den Partiezügen entlang, solange es sie im Baum gibt. */
export function gameNodes(root: AnalysisNode, gameUcis: readonly string[]): Set<AnalysisNode> {
  const set = new Set<AnalysisNode>();
  let node: AnalysisNode | undefined = root;
  for (const uci of gameUcis) {
    node = node?.children.find(c => c.uci === uci);
    if (!node) break;
    set.add(node);
  }
  return set;
}

/** Eine eigene Entscheidung im Vergleich: der Zug, seine Klasse, der Verlust (Prozentpunkte Gewinnchance) und der bessere Zug. */
export interface LineVerdict {
  node: AnalysisNode;
  cls: MoveClass;
  loss: number;
  /** Der Bestzug der Stellung davor in SAN — nur, wenn der gespielte nicht der beste war. */
  bestSan: string | null;
  /** Bewertung nach dem Zug („+0.40"). */
  evalText: string;
}

export interface LineComparison {
  verdicts: LineVerdict[];
  /** Eigene Züge mit Bewertung vor und nach dem Zug. */
  rated: number;
  /** Davon der Bestzug der Engine. */
  best: number;
  /** Davon Ungenauigkeit, Fehler, grober Fehler. */
  inaccuracies: number;
  mistakes: number;
  blunders: number;
}

/**
 * Der Vergleich über die EIGENEN Züge (alles im Baum außer der gespielten Partie): je Zug die Klasse wie im Rückblick
 * (Verlust an Gewinnchance aus Sicht des Ziehenden, `classify`), und wo er nicht der beste war, der bessere Zug.
 * `verdicts` enthält nur die Züge ab Ungenauigkeit — in der Reihenfolge des Baums.
 */
export function compareLines(root: AnalysisNode, evalOf: ReadonlyMap<AnalysisNode, NodeEval>, gameUcis: readonly string[]): LineComparison {
  const game = gameNodes(root, gameUcis);
  const result: LineComparison = { verdicts: [], rated: 0, best: 0, inaccuracies: 0, mistakes: 0, blunders: 0 };
  for (const node of flatNodes(root)) {
    if (game.has(node) || !node.parent) continue;
    const before = evalOf.get(node.parent);
    const after = evalOf.get(node);
    if (!before || !after) continue;
    const moverWhite = whiteToMove(node.parent.fen);
    const wb = winPercent(before, moverWhite);
    const wa = winPercent(after, whiteToMove(node.fen));
    if (wb == null || wa == null) continue;
    const mb = moverWhite ? wb : 100 - wb;
    const ma = moverWhite ? wa : 100 - wa;
    const isBest = !!before.bestUci && before.bestUci.toLowerCase() === node.uci.toLowerCase();
    const cls = classify(mb, ma, isBest);
    result.rated++;
    if (cls === 'best') result.best++;
    if (cls !== 'inaccuracy' && cls !== 'mistake' && cls !== 'blunder') continue;
    if (cls === 'inaccuracy') result.inaccuracies++;
    else if (cls === 'mistake') result.mistakes++;
    else result.blunders++;
    result.verdicts.push({ node, cls, loss: mb - ma, bestSan: sanOf(node.parent.fen, before.bestUci), evalText: formatEval(after) });
  }
  return result;
}

/** SAN eines UCI-Zugs in einer Stellung; `null`, wenn er dort nicht geht. */
export function sanOf(fen: string, uci: string | null | undefined): string | null {
  if (!uci) return null;
  try {
    const c = new Chess(fen);
    return c.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] }).san;
  } catch { return null; }
}
