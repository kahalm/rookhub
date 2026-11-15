import { Chess } from 'chess.js';
import { KnownPosition, explanationParts } from '../explanation-lines';
import type { LiveMove } from '../live-engine-session';
import type { GameTextSegment } from './summary-game-text.util';

/**
 * Partie-Zusammenfassung (0.913.0): der Markdown-Text, den Claude für eine Vereinspartie schreibt, in Blöcke zerlegen —
 * bewusst nur die Teilmenge, die die Vorgabe des Watchers erlaubt (docs/api/partie-zusammenfassung.md): Überschriften,
 * Absätze, Listen, Zitate, Trennlinie, einfache Tabellen und ```diagram-Blöcke. Gerendert wird über das Template, nie
 * über innerHTML — fremder Text kann so kein HTML einschleusen.
 */

export interface InlinePart {
  text: string; bold?: boolean; italic?: boolean; code?: boolean;
  /** Klickbarer Zug: die Zugfolge ab Partieanfang und wie weit sie bis einschließlich dieses Zugs reicht. */
  line?: LiveMove[]; at?: number;
  /** Link (0.980.0): `text` ist der Linktext. In einem Linktext werden keine Züge klickbar gemacht. */
  link?: SummaryLink;
}

/**
 * Ein Link im Text — nur die Ziele der Erlaubt-Liste ({@link summaryLink}):
 * die Ansicht „Meisterpartie“ dieser Seite (`masterGame` = Kennung im Explorer, `ply` = Halbzug, an dem sie aufgeht) oder
 * eine Partie auf lichess.org (`href`, öffnet in neuem Tab).
 */
export interface SummaryLink { masterGame?: string; ply?: number; href?: string; }

/** Pfad der Ansicht „Meisterpartie“ — Spiegel von `LocalExplorerClient.MasterGamePath` (das Feld `path` von `/api/explorer/games`). */
export const MASTER_GAME_PATH = '/explorer/game/masters/';

const MASTER_GAME_LINK = /^\/explorer\/game\/masters\/([A-Za-z0-9]{8})(?:\?ply=(\d{1,3}))?$/;
const LICHESS_GAME_LINK = /^https:\/\/lichess\.org\/[A-Za-z0-9]{8}(?:\/(?:white|black))?(?:#\d{1,3})?$/;

/**
 * Erlaubt-Liste der Linkziele (Wunsch 2026-11-15: Beispielpartien aus dem Explorer „am besten mit einem Link“). Der Text
 * kommt von Claude bzw. aus fremden Namen — deshalb NUR diese zwei festen Muster, alles andere (andere Adressen,
 * `javascript:`, relative Pfade, Zusätze hinter dem Muster) ist kein Link und bleibt wörtlich stehen:
 *
 * - `/explorer/game/masters/<8 Zeichen a–z, A–Z, 0–9>` mit optionalem `?ply=<Halbzug>` — seitenintern, über den Router;
 * - `https://lichess.org/<8 Zeichen>` mit optionalem `/white`, `/black` und `#<Halbzug>`.
 */
export function summaryLink(target: string): SummaryLink | null {
  const m = MASTER_GAME_LINK.exec(target);
  if (m) return m[2] === undefined ? { masterGame: m[1] } : { masterGame: m[1], ply: Number(m[2]) };
  return LICHESS_GAME_LINK.test(target) ? { href: target } : null;
}

/** `[Text](Ziel)` als Ganzes: Text ohne `]` und Zeilenumbruch, Ziel ohne Leerraum und Klammern. */
const WHOLE_LINK = /^\[([^\]\n]+)\]\(([^()\s]+)\)$/;

/** Ein Stück mit Stil: ist es als Ganzes ein erlaubter Link, wird es einer (`**[Text](Ziel)**`), sonst bleibt es Text. */
function styled(text: string, style: Pick<InlinePart, 'bold' | 'italic'>): InlinePart {
  const m = WHOLE_LINK.exec(text);
  const link = m ? summaryLink(m[2]) : null;
  return link ? { text: m![1], ...style, link } : { text, ...style };
}

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
  /** ```book-Block der „Empfohlenen Literatur“: Buch und warum es passt; `whyParts` = die Begründung mit klickbaren Zügen. */
  | { kind: 'book'; id: number; why: string; whyParts?: InlinePart[] }
  /** „Die Partie“ (Wunsch 2026-11-13): die ganze Zugfolge zum Durchklicken — kommt nie aus dem Markdown, die Seite setzt
   *  sie aus dem PGN ein ({@link withGameBlock}), wenn der Text die Partie nicht selbst erzählt. */
  | { kind: 'game' }
  /** Ein Absatz des verschmolzenen Abschnitts „## Die Partie“ (Wunsch 2026-11-14): Text, Partiezüge und Varianten in
   *  Klammern — entsteht in `mergeGameSection` (summary-game-text.util.ts), nie direkt aus dem Markdown. */
  | { kind: 'gameText'; segments: GameTextSegment[] };

/**
 * `**fett**`, `*kursiv*` / `_kursiv_`, `` `Code` ``, `[Text](Ziel)` — nicht verschachtelt, unvollständige Zeichen bleiben Text.
 * Ein Link wird nur mit einem Ziel der Erlaubt-Liste ({@link summaryLink}) zum Link; sonst bleibt `[Text](Ziel)` wörtlich
 * stehen. Fett oder kursiv um den ganzen Link herum geht (`**[Text](Ziel)**`), Stil IM Linktext nicht.
 */
export function parseInline(text: string): InlinePart[] {
  const parts: InlinePart[] = [];
  const push = (p: InlinePart) => {
    // Nicht erlaubter Link = gewöhnlicher Text: mit dem Nachbarn davor verschmelzen, damit Sätze nicht zerfallen.
    const prev = parts[parts.length - 1];
    if (prev && !p.bold && !p.italic && !p.code && !p.link && !prev.bold && !prev.italic && !prev.code && !prev.link) prev.text += p.text;
    else parts.push(p);
  };
  const re = /\*\*(.+?)\*\*|`([^`]+)`|(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])|(?<!\w)_(?!\s)(.+?)(?<!\s)_(?!\w)|\[([^\]\n]+)\]\(([^()\s]+)\)/g;
  let last = 0;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    if (m.index > last) push({ text: text.slice(last, m.index) });
    if (m[1] !== undefined) push(styled(m[1], { bold: true }));
    else if (m[2] !== undefined) push({ text: m[2], code: true });
    else if (m[5] !== undefined) {
      const link = summaryLink(m[6]);
      push(link ? { text: m[5], link } : { text: m[0] });
    }
    else push(styled(m[3] ?? m[4], { italic: true }));
    last = m.index + m[0].length;
  }
  if (last < text.length) push({ text: text.slice(last) });
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
      let end = i + 1;
      while (end < lines.length && !lines[end].trim().startsWith('```')) end++;
      if (end >= lines.length) {
        // Kein Abschluss (F2-018: Text abgebrochen oder ``` vergessen): der Block endet an der nächsten Leerzeile, der Rest
        // bleibt normaler Text — sonst verschluckte der offene Zaun das ganze übrige Dokument.
        end = i + 1;
        while (end < lines.length && lines[end].trim() !== '') end++;
      }
      const body = lines.slice(i + 1, end);
      i = end;
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

type Positions = ReturnType<typeof positionsOf>;

/** Jede Stellung der Partie samt Weg dorthin (Index = Halbzüge). */
export function gamePositions({ fens, lastMoves, sans }: Positions): KnownPosition[] {
  const out: KnownPosition[] = [{ fen: fens[0], path: [] }];
  let path: LiveMove[] = [];
  sans.forEach((san, i) => {
    const [from, to] = lastMoves[i + 1]!;
    const promotion = /=([QRBN])/.exec(san)?.[1].toLowerCase() ?? '';
    path = [...path, { san, uci: from + to + promotion, fen: fens[i + 1], from, to }];
    out.push({ fen: fens[i + 1], path });
  });
  return out;
}

/**
 * Züge in Überschriften, Absätzen, Listen, Zitaten, Tabellen, Bildunterschriften und Buch-Begründungen klickbar machen;
 * Unauflösbares bleibt Text.
 */
export function linkSummaryMoves(blocks: SummaryBlock[], positions: Positions): SummaryBlock[] {
  const game = gamePositions(positions);
  const link = (parts: InlinePart[]): InlinePart[] => {
    const known: KnownPosition[] = [];   // je Absatz: „Nach 12.Sd3 kann Schwarz 12...b6“ bezieht sich aufeinander
    return parts.flatMap(p => {
      if (p.code || p.link) return [p];   // ein Linktext bleibt ganz — „Kosakowski – Nie (2025)“ ist keine Zugfolge
      const { text: _, ...style } = p;
      return explanationParts(p.text, game[0].fen, game, known).map(x => ({ ...style, ...x }));
    });
  };
  return blocks.map(b => {
    switch (b.kind) {
      case 'heading': case 'paragraph': case 'quote': return { ...b, parts: link(b.parts) };
      case 'list': return { ...b, items: b.items.map(link) };
      case 'table': return { ...b, head: b.head.map(link), rows: b.rows.map(r => r.map(link)) };
      case 'diagram': return b.spec.caption ? { ...b, spec: { ...b.spec, caption: link(b.spec.caption) } } : b;
      // Partie 243: „mit 35.axb5 und 45.Lxd5 jeweils sofort geerntet“ in der Literatur-Begründung blieb Text.
      case 'book': return { ...b, whyParts: link([{ text: b.why }]) };
      default: return b;
    }
  });
}

/**
 * Sichtbare Blöcke: `shown` entscheidet über Bücher. Eine Überschrift fällt weg, wenn alles bis zur nächsten gleich hohen oder
 * höheren Überschrift ausgeblendete Bücher sind — so bleibt weder „### Für Weiß“ noch „## Empfohlene Literatur“ leer stehen.
 */
export function visibleSummaryBlocks(all: SummaryBlock[], shown: (b: SummaryBlock & { kind: 'book' }) => boolean): SummaryBlock[] {
  const hiddenBook = (b: SummaryBlock) => b.kind === 'book' && !shown(b);
  return all.filter((b, i) => {
    if (hiddenBook(b)) return false;
    if (b.kind !== 'heading') return true;
    let end = i + 1;
    while (end < all.length) {
      const n = all[end];
      if (n.kind === 'heading' && n.level <= b.level) break;
      end++;
    }
    const body = all.slice(i + 1, end).filter(x => x.kind !== 'heading');
    return !body.length || !body.every(hiddenBook);
  });
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

/** Ein Zugpaar der ganzen Partie: Nummer („12.“ bzw. „12…“, wenn Schwarz die Zeile beginnt) und seine Halbzüge (`ply` = Stellung danach). */
export interface GameMovePair { number: string; moves: { ply: number; san: string }[]; }

/**
 * Die ganze Partie als Zugpaare für den Block „Die Partie“. Die Nummer kommt aus der FEN vor dem Zug — so stimmt sie
 * auch für eine Partie ab einer FEN-Stellung, in der Schwarz anfängt („17… Kd7 18. Tc1 …“).
 */
export function gameMovePairs({ fens, sans }: Positions): GameMovePair[] {
  const pairs: GameMovePair[] = [];
  sans.forEach((san, i) => {
    const [, turn, , , , full] = fens[i].split(' ');
    const move = { ply: i + 1, san };
    if (turn === 'w' || !pairs.length) pairs.push({ number: turn === 'w' ? `${full}.` : `${full}…`, moves: [move] });
    else pairs[pairs.length - 1].moves.push(move);
  });
  return pairs;
}

/** Überschrift des Abschnitts „Zusammenfassung“ in den Sprachen, in denen der Watcher schreibt (nur der Anfang zählt). */
const SUMMARY_HEADING = /^(zusammenfassung|summary|sažetak|összefoglal|résumé|resumen|riepilogo|sommario|samenvatting|podsumowanie|shrnutí|rezumat|resumo|sammanfattning|özet|резюме|підсумок|итог)/i;

/**
 * Wo „Die Partie“ hingehört: hinter den Abschnitt „## Zusammenfassung“, also vor die nächste Überschrift gleicher oder
 * höherer Stufe (meist „Schlüsselstellungen“). Fehlt er: hinter die Kopfzeile (der Absatz direkt unter dem `#`-Titel),
 * ohne Titel an den Anfang.
 */
export function gameBlockIndex(blocks: SummaryBlock[]): number {
  const text = (b: SummaryBlock) => b.kind === 'heading' ? b.parts.map(p => p.text).join('').trim() : '';
  const at = blocks.findIndex(b => b.kind === 'heading' && b.level === 2 && SUMMARY_HEADING.test(text(b)));
  if (at >= 0) {
    let end = at + 1;
    while (end < blocks.length && !(blocks[end].kind === 'heading' && (blocks[end] as { level: number }).level <= 2)) end++;
    return end;
  }
  if (blocks[0]?.kind === 'heading' && blocks[0].level === 1) return blocks[1]?.kind === 'paragraph' ? 2 : 1;
  return 0;
}

/** Blöcke mit eingesetztem „Die Partie“ (an {@link gameBlockIndex}). */
export function withGameBlock(blocks: SummaryBlock[]): SummaryBlock[] {
  const i = gameBlockIndex(blocks);
  return [...blocks.slice(0, i), { kind: 'game' }, ...blocks.slice(i)];
}
