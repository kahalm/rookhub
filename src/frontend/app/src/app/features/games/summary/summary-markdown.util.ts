import { Chess } from 'chess.js';

/**
 * Partie-Zusammenfassung (0.913.0): der Markdown-Text, den Claude für eine Vereinspartie schreibt, in Blöcke zerlegen —
 * bewusst nur die Teilmenge, die die Vorgabe des Watchers erlaubt (docs/api/partie-zusammenfassung.md): Überschriften,
 * Absätze, Listen, Zitate, Trennlinie, einfache Tabellen und ```diagram-Blöcke. Gerendert wird über das Template, nie
 * über innerHTML — fremder Text kann so kein HTML einschleusen.
 */

export interface InlinePart { text: string; bold?: boolean; italic?: boolean; code?: boolean; }

/** Ein Diagramm: Stellung nach `ply` Halbzügen der Partie (0 = Anfang) oder eine freie `fen`. */
export interface DiagramSpec {
  ply?: number;
  fen?: string;
  arrows: { from: string; to: string }[];
  caption?: InlinePart[];
  orientation?: 'white' | 'black';
}

export type SummaryBlock =
  | { kind: 'heading'; level: 1 | 2 | 3; parts: InlinePart[] }
  | { kind: 'paragraph'; parts: InlinePart[] }
  | { kind: 'list'; ordered: boolean; items: InlinePart[][] }
  | { kind: 'quote'; parts: InlinePart[] }
  | { kind: 'rule' }
  | { kind: 'table'; head: InlinePart[][]; rows: InlinePart[][][] }
  | { kind: 'diagram'; spec: DiagramSpec }
  /** `> **Merksatz:** <Kennung oder Wortlaut>` — nur Sätze der kuratierten Liste werden gezeigt, mit (i). */
  | { kind: 'maxim'; value: string }
  /** ```book-Block der „Empfohlenen Literatur“: Buch und warum es passt. */
  | { kind: 'book'; id: number; why: string };

/** `**fett**`, `*kursiv*` / `_kursiv_`, `` `Code` `` — nicht verschachtelt, unvollständige Zeichen bleiben Text. */
export function parseInline(text: string): InlinePart[] {
  const parts: InlinePart[] = [];
  const re = /\*\*(.+?)\*\*|`([^`]+)`|(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])|(?<!\w)_(?!\s)(.+?)(?<!\s)_(?!\w)/g;
  let last = 0;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    if (m.index > last) parts.push({ text: text.slice(last, m.index) });
    if (m[1] !== undefined) parts.push({ text: m[1], bold: true });
    else if (m[2] !== undefined) parts.push({ text: m[2], code: true });
    else parts.push({ text: m[3] ?? m[4], italic: true });
    last = m.index + m[0].length;
  }
  if (last < text.length) parts.push({ text: text.slice(last) });
  return parts;
}

const SQUARE = /^[a-h][1-8]$/;

function parseDiagram(lines: string[]): DiagramSpec | null {
  const spec: DiagramSpec = { arrows: [] };
  for (const raw of lines) {
    const i = raw.indexOf(':');
    if (i < 0) continue;
    const key = raw.slice(0, i).trim().toLowerCase();
    const value = raw.slice(i + 1).trim();
    if (key === 'ply') {
      const n = Number(value);
      if (Number.isInteger(n) && n >= 0) spec.ply = n;
    } else if (key === 'fen') {
      spec.fen = value;
    } else if (key === 'arrows' || key === 'arrow') {
      for (const a of value.split(/[\s,]+/)) {
        const from = a.slice(0, 2), to = a.slice(2, 4);
        if (SQUARE.test(from) && SQUARE.test(to)) spec.arrows.push({ from, to });
      }
    } else if (key === 'caption') {
      spec.caption = parseInline(value);
    } else if (key === 'orientation' && (value === 'white' || value === 'black')) {
      spec.orientation = value;
    }
  }
  return spec.ply !== undefined || spec.fen ? spec : null;
}

function splitRow(line: string): string[] {
  return line.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map(c => c.trim());
}

export function parseSummary(markdown: string): SummaryBlock[] {
  const lines = (markdown ?? '').replace(/\r\n?/g, '\n').split('\n');
  const blocks: SummaryBlock[] = [];
  let para: string[] = [];
  const flush = () => {
    if (para.length) blocks.push({ kind: 'paragraph', parts: parseInline(para.join(' ')) });
    para = [];
  };
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const t = line.trim();
    if (t === '') { flush(); continue; }
    const fence = /^```\s*(\w*)/.exec(t);
    if (fence) {
      flush();
      const body: string[] = [];
      while (++i < lines.length && !lines[i].trim().startsWith('```')) body.push(lines[i]);
      if (fence[1].toLowerCase() === 'diagram') {
        const spec = parseDiagram(body);
        if (spec) blocks.push({ kind: 'diagram', spec });
      } else if (fence[1].toLowerCase() === 'book') {
        const kv = new Map(body.map(l => [l.slice(0, l.indexOf(':')).trim().toLowerCase(), l.slice(l.indexOf(':') + 1).trim()]));
        const id = Number(kv.get('id'));
        if (id > 0) blocks.push({ kind: 'book', id, why: kv.get('why') ?? '' });
      } else if (body.length) {
        blocks.push({ kind: 'paragraph', parts: [{ text: body.join('\n'), code: true }] });
      }
      continue;
    }
    const h = /^(#{1,6})\s+(.*)$/.exec(t);
    if (h) {
      flush();
      blocks.push({ kind: 'heading', level: Math.min(h[1].length, 3) as 1 | 2 | 3, parts: parseInline(h[2]) });
      continue;
    }
    if (/^(-{3,}|\*{3,}|_{3,})$/.test(t)) { flush(); blocks.push({ kind: 'rule' }); continue; }
    if (t.startsWith('>')) {
      flush();
      const q: string[] = [];
      while (i < lines.length && lines[i].trim().startsWith('>')) q.push(lines[i++].trim().replace(/^>\s?/, ''));
      i--;
      const maxim = /^\*{0,2}Merksatz:\*{0,2}\s*(.+)$/.exec(q.join(' ').trim());
      blocks.push(maxim ? { kind: 'maxim', value: maxim[1].trim() } : { kind: 'quote', parts: parseInline(q.join(' ')) });
      continue;
    }
    const item = /^([-*+]|\d+[.)])\s+(.*)$/;
    if (item.test(t)) {
      flush();
      const ordered = /^\d/.test(t);
      const items: InlinePart[][] = [];
      while (i < lines.length) {
        const cur = lines[i].trim();
        const m = item.exec(cur);
        if (m && /^\d/.test(m[1]) === ordered) { items.push(parseInline(m[2])); i++; continue; }
        // eingerückte Fortsetzungszeile gehört zum letzten Punkt
        if (cur !== '' && /^\s{2,}/.test(lines[i]) && items.length) {
          items[items.length - 1].push({ text: ' ' }, ...parseInline(cur));
          i++;
          continue;
        }
        break;
      }
      i--;
      blocks.push({ kind: 'list', ordered, items });
      continue;
    }
    if (t.startsWith('|') && i + 1 < lines.length && /^\|?\s*:?-{2,}/.test(lines[i + 1].trim())) {
      flush();
      const head = splitRow(t).map(parseInline);
      i += 2;
      const rows: InlinePart[][][] = [];
      while (i < lines.length && lines[i].trim().startsWith('|')) rows.push(splitRow(lines[i++]).map(parseInline));
      i--;
      blocks.push({ kind: 'table', head, rows });
      continue;
    }
    para.push(t);
  }
  flush();
  return blocks;
}

/** Stellungen der Partie: `fens[n]` = nach n Halbzügen, `lastMoves[n]` = der Zug dorthin. Ungültiges PGN → nur Anfang. */
export function positionsOf(pgn: string): { fens: string[]; lastMoves: ([string, string] | undefined)[]; sans: string[] } {
  const chess = new Chess();
  try { chess.loadPgn(pgn ?? ''); } catch { /* nur Anfangsstellung */ }
  const history = chess.history({ verbose: true });
  const fens = [history.length ? history[0].before : chess.fen()];
  const lastMoves: ([string, string] | undefined)[] = [undefined];
  for (const m of history) { fens.push(m.after); lastMoves.push([m.from, m.to]); }
  return { fens, lastMoves, sans: history.map(m => m.san) };
}

/** Halbzug → „23.“ bzw. „23…“ (Zugnummer der Stellung NACH diesem Halbzug, für die Bildunterschrift). */
export function moveLabel(ply: number, san?: string): string {
  if (ply <= 0) return '';
  const n = Math.ceil(ply / 2);
  return ply % 2 === 1 ? `${n}.${san ?? ''}` : `${n}…${san ?? ''}`;
}
