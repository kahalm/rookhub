import { Chess, Square } from 'chess.js';
import { Color, Key } from 'chessground/types';
import { AnalysisNode, createRoot, lineThrough, pathTo, starredNodes } from './analysis-tree';

/**
 * Zuglinien-Modell des Analysebretts (Zerlegung Z22), rein: der Zugbaum (`root`), die Linie durch den aktuellen Knoten
 * (Pfeiltasten laufen auf ihr), die Stelle darauf (`ply`) und der Änderungszähler (`treeVersion`, die Zugliste baut
 * danach neu auf). Die Baum-Operationen selbst liegen in `analysis-tree.ts`.
 */
export class AnalysisLineModel {
  /** Zugbaum (0.604.0): die Wurzel ist die Ausgangsstellung, `children[0]` jeweils die Fortsetzung. */
  root: AnalysisNode;
  /** Die Linie durch den aktuellen Knoten: der Weg dorthin und seine Fortsetzung. */
  line: AnalysisNode[] = [];
  /** Wie weit man auf `line` steht (0 = Ausgangsstellung). */
  ply = 0;
  /** Zählt Änderungen am Baum (Zug, Variante, Stern, Bewertung). */
  treeVersion = 0;
  private starCache: { version: number; root: AnalysisNode | null; list: AnalysisNode[] } = { version: -1, root: null, list: [] };

  constructor(fen: string) { this.root = createRoot(fen); }

  /** Der Knoten, dessen Stellung auf dem Brett steht (die Wurzel = Ausgangsstellung). */
  get currentNode(): AnalysisNode { return this.ply === 0 ? this.root : this.line[this.ply - 1]; }

  /** Auf der Linie springen (geklemmt). */
  goTo(ply: number): void { this.ply = Math.max(0, Math.min(ply, this.line.length)); }

  /** Auf einen beliebigen Knoten springen — die Linie läuft dann durch ihn. */
  goToNode(node: AnalysisNode): void {
    this.line = lineThrough(node);
    this.ply = pathTo(node).length;
  }

  /** Neuer Baum (Laden, Zurücksetzen); auf einen Knoten springt der Aufrufer danach selbst. */
  setRoot(root: AnalysisNode): void {
    this.root = root;
    this.treeVersion++;
  }

  /** Reihenfolge im Baum geändert: die Linie durch `current` neu ziehen, Stelle bleibt. */
  relineThrough(current: AnalysisNode): void { this.line = lineThrough(current); }

  /** Markierte Stellungen, Hauptlinie zuerst — je Baumstand nur einmal gesucht (template-gebunden). */
  get starred(): AnalysisNode[] {
    const c = this.starCache;
    if (c.version !== this.treeVersion || c.root !== this.root) {
      this.starCache = { version: this.treeVersion, root: this.root, list: starredNodes(this.root) };
    }
    return this.starCache.list;
  }

  toggleStar(node: AnalysisNode): void {
    if (node.starred) delete node.starred; else node.starred = true;
    this.treeVersion++;
  }
}

/** Züge nacheinander ab `start` spielen; am ersten, der nicht geht, ist Schluss. Liefert den letzten Knoten. */
export function playLine<M>(start: AnalysisNode, moves: readonly M[], play: (n: AnalysisNode, m: M) => AnalysisNode | null): AnalysisNode {
  let node = start;
  for (const m of moves) {
    const next = play(node, m);
    if (!next) break;
    node = next;
  }
  return node;
}

/** Ergebnis eines Brett-Zugs: kaputte Stellung, illegaler Zug (Brett zurücksetzen) oder der Zug fürs Einhängen. */
export type BoardMoveResult =
  | { kind: 'bad-fen' }
  | { kind: 'illegal' }
  | { kind: 'ok'; mover: Color; move: { san: string; uci: string; fen: string } };

/** Einen Zug vom Brett in der Stellung `fen` ausführen. Die Umwandlungsfigur kommt aus dem Picker, Dame nur als
 *  Rückfall. */
export function boardMove(fen: string, ev: { orig: Key; dest: Key; promotion?: string }): BoardMoveResult {
  let c: Chess;
  try { c = new Chess(fen); } catch { return { kind: 'bad-fen' }; }
  const mover: Color = c.turn() === 'w' ? 'white' : 'black';
  const piece = c.get(ev.orig as Square);
  const isPromo = piece?.type === 'p' && (ev.dest[1] === '8' || ev.dest[1] === '1');
  const promotion = isPromo ? (ev.promotion ?? 'q') : undefined;
  let mv;
  try { mv = c.move({ from: ev.orig, to: ev.dest, promotion }); } catch { return { kind: 'illegal' }; }
  if (!mv) return { kind: 'illegal' };
  return { kind: 'ok', mover, move: { san: mv.san, uci: mv.from + mv.to + (mv.promotion ?? ''), fen: c.fen() } };
}
