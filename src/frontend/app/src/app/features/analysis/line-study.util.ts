import { Chess } from 'chess.js';
import { AnalysisNode, mainline } from './analysis-tree';
import { EvalScore, MoveClass, classifyMove, formatEval, winPercent } from '../games/game-review.util';
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

/** Eigenes am Knoten: Stern, Zug-Symbol, Stellungsbewertung oder Kommentar (die Engine-Bewertung zählt nicht dazu). */
function hasOwnMark(n: AnalysisNode): boolean {
  return !!(n.starred || n.glyph || n.evaluation || n.comment);
}

/** Ist der Baum noch die unberührte Partie — dieselben Züge in derselben Form wie `game` (der Baum aus dem PGN, samt dessen
 *  Varianten), nichts Eigenes daran? Ohne Rekursion: eine lange Partie ist eine tiefe Kette. */
export function isUntouchedGame(root: AnalysisNode, game: AnalysisNode): boolean {
  const open: [AnalysisNode, AnalysisNode][] = [[root, game]];
  while (open.length > 0) {
    const [a, b] = open.pop()!;
    if (hasOwnMark(a) || a.children.length !== b.children.length) return false;
    for (let i = 0; i < a.children.length; i++) {
      if (a.children[i].uci !== b.children[i].uci) return false;
      open.push([a.children[i], b.children[i]]);
    }
  }
  return true;
}

/**
 * Wo das Brett „ohne Engine" beim Wiederöffnen steht (Wunsch 2026-11-18: „ohne Engine lande ich am Ende der Partie"): an der
 * gespeicherten Stellung — außer der Baum ist noch die unberührte Partie UND die gespeicherte Stellung ihr letzter Zug. So
 * wurden die Bäume bis 0.991.0 ANGELEGT (das Brett sprang beim Laden ans Partieende); dort liegt keine Arbeit, also beginnt
 * die Analyse in der Ausgangsstellung. Jede andere gespeicherte Stellung bleibt (Fortsetzen, wo man war).
 */
export function blindResumeNode(root: AnalysisNode, current: AnalysisNode, game: AnalysisNode): AnalysisNode {
  const main = mainline(root);
  return main.length > 0 && current === main[main.length - 1] && isUntouchedGame(root, game) ? root : current;
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
 * (Verlust an Gewinnchance aus Sicht des Ziehenden, `classifyMove` — in ohnehin verlorener Stellung kein Fehler), und wo er nicht der beste war, der bessere Zug.
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
    const cls = classifyMove(mb, ma, isBest, before, moverWhite);
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
