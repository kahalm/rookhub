import { Chess } from 'chess.js';

/**
 * Zugbaum des Analysebretts (0.604.0, Wunsch 2026-09-29 mit Screenshot des Lichess-Analysebretts: „so hätt ichs bei uns
 * auch gern in der Analyse — inkl. der Variationen + Hauptlinie — + Rechtsklick Variante hochstufen/löschen"). Rein, ohne
 * Angular: Knoten anlegen, Linie durch einen Knoten, Hauptvariante/hochstufen/löschen, (De-)Serialisierung für den
 * Verlauf, PGN mit Varianten lesen und die Zugtabelle im Stil von Lichess.
 *
 * `children[0]` ist immer die FORTSETZUNG (Hauptlinie ab hier), alle weiteren sind Varianten.
 */
export interface AnalysisNode {
  san: string;
  uci: string;
  fen: string;
  children: AnalysisNode[];
  parent: AnalysisNode | null;
  /** Mit Stern markiert (auch die Wurzel = Ausgangsstellung). */
  starred?: boolean;
  /** Zuletzt gesehene Bewertung der Engine in dieser Stellung (Weiß-Sicht, „+0.25"). */
  evalText?: string;
}

/** Ein Knoten in der FLACHEN Form des Verlaufs (Server `AnalysisTreeNodeDto`): `p` = Index des Elternknotens davor
 *  (-1 = Ausgangsstellung), `u` = UCI, `s` = Stern, `e` = Bewertung. Die Reihenfolge der Geschwister ist die der Liste.
 *  Flach, weil ein verschachtelter Baum bei langen Partien an der Schachtelungsgrenze des JSON-Lesers scheitert. */
export interface AnalysisTreeNodeDto { p: number; u: string; s?: boolean | null; e?: string | null; }
export interface AnalysisTreeDto { s?: boolean | null; n?: AnalysisTreeNodeDto[] | null; }

export function createRoot(fen: string): AnalysisNode {
  return { san: '', uci: '', fen, children: [], parent: null };
}

/** Den Zug unter `parent` einhängen — gibt es ihn dort schon, wird der vorhandene genommen. */
export function addMove(parent: AnalysisNode, move: { san: string; uci: string; fen: string }): AnalysisNode {
  const existing = parent.children.find(c => c.uci === move.uci);
  if (existing) return existing;
  const node: AnalysisNode = { san: move.san, uci: move.uci, fen: move.fen, children: [], parent };
  parent.children.push(node);
  return node;
}

/** Einen UCI-Zug in der Stellung von `parent` spielen und einhängen; `null`, wenn er dort nicht geht. */
export function playUci(parent: AnalysisNode, uci: string): AnalysisNode | null {
  try {
    const c = new Chess(parent.fen);
    const mv = c.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci.length > 4 ? uci[4] : undefined });
    return addMove(parent, { san: mv.san, uci: mv.from + mv.to + (mv.promotion ?? ''), fen: c.fen() });
  } catch { return null; }
}

/** Einen SAN-Zug spielen und einhängen; `null`, wenn er dort nicht geht. */
export function playSan(parent: AnalysisNode, san: string): AnalysisNode | null {
  try {
    const c = new Chess(parent.fen);
    const mv = c.move(san);
    return addMove(parent, { san: mv.san, uci: mv.from + mv.to + (mv.promotion ?? ''), fen: c.fen() });
  } catch { return null; }
}

/** Die Knoten von der Wurzel (ausschließlich) bis `node` (einschließlich). */
export function pathTo(node: AnalysisNode): AnalysisNode[] {
  const out: AnalysisNode[] = [];
  for (let n: AnalysisNode | null = node; n && n.parent; n = n.parent) out.push(n);
  return out.reverse();
}

/** Die Linie, in der `node` steht: der Weg dorthin und danach die Fortsetzung (`children[0]`). */
export function lineThrough(node: AnalysisNode): AnalysisNode[] {
  const line = pathTo(node);
  for (let n = node.children[0]; n; n = n.children[0]) line.push(n);
  return line;
}

export function mainline(root: AnalysisNode): AnalysisNode[] {
  const out: AnalysisNode[] = [];
  for (let n = root.children[0]; n; n = n.children[0]) out.push(n);
  return out;
}

export function rootOf(node: AnalysisNode): AnalysisNode {
  let n = node;
  while (n.parent) n = n.parent;
  return n;
}

/** Liegt `node` auf der Hauptlinie (jeder Schritt ist `children[0]`)? */
export function isMainline(node: AnalysisNode): boolean {
  for (let n = node; n.parent; n = n.parent) if (n.parent.children[0] !== n) return false;
  return true;
}

/** Zur Hauptvariante machen: an jeder Verzweigung auf dem Weg dorthin rückt der Zweig nach vorn. */
export function makeMainline(node: AnalysisNode): void {
  for (let n = node; n.parent; n = n.parent) moveToFront(n);
}

/** Um EINE Stufe hochstufen (Lichess „Variante hochstufen"): an der tiefsten Verzweigung, an der der Weg nicht die
 *  Fortsetzung ist, rückt der Zweig nach vorn. Liegt der Knoten schon auf der Hauptlinie, passiert nichts. */
export function promote(node: AnalysisNode): void {
  for (let n = node; n.parent; n = n.parent) {
    if (n.parent.children[0] !== n) { moveToFront(n); return; }
  }
}

/** Den Knoten samt allem dahinter löschen; gibt den Elternknoten zurück. */
export function removeNode(node: AnalysisNode): AnalysisNode | null {
  const parent = node.parent;
  if (!parent) return null;
  parent.children = parent.children.filter(c => c !== node);
  node.parent = null;
  return parent;
}

/** Liegt `node` in dem Teilbaum unter `top` (oder ist es selbst)? */
export function isWithin(node: AnalysisNode, top: AnalysisNode): boolean {
  for (let n: AnalysisNode | null = node; n; n = n.parent) if (n === top) return true;
  return false;
}

function moveToFront(n: AnalysisNode): void {
  const p = n.parent!;
  const i = p.children.indexOf(n);
  if (i > 0) { p.children.splice(i, 1); p.children.unshift(n); }
}

/** Kindindizes von der Wurzel bis `node` — so merkt sich der Verlauf, wo man stand. */
export function indexPath(node: AnalysisNode): number[] {
  return pathTo(node).map(n => n.parent!.children.indexOf(n));
}

/** Knoten zu einem Index-Weg; bricht an der ersten unbekannten Stelle ab (dann der letzte gültige). */
export function nodeAtPath(root: AnalysisNode, path: readonly number[]): AnalysisNode {
  let n = root;
  for (const i of path) {
    const next = n.children[i];
    if (!next) break;
    n = next;
  }
  return n;
}

/** Alle markierten Knoten, Hauptlinie zuerst, sonst in Baum-Reihenfolge (Wurzel ganz vorn). */
export function starredNodes(root: AnalysisNode): AnalysisNode[] {
  const out: AnalysisNode[] = [];
  const walk = (n: AnalysisNode) => {
    if (n.starred) out.push(n);
    n.children.forEach(walk);
  };
  walk(root);
  return out;
}

export function countNodes(root: AnalysisNode): number {
  let count = 0;
  const walk = (n: AnalysisNode) => { count++; n.children.forEach(walk); };
  root.children.forEach(walk);
  return count;
}

// ── Verlauf ──────────────────────────────────────────────────────────────────────────────────

/** Der Baum in der flachen Form (Tiefe zuerst, Fortsetzung vor den Varianten) samt Index von `current` (-1 = Wurzel
 *  oder nicht gefunden). */
export function toDto(root: AnalysisNode, current?: AnalysisNode | null): { tree: AnalysisTreeDto; current: number } {
  const n: AnalysisTreeNodeDto[] = [];
  let cur = -1;
  const walk = (node: AnalysisNode, p: number) => {
    for (const child of node.children) {
      const i = n.length;
      const d: AnalysisTreeNodeDto = { p, u: child.uci };
      if (child.starred) d.s = true;
      if (child.evalText) d.e = child.evalText;
      n.push(d);
      if (child === current) cur = i;
      walk(child, i);
    }
  };
  walk(root, -1);
  const tree: AnalysisTreeDto = { n };
  if (root.starred) tree.s = true;
  return { tree, current: cur };
}

/** Aus dem Verlauf zurück — Züge, die in ihrer Stellung nicht gehen, fallen samt Teilbaum weg. `nodes[i]` ist der
 *  Knoten zum Eintrag `i` (null, wenn er wegfiel). */
export function fromDto(fen: string, tree: AnalysisTreeDto | null | undefined): { root: AnalysisNode; nodes: (AnalysisNode | null)[] } {
  const root = createRoot(fen);
  if (tree?.s) root.starred = true;
  const nodes: (AnalysisNode | null)[] = [];
  for (const d of tree?.n ?? []) {
    const parent = d.p === -1 ? root : d.p >= 0 && d.p < nodes.length ? nodes[d.p] : null;
    const node = parent && d.u ? playUci(parent, d.u) : null;
    if (node && d.s) node.starred = true;
    if (node && d.e) node.evalText = d.e;
    nodes.push(node);
  }
  return { root, nodes };
}

// ── Beschriftung ─────────────────────────────────────────────────────────────────────────────

/** Die Stellung VOR dem Zug des Knotens. */
function fenBefore(n: AnalysisNode): string {
  return n.parent ? n.parent.fen : n.fen;
}

function moveNumberOf(fen: string): { number: number; white: boolean } {
  const parts = fen.split(' ');
  return { number: parseInt(parts[5] ?? '1', 10) || 1, white: parts[1] !== 'b' };
}

/** „12.Nf3" bzw. „12...Nf6". */
export function numberedSan(n: AnalysisNode): string {
  const { number, white } = moveNumberOf(fenBefore(n));
  return (white ? `${number}.` : `${number}...`) + n.san;
}

// ── Zugtabelle im Stil von Lichess ───────────────────────────────────────────────────────────

/** Eine Zeile der Tabelle: Zugnummer, Weiß, Schwarz — `null` = leer, „…" steht für eine unterbrochene Zeile. */
export interface MoveRow { kind: 'row'; number: number; white: AnalysisNode | 'gap' | null; black: AnalysisNode | 'gap' | null; }

/** Ein Stück einer Variante: ein Zug (mit oder ohne Nummer) oder eine Klammer für eine Untervariante. */
export type VariationToken =
  | { kind: 'move'; node: AnalysisNode; label: string }
  | { kind: 'open' }
  | { kind: 'close' };

/** Der Block unter einem Zug mit Varianten: je Variante eine Zeile. */
export interface VariationBlock { kind: 'variations'; lines: VariationToken[][]; }

export type MoveTableItem = MoveRow | VariationBlock;

/** Die Hauptlinie als Tabelle; wo sie sich verzweigt, unterbricht ein Block mit den Varianten die Zeile (wie Lichess:
 *  „1 | e4 | …", dann die Varianten zu 1.e4, dann „1 | … | c5"). */
export function buildMoveTable(root: AnalysisNode): MoveTableItem[] {
  const items: MoveTableItem[] = [];
  let row: MoveRow | null = null;   // offene Zeile, in die noch der schwarze Zug kommen kann
  for (let node = root; node.children.length > 0; node = node.children[0]) {
    const main = node.children[0];
    const { number, white } = moveNumberOf(node.fen);
    if (white) {
      row = { kind: 'row', number, white: main, black: null };
      items.push(row);
    } else if (row && row.number === number && row.black === null) {
      row.black = main;
    } else {
      row = { kind: 'row', number, white: 'gap', black: main };
      items.push(row);
    }
    const alts = node.children.slice(1);
    if (alts.length > 0) {
      // Folgt noch ein schwarzer Zug, steht er nach dem Block in einer eigenen Zeile hinter „…".
      if (white && main.children.length > 0) row.black = 'gap';
      items.push({ kind: 'variations', lines: alts.map(a => variationTokens(a)) });
      row = null;
    }
  }
  return items;
}

/** Eine Variante ab `start` als Zugfolge; die Geschwister eines Zugs der Variante stehen als Untervarianten in
 *  Klammern direkt hinter ihm („2...e6 3.g3 Nc6 (3...Na6 4.f5)"), danach geht es mit Zugnummer weiter. */
export function variationTokens(start: AnalysisNode): VariationToken[] {
  const tokens: VariationToken[] = [];
  let needNumber = true;
  for (let n: AnalysisNode | undefined = start; n; n = n.children[0]) {
    tokens.push({ kind: 'move', node: n, label: moveLabel(n, needNumber) });
    needNumber = false;
    const parent: AnalysisNode | null = n.parent;
    if (n !== start && parent && parent.children[0] === n && parent.children.length > 1) {
      for (const alt of parent.children.slice(1)) {
        tokens.push({ kind: 'open' }, ...variationTokens(alt), { kind: 'close' });
      }
      needNumber = true;
    }
  }
  return tokens;
}

function moveLabel(n: AnalysisNode, needNumber: boolean): string {
  const { number, white } = moveNumberOf(fenBefore(n));
  return white ? `${number}.${n.san}` : needNumber ? `${number}...${n.san}` : n.san;
}

// ── PGN mit Varianten ────────────────────────────────────────────────────────────────────────

export interface ParsedPgnTree { root: AnalysisNode; headers: Record<string, string>; }

/**
 * Ein PGN samt Varianten (RAV) als Baum lesen — nur die ERSTE Partie. Kommentare, NAGs, Bewertungszeichen, Zugnummern
 * und Wörter, die kein Zug sein können („e.p."), fallen weg; ein Zug, der in seiner Stellung nicht geht, beendet seine
 * Variante (der Rest der Variante wird übersprungen, die Linie davor bleibt). `null`, wenn die Hauptlinie gar keinen Zug
 * hat oder die Ausgangsstellung unlesbar ist.
 */
export function parsePgnTree(pgn: string): ParsedPgnTree | null {
  const headers: Record<string, string> = {};
  const headerRe = /^\s*\[(\w+)\s+"((?:[^"\\]|\\.)*)"\]\s*$/gm;
  let m: RegExpExecArray | null;
  const firstMoves = pgn.search(/^\s*[^\s[]/m);   // Kopfzeilen einer zweiten Partie gehören nicht dazu
  while ((m = headerRe.exec(pgn))) {
    if (firstMoves >= 0 && m.index > firstMoves) break;
    if (!(m[1] in headers)) headers[m[1]] = m[2].replace(/\\(.)/g, '$1');
  }
  const fen = headers['FEN'] || new Chess().fen();
  try { new Chess(fen); } catch { return null; }
  const moveText = pgn.replace(/^\s*\[[^\]]*\]\s*$/gm, ' ');
  const root = createRoot(fen);
  let cursor: AnalysisNode = root;
  let last: AnalysisNode | null = null;   // der zuletzt gespielte Knoten — eine Variante ersetzt SEINEN Zug
  let dead = 0;                             // > 0: nach einem ungültigen Zug bis zur passenden Klammer überspringen
  const stack: { cursor: AnalysisNode; last: AnalysisNode | null; dead: number }[] = [];
  for (const t of tokenize(moveText)) {
    if (t === END) { if (stack.length === 0) break; continue; }
    if (t === '(') {
      stack.push({ cursor, last, dead });
      if (dead) { dead++; continue; }
      cursor = last?.parent ?? cursor;      // die Variante setzt dort an, wo der zuletzt gespielte Zug herkam
      last = null;
      continue;
    }
    if (t === ')') {
      const s = stack.pop();
      if (s) { cursor = s.cursor; last = s.last; dead = s.dead; }
      continue;
    }
    if (dead) continue;
    const node = playSan(cursor, t);
    if (!node) { dead = 1; continue; }
    last = node;
    cursor = node;
  }
  if (root.children.length === 0) return null;
  return { root, headers };
}

/** Ende einer Partie (Ergebnis) im Zugtext. */
const END = '\u0000end';
/** Was als Zug in Frage kommt: SAN (auch ohne „=" bei der Umwandlung), lange Notation, Rochade mit O oder 0. */
const MOVE_RE = /^(?:[PKQRBN]?[a-h]?[1-8]?x?-?[a-h][1-8](?:=?[QRBNqrbn])?|O-O(?:-O)?|0-0(?:-0)?)[+#]?$/;

function tokenize(text: string): string[] {
  const out: string[] = [];
  let i = 0;
  while (i < text.length) {
    const ch = text[i];
    if (ch === '{') { const j = text.indexOf('}', i); i = j < 0 ? text.length : j + 1; continue; }
    if (ch === ';') { const j = text.indexOf('\n', i); i = j < 0 ? text.length : j + 1; continue; }
    if (ch === '(' || ch === ')') { out.push(ch); i++; continue; }
    if (/\s/.test(ch)) { i++; continue; }
    let j = i;
    while (j < text.length && !/[\s(){};]/.test(text[j])) j++;
    const word = text.slice(i, j);
    i = j;
    if (/^(1-0|0-1|1\/2-1\/2|\*)$/.test(word)) { out.push(END); continue; }   // Ergebnis
    const san = word.replace(/^\d+\.(\.\.)?/, '').replace(/^\.+/, '').replace(/[!?]+$/, '');
    if (!MOVE_RE.test(san)) continue;                                          // Zugnummer, NAG, „e.p." …
    out.push(san.replace(/^0-0-0/, 'O-O-O').replace(/^0-0/, 'O-O'));
  }
  return out;
}
