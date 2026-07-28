/**
 * Analysebaum des Kalkulations-Modus — reine Datenlogik, kein Angular, keine chess.js-Abhängigkeit
 * (Züge werden von der Komponente validiert und fertig hereingegeben).
 *
 * Modell: ein Baum mit EINEM Wurzelknoten (die Ausgangsstellung, `san === ''`). Jeder weitere
 * Knoten ist ein Halbzug — der Nutzer klickt für BEIDE Seiten, es gibt also keine „eigene" Farbe.
 * Eine „Linie" ist ein Pfad Wurzel→Blatt; eine „Abzweigung" entsteht dadurch, dass an einem Knoten
 * mitten in einer Linie ein zweiter Kindzug angelegt wird.
 *
 * Serialisiert wird der Baum 1:1 als JSON (Feld `version` für spätere Formatwechsel). Der Server
 * behandelt ihn als opak, siehe `CalculationService`.
 */

/** Zugbewertungen (Suffix am Zug). */
export const CALC_GLYPHS = ['!!', '!', '!?', '?!', '?', '??'] as const;
export type CalcGlyph = typeof CALC_GLYPHS[number];

/** Stellungsbewertungen (Standard-Schachsymbole, hinter dem Zug). */
export const CALC_EVALS = ['=', '⩲', '⩱', '±', '∓', '+−', '−+', '∞', '⯹', '→', '↑', '⇆', '⨀'] as const;
export type CalcEval = typeof CALC_EVALS[number];

/**
 * i18n-Namen der Symbole („+−" = „Weiß gewinnt"). Die Symbole selbst taugen nicht als
 * Übersetzungs-Schlüssel (Sonderzeichen im Pfad `calc.eval.…`), darum je Symbol ein Slug.
 * Vollständigkeit erzwingt `Record<CalcGlyph|CalcEval, string>` — ein neues Symbol ohne Name
 * bricht dann den Build statt still einen leeren Tooltip zu zeigen.
 */
export const CALC_GLYPH_KEYS: Record<CalcGlyph, string> = {
  '!!': 'brilliant',
  '!': 'good',
  '!?': 'interesting',
  '?!': 'dubious',
  '?': 'mistake',
  '??': 'blunder',
};

export const CALC_EVAL_KEYS: Record<CalcEval, string> = {
  '=': 'equal',
  '⩲': 'whiteSlight',
  '⩱': 'blackSlight',
  '±': 'whiteClear',
  '∓': 'blackClear',
  '+−': 'whiteWinning',
  '−+': 'blackWinning',
  '∞': 'unclear',
  '⯹': 'compensation',
  '→': 'attack',
  '↑': 'initiative',
  '⇆': 'counterplay',
  '⨀': 'zugzwang',
};

/** Übersetzungs-Schlüssel für die Bedeutung eines Zug-Symbols. */
export function glyphNameKey(glyph: CalcGlyph): string {
  return `calc.glyph.${CALC_GLYPH_KEYS[glyph]}`;
}

/** Übersetzungs-Schlüssel für die Bedeutung einer Stellungsbewertung. */
export function evalNameKey(evaluation: CalcEval): string {
  return `calc.eval.${CALC_EVAL_KEYS[evaluation]}`;
}

export interface CalcNode {
  id: number;
  /** null nur bei der Wurzel. */
  parentId: number | null;
  /** Zug in SAN; Leerstring bei der Wurzel. */
  san: string;
  /** Zug in UCI (für Dedupe/Wiedergabe); Leerstring bei der Wurzel. */
  uci: string;
  /** Stellung NACH diesem Zug (bei der Wurzel: die Ausgangsstellung). */
  fen: string;
  /** Zugbewertung (`??`, `!` …). */
  glyph?: CalcGlyph;
  /** Stellungsbewertung (`⩲`, `∞` …). */
  evaluation?: CalcEval;
  /** Freier Kommentar; am Blatt = „Kommentar zur Linie". */
  comment?: string;
  childIds: number[];
}

export interface CalcTree {
  version: 1;
  /** Ausgangsstellung, gegen die der Baum gebaut wurde (Wechsel ⇒ Baum passt nicht mehr). */
  startFen: string;
  rootId: number;
  nextId: number;
  nodes: CalcNode[];
}

/** Ein Zug, wie ihn die Komponente nach der Legalitätsprüfung hereingibt. */
export interface CalcMoveInput {
  san: string;
  uci: string;
  fen: string;
}

/** Eine „Linie" = Pfad Wurzel→Blatt (ohne die Wurzel selbst). */
export interface CalcLine {
  leafId: number;
  moves: CalcNode[];
  /** Wie viele führende Züge diese Linie mit der VORHERIGEN Linie teilt (fürs Abblenden). */
  sharedPrefix: number;
}

export const CALC_TREE_VERSION = 1;

export function createTree(startFen: string): CalcTree {
  return {
    version: CALC_TREE_VERSION,
    startFen,
    rootId: 0,
    nextId: 1,
    nodes: [{ id: 0, parentId: null, san: '', uci: '', fen: startFen, childIds: [] }],
  };
}

export function findNode(tree: CalcTree, id: number): CalcNode | undefined {
  return tree.nodes.find(n => n.id === id);
}

/** Wurzel-Knoten (immer vorhanden). */
export function rootNode(tree: CalcTree): CalcNode {
  return findNode(tree, tree.rootId) ?? tree.nodes[0];
}

/** Enthält der Baum außer der Wurzel gar nichts? */
export function isEmpty(tree: CalcTree): boolean {
  return tree.nodes.length <= 1;
}

/**
 * Hängt einen Zug an `parentId`. Existiert dort schon ein Kind mit demselben UCI, wird KEIN
 * Duplikat angelegt, sondern das bestehende Kind zurückgegeben (Doppelklick/erneutes Durchspielen
 * derselben Linie soll den Baum nicht aufblähen).
 */
export function addMove(tree: CalcTree, parentId: number, move: CalcMoveInput): CalcNode {
  const parent = findNode(tree, parentId);
  if (!parent) throw new Error(`unknown node ${parentId}`);
  const existing = parent.childIds
    .map(id => findNode(tree, id))
    .find(n => n?.uci === move.uci);
  if (existing) return existing;

  const node: CalcNode = {
    id: tree.nextId++,
    parentId,
    san: move.san,
    uci: move.uci,
    fen: move.fen,
    childIds: [],
  };
  tree.nodes.push(node);
  parent.childIds.push(node.id);
  return node;
}

/** Pfad von der Wurzel bis `id` (inklusive Wurzel und `id`). */
export function pathToRoot(tree: CalcTree, id: number): CalcNode[] {
  const out: CalcNode[] = [];
  let cur = findNode(tree, id);
  const guard = tree.nodes.length + 1;
  while (cur && out.length <= guard) {
    out.unshift(cur);
    cur = cur.parentId === null ? undefined : findNode(tree, cur.parentId);
  }
  return out;
}

/** Halbzug-Tiefe eines Knotens (Wurzel = 0). */
export function depthOf(tree: CalcTree, id: number): number {
  return Math.max(0, pathToRoot(tree, id).length - 1);
}

/** Alle Blätter in stabiler Reihenfolge (Tiefensuche entlang der Anlege-Reihenfolge). */
export function leafIds(tree: CalcTree): number[] {
  const out: number[] = [];
  const walk = (id: number): void => {
    const node = findNode(tree, id);
    if (!node) return;
    if (node.childIds.length === 0) {
      if (node.parentId !== null) out.push(node.id);   // die nackte Wurzel ist keine Linie
      return;
    }
    for (const child of node.childIds) walk(child);
  };
  walk(tree.rootId);
  return out;
}

/**
 * Alle Linien (Wurzel→Blatt, ohne Wurzel) in Anlege-Reihenfolge, jeweils mit der Anzahl der mit
 * der vorherigen Linie geteilten führenden Züge — damit die Anzeige den gemeinsamen Vorlauf
 * abblenden und den Blick auf die Abzweigung lenken kann.
 */
export function lines(tree: CalcTree): CalcLine[] {
  const out: CalcLine[] = [];
  let previous: CalcNode[] = [];
  for (const leafId of leafIds(tree)) {
    const moves = pathToRoot(tree, leafId).slice(1);
    let shared = 0;
    while (shared < moves.length && shared < previous.length && moves[shared].id === previous[shared].id) shared++;
    out.push({ leafId, moves, sharedPrefix: shared });
    previous = moves;
  }
  return out;
}

/** Entfernt einen Knoten samt Teilbaum. Gibt die Id des Elternknotens zurück (Wurzel = nicht löschbar). */
export function removeSubtree(tree: CalcTree, id: number): number {
  const node = findNode(tree, id);
  if (!node || node.parentId === null) return tree.rootId;
  const doomed = new Set<number>();
  const collect = (nid: number): void => {
    doomed.add(nid);
    findNode(tree, nid)?.childIds.forEach(collect);
  };
  collect(id);
  const parent = findNode(tree, node.parentId)!;
  parent.childIds = parent.childIds.filter(cid => cid !== id);
  tree.nodes = tree.nodes.filter(n => !doomed.has(n.id));
  return parent.id;
}

/**
 * Löscht eine ganze Linie, ohne mit anderen Linien geteilte Züge anzutasten: gelöscht wird ab dem
 * OBERSTEN Knoten, unterhalb dessen sich nichts mehr verzweigt (also ab der letzten Abzweigung).
 * Gibt die Id des dann aktuellen Knotens (= Elternknoten des gelöschten Astes) zurück.
 */
export function removeLine(tree: CalcTree, leafId: number): number {
  const leaf = findNode(tree, leafId);
  if (!leaf || leaf.parentId === null) return tree.rootId;
  // So weit nach oben wandern, wie der Elternknoten NUR dieses eine Kind hat: dann gehört er
  // ausschließlich zu dieser Linie und darf mit weg. Beim ersten Elternknoten mit mehreren Kindern
  // (= Abzweigung, von anderen Linien geteilt) stehen bleiben.
  let cut: CalcNode = leaf;
  for (;;) {
    const parentId = cut.parentId;
    const parent = parentId === null ? undefined : findNode(tree, parentId);
    if (!parent || parent.parentId === null || parent.childIds.length > 1) break;
    cut = parent;
  }
  return removeSubtree(tree, cut.id);
}

export function setGlyph(tree: CalcTree, id: number, glyph: CalcGlyph | undefined): void {
  const node = findNode(tree, id);
  if (!node || node.parentId === null) return;      // die Ausgangsstellung selbst hat keinen Zug
  if (glyph && node.glyph === glyph) { delete node.glyph; return; }   // gleiches Symbol = Umschalter
  if (glyph) node.glyph = glyph; else delete node.glyph;
}

export function setEvaluation(tree: CalcTree, id: number, evaluation: CalcEval | undefined): void {
  const node = findNode(tree, id);
  if (!node || node.parentId === null) return;
  if (evaluation && node.evaluation === evaluation) { delete node.evaluation; return; }
  if (evaluation) node.evaluation = evaluation; else delete node.evaluation;
}

export function setComment(tree: CalcTree, id: number, comment: string): void {
  const node = findNode(tree, id);
  if (!node) return;
  const text = comment.trim();
  if (text) node.comment = text; else delete node.comment;
}

// ===== Zugnummern =========================================================

/** Zieht Seite am Zug + Zugnummer aus einer FEN (defensiv: Standardwerte bei kaputter FEN). */
export function fenTurnInfo(fen: string): { whiteToMove: boolean; moveNumber: number } {
  const parts = (fen || '').trim().split(/\s+/);
  const whiteToMove = parts[1] !== 'b';
  const parsed = parseInt(parts[5] ?? '1', 10);
  return { whiteToMove, moveNumber: Number.isFinite(parsed) && parsed > 0 ? parsed : 1 };
}

/**
 * Zugnummern-Präfix für den Halbzug `ply` (0-basiert, gezählt ab der Ausgangsstellung).
 * Weiß bekommt `4.`, Schwarz nur dann `4…`, wenn der Halbzug eine Linie/Anzeige ERÖFFNET
 * (`startsDisplay`) — mitten in der Linie steht hinter dem Weißzug ohnehin schon die Nummer.
 */
export function plyPrefix(startFen: string, ply: number, startsDisplay: boolean): string {
  const { whiteToMove, moveNumber } = fenTurnInfo(startFen);
  const isWhiteMove = whiteToMove ? ply % 2 === 0 : ply % 2 === 1;
  const number = moveNumber + Math.floor((ply + (whiteToMove ? 0 : 1)) / 2);
  if (isWhiteMove) return `${number}.`;
  return startsDisplay ? `${number}…` : '';
}

/** Ist an der Stellung `fen` Weiß am Zug? (Anzeige „Am Zug".) */
export function whiteToMove(fen: string): boolean {
  return fenTurnInfo(fen).whiteToMove;
}

// ===== Serialisierung =====================================================

export function serializeTree(tree: CalcTree): string {
  return JSON.stringify(tree);
}

/**
 * Liest einen gespeicherten Baum. Gibt `null` zurück, wenn er fehlt, unlesbar ist, aus einer
 * anderen Formatversion stammt oder zu einer ANDEREN Ausgangsstellung gehört (z. B. weil das Buch
 * neu importiert wurde) — der Aufrufer beginnt dann mit einem frischen Baum.
 */
export function deserializeTree(json: string | null | undefined, expectedStartFen: string): CalcTree | null {
  if (!json) return null;
  let raw: unknown;
  try { raw = JSON.parse(json); } catch { return null; }
  if (!raw || typeof raw !== 'object') return null;
  const tree = raw as Partial<CalcTree>;
  if (tree.version !== CALC_TREE_VERSION) return null;
  if (!Array.isArray(tree.nodes) || tree.nodes.length === 0) return null;
  if (tree.startFen !== expectedStartFen) return null;
  const rootId = typeof tree.rootId === 'number' ? tree.rootId : 0;
  if (!tree.nodes.some(n => n && n.id === rootId)) return null;
  const maxId = tree.nodes.reduce((m, n) => Math.max(m, n?.id ?? 0), 0);
  return {
    version: CALC_TREE_VERSION,
    startFen: tree.startFen,
    rootId,
    nextId: Math.max(typeof tree.nextId === 'number' ? tree.nextId : 0, maxId + 1),
    nodes: tree.nodes.map(n => ({
      id: n.id,
      parentId: n.parentId ?? null,
      san: n.san ?? '',
      uci: n.uci ?? '',
      fen: n.fen ?? '',
      glyph: n.glyph,
      evaluation: n.evaluation,
      comment: n.comment,
      childIds: Array.isArray(n.childIds) ? [...n.childIds] : [],
    })),
  };
}
