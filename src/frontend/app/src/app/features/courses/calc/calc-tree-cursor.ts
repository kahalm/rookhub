import {
  CalcNode, CalcTree, addMove, createTree, findNode, lines, pathToRoot, removeLine, removeSubtree, setComment,
  whiteToMove,
} from './calc-tree.util';

/**
 * Position im Analysebaum des Kalkulations-Modus (Zerlegung Z20, aus `CalculationComponent`): welcher Zug ausgewählt
 * ist, auf welcher Linie man sich „bewegt" (für →/↑/↓), die FEN am Cursor (nur für die Legalitätsprüfung, nie
 * sichtbar) und der Kommentar-Entwurf zum ausgewählten Zug. Reine Logik ohne Angular; Speichern, Festlegung und
 * Neuzeichnen bleiben in der Komponente.
 */
export class CalcTreeCursor {
  tree: CalcTree = createTree('');
  /** Ausgangsstellung (nach dem Vorlauf) — das Brett zeigt IMMER nur diese. */
  startFen = '';
  cursorId = 0;
  /** FEN am aktuellen Cursor. */
  cursorFen = '';
  /** Kommentar-Entwurf zum ausgewählten Zug. */
  comment = '';
  /** Linie, auf der man sich „bewegt" (ihr Blatt). */
  private activeLeafId = 0;

  /** Neuen Baum übernehmen (Stellungswechsel): Cursor auf die Wurzel, Entwurf leer. */
  reset(tree: CalcTree, startFen: string): void {
    this.tree = tree;
    this.startFen = startFen;
    this.cursorId = tree.rootId;
    this.activeLeafId = tree.rootId;
    this.cursorFen = startFen;
    this.comment = '';
  }

  get node(): CalcNode | undefined { return findNode(this.tree, this.cursorId); }
  get atStart(): boolean { return this.cursorId === this.tree.rootId; }
  get lineCount(): number { return lines(this.tree).length; }
  get whiteToMove(): boolean { return whiteToMove(this.cursorFen || this.startFen); }

  setCursor(nodeId: number): void {
    const node = findNode(this.tree, nodeId);
    if (!node) return;
    this.cursorId = node.id;
    this.cursorFen = node.fen || this.startFen;
    this.comment = node.comment ?? '';
    // „Aktive Linie" mitziehen: liegt der Cursor nicht mehr auf ihr, die erste Fortsetzung nehmen.
    const onActiveLine = pathToRoot(this.tree, this.activeLeafId).some(n => n.id === node.id);
    if (!onActiveLine) this.activeLeafId = this.leafUnder(node.id);
  }

  /** Zug hinter dem Cursor anhängen (oder einer vorhandenen Fortsetzung folgen) und dorthin springen. */
  addMove(move: { san: string; uci: string; fen: string }): void {
    const added = addMove(this.tree, this.cursorId, move);
    this.setCursor(added.id);
  }

  /** Neue Linie ab der Ausgangsstellung: (+) setzt den Cursor auf die Wurzel. */
  startNewLine(): void {
    this.setCursor(this.tree.rootId);
  }

  goBack(): void {
    const node = this.node;
    if (node?.parentId != null) this.setCursor(node.parentId);
  }

  goForward(): void {
    const node = this.node;
    if (!node || node.childIds.length === 0) return;
    const path = pathToRoot(this.tree, this.activeLeafId).map(n => n.id);
    this.setCursor(node.childIds.find(id => path.includes(id)) ?? node.childIds[0]);
  }

  /** Linie wechseln (↑/↓): Cursor auf das Blatt der vorherigen/nächsten Linie. */
  switchLine(delta: number): void {
    const all = lines(this.tree);
    if (all.length === 0) return;
    const current = all.findIndex(l => l.leafId === this.activeLeafId);
    const next = ((current < 0 ? 0 : current) + delta + all.length) % all.length;
    this.activeLeafId = all[next].leafId;
    this.setCursor(all[next].leafId);
  }

  /** Ausgewählten Zug samt Fortsetzung löschen; an der Wurzel nichts (Rückgabe false). */
  deleteFromCursor(): boolean {
    if (this.atStart) return false;
    const parentId = removeSubtree(this.tree, this.cursorId);
    this.activeLeafId = this.leafUnder(parentId);
    this.setCursor(parentId);
    return true;
  }

  /** Eine Linie löschen (gemeinsame Züge anderer Linien bleiben). */
  deleteLine(leafId: number): void {
    const cursor = removeLine(this.tree, leafId);
    this.activeLeafId = this.leafUnder(cursor);
    this.setCursor(cursor);
  }

  /** Kommentar eines Zugs setzen; gilt er dem ausgewählten Zug, zieht der Entwurf mit. */
  setComment(nodeId: number, text: string): void {
    setComment(this.tree, nodeId, text);
    if (nodeId === this.cursorId) this.comment = text.trim();
  }

  /** Den Entwurf als Kommentar des ausgewählten Zugs übernehmen. */
  saveComment(): void {
    setComment(this.tree, this.cursorId, this.comment);
  }

  /** Züge (UCI) von der Wurzel bis zu diesem Knoten — für den Analyse-Modus. */
  movesTo(nodeId: number): string[] {
    return pathToRoot(this.tree, nodeId).slice(1).map(n => n.uci).filter(Boolean);
  }

  private leafUnder(nodeId: number): number {
    let node = findNode(this.tree, nodeId);
    while (node && node.childIds.length > 0) node = findNode(this.tree, node.childIds[0]);
    return node?.id ?? nodeId;
  }
}
