import { Chess } from 'chess.js';

/** Der Eröffnungsbaum eines Buchs, wie ihn `GET /api/ebooks/{id}/tree` liefert (0.881.0): `[parentIndex, san]` je Eintrag,
 *  Eintrag i = Knoten i+1, Knoten 0 = Grundstellung, der Elternknoten liegt immer davor. */
export type EbookTreeWire = [number, string][];

/** Ein Zug im Baum zur Anzeige: Knoten, SAN, Zugnummer-Text („5.“ bzw. „5…“) und Zahl der Buchlinien darunter. */
export interface EbookTreeMove {
  node: number;
  san: string;
  label: string;
  lines: number;
}

/** Ergebnis von {@link EbookOpeningTree.find}: Knoten der Stellung (oder `null`) und ob das Diagramm gedreht gesetzt ist. */
export interface EbookTreeHit { node: number | null; rotated: boolean; }

/** Höchstens so viele Züge gehen in ein Repertoire (PGN mit Varianten) — darüber wird der Upload zäh. */
export const TREE_EXPORT_MAX_NODES = 20_000;

/** Figurenstellung einer FEN (erstes Feld) — Zugrecht und Zähler bleiben außen vor, damit Diagramm-FENs passen. */
export function placementOf(fen: string): string {
  return (fen ?? '').trim().split(/\s+/)[0] ?? '';
}

/** Dieselbe Figurenstellung um 180° gedreht (Diagramm „von Schwarz aus“ gesetzt): Zeichenfolge rückwärts. */
export function rotatedPlacement(placement: string): string {
  return placement.split('').reverse().join('');
}

/**
 * Der Eröffnungsbaum eines Buchs im Leser (Brett-Modus „Baum“). Baut alle Stellungen einmal mit chess.js auf (Tiefensuche
 * mit EINER Chess-Instanz, Zug + Zurücknehmen — schneller als je Knoten eine FEN zu laden). Ein Zug, der in seiner Stellung
 * nicht geht, fällt samt Teilbaum weg (kaputte Quelle soll den Leser nicht sprengen). Knotennummern bleiben die des Servers.
 */
export class EbookOpeningTree {
  /** Je Knoten: Elternknoten (-1 = Wurzel bzw. ungültig), SAN (chess.js-Schreibweise), FEN, Tiefe, Linien darunter. */
  readonly parent: Int32Array;
  readonly san: string[];
  readonly fen: string[];
  readonly depth: Int32Array;
  readonly lines: Int32Array;
  /** Gültige Kinder je Knoten, meistverzweigte zuerst (bei Gleichstand Buchreihenfolge). */
  readonly children: number[][];
  /** Zahl der gültigen Knoten ohne Grundstellung. */
  readonly size: number;
  private readonly byPlacement = new Map<string, number>();

  constructor(wire: EbookTreeWire) {
    const n = wire.length + 1;
    this.parent = new Int32Array(n).fill(-1);
    this.depth = new Int32Array(n);
    this.lines = new Int32Array(n);
    this.san = new Array<string>(n).fill('');
    this.fen = new Array<string>(n).fill('');
    this.children = Array.from({ length: n }, () => [] as number[]);
    const raw: number[][] = Array.from({ length: n }, () => [] as number[]);
    wire.forEach(([p, s], i) => {
      if (Number.isInteger(p) && p >= 0 && p <= i && typeof s === 'string') raw[p].push(i + 1);
    });

    const chess = new Chess();
    this.fen[0] = chess.fen();
    this.byPlacement.set(placementOf(this.fen[0]), 0);
    let size = 0;
    // Tiefensuche: Stapel aus (Knoten, nächstes Kind); Zug beim Betreten, Zurücknehmen beim Verlassen.
    const stack: { node: number; next: number }[] = [{ node: 0, next: 0 }];
    while (stack.length) {
      const top = stack[stack.length - 1];
      const kids = raw[top.node];
      if (top.next >= kids.length) {
        stack.pop();
        if (stack.length) chess.undo();
        continue;
      }
      const child = kids[top.next++];
      let san: string;
      try { san = chess.move(wire[child - 1][1]).san; } catch { continue; }
      this.parent[child] = top.node;
      this.san[child] = san;
      this.depth[child] = this.depth[top.node] + 1;
      this.fen[child] = chess.fen();
      this.children[top.node].push(child);
      size++;
      const key = placementOf(this.fen[child]);
      const seen = this.byPlacement.get(key);
      if (seen === undefined || this.depth[child] < this.depth[seen]) this.byPlacement.set(key, child);
      stack.push({ node: child, next: 0 });
    }
    this.size = size;

    // Linien = Blätter darunter; Kinder haben immer eine größere Nummer als ihr Elternknoten → rückwärts aufsummieren.
    for (let i = n - 1; i >= 1; i--) {
      if (this.parent[i] < 0) continue;
      if (this.lines[i] === 0) this.lines[i] = 1;
      this.lines[this.parent[i]] += this.lines[i];
    }
    for (const kids of this.children) kids.sort((a, b) => this.lines[b] - this.lines[a] || a - b);
  }

  /** Knoten zu einer Stellung (nur Figurenstellung; die geringste Tiefe gewinnt), auch gedreht gesetzte Diagramme. */
  find(fen: string): EbookTreeHit {
    const p = placementOf(fen);
    const direct = this.byPlacement.get(p);
    if (direct !== undefined) return { node: direct, rotated: false };
    const turned = this.byPlacement.get(rotatedPlacement(p));
    return turned !== undefined ? { node: turned, rotated: true } : { node: null, rotated: false };
  }

  /** Knoten von der Grundstellung bis hierher (ohne Knoten 0). */
  path(node: number): number[] {
    const out: number[] = [];
    for (let n = node; n > 0 && this.parent[n] >= 0; n = this.parent[n]) out.push(n);
    return out.reverse();
  }

  /** „5.“ für einen Zug von Weiß, „5…“ für Schwarz (Knoten in Tiefe d ist der d-te Halbzug ab der Grundstellung). */
  moveNumber(node: number): string {
    const d = this.depth[node];
    return d % 2 === 1 ? `${(d + 1) / 2}.` : `${d / 2}…`;
  }

  move(node: number): EbookTreeMove {
    return { node, san: this.san[node], label: this.moveNumber(node), lines: this.lines[node] };
  }

  /** Knoten im Teilbaum unter `node` (ohne ihn selbst). */
  subtreeSize(node: number): number {
    let count = 0;
    const stack = [...this.children[node]];
    while (stack.length) { const c = stack.pop()!; count++; stack.push(...this.children[c]); }
    return count;
  }

  /** Seite, die in der Mehrheit der Linien unter `node` den letzten Zug macht (wie die Kapitel-Erkennung der Repertoires);
   *  Gleichstand oder keine Linie → Weiß. */
  majorityLastMover(node: number): 'w' | 'b' {
    let w = 0, b = 0;
    const stack = [node];
    while (stack.length) {
      const c = stack.pop()!;
      if (this.children[c].length === 0) {
        if (c === 0) continue;
        if (this.depth[c] % 2 === 1) w++; else b++;
      } else stack.push(...this.children[c]);
    }
    return b > w ? 'b' : 'w';
  }

  /**
   * Zugtext mit Varianten (RAV) für ein Repertoire: die Züge von der Grundstellung bis `from` als Hauptlinie, danach der
   * Teilbaum ab `from` — Hauptfortsetzung ist jeweils das Kind mit den meisten Linien darunter, die übrigen werden Varianten.
   */
  movetext(from: number): string {
    const out: string[] = [];
    for (const n of this.path(from)) out.push(this.label(n, out.length === 0));
    this.writeLine(from, out, out.length === 0);
    return out.join(' ');
  }

  private label(node: number, needNumber: boolean): string {
    const d = this.depth[node];
    if (d % 2 === 1) return `${(d + 1) / 2}. ${this.san[node]}`;
    return needNumber ? `${d / 2}... ${this.san[node]}` : this.san[node];
  }

  /** Hauptlinie ab `node` schreiben, Varianten an jeder Verzweigung (rekursiv nur für Varianten, die Tiefe bleibt klein). */
  private writeLine(node: number, out: string[], needNumber: boolean): void {
    let cur = node;
    let need = needNumber;
    for (;;) {
      const kids = this.children[cur];
      if (kids.length === 0) return;
      out.push(this.label(kids[0], need));
      need = false;
      for (const alt of kids.slice(1)) {
        const sub: string[] = [this.label(alt, true)];
        this.writeLine(alt, sub, false);
        out.push(`(${sub.join(' ')})`);
        need = true;
      }
      cur = kids[0];
    }
  }
}

/** PGN-Kopfwert maskieren (Backslash und Anführungszeichen). */
function header(name: string, value: string): string {
  return `[${name} "${value.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"]`;
}

/**
 * Ein Repertoire-PGN aus dem Baum (eine Partie mit Varianten). `Black` trägt den Namen (= Kapitel im Repertoire),
 * `ChessableColor` die Trainingsfarbe — beides werten Server und Trainer beim Import aus.
 */
export function treeToPgn(tree: EbookOpeningTree, from: number, opts: { bookTitle: string; name: string; color: 'w' | 'b' }): string {
  const heads = [
    header('Event', opts.bookTitle),
    header('Site', 'RookHub'),
    header('Date', '????.??.??'),
    header('Round', '-'),
    header('White', opts.bookTitle),
    header('Black', opts.name),
    header('Result', '*'),
    header('Annotator', 'RookHub book tree'),
    header('ChessableColor', opts.color === 'w' ? 'white' : 'black'),
  ];
  return `${heads.join('\n')}\n\n${tree.movetext(from)} *\n`;
}
