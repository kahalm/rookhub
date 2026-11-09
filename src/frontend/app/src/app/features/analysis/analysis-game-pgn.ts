import { AnalysisNode, moveNumberOf } from './analysis-tree';
import { pgnFileName } from '../../shared/pgn-export.util';

/**
 * Das Analysebrett als PGN (Wunsch 2026-11-09): Kopfdaten aus dem kleinen Dialog + Zugbaum samt Varianten, Symbolen und
 * Kommentaren. Rein, ohne Angular — Gegenstück zu `parsePgnTree` (analysis-tree.ts).
 *
 * Tief wird nur über Varianten verschachtelt: entlang einer Linie läuft die Schleife, so dass auch lange Partien nicht an
 * einer Rekursionsgrenze scheitern.
 */
export const PGN_RESULTS = ['*', '1-0', '0-1', '1/2-1/2'] as const;
export type PgnResult = (typeof PGN_RESULTS)[number];

export interface GameDetails {
  white: string;
  black: string;
  result: PgnResult;
  /** „YYYY-MM-DD" (Datumsfeld), `''` = keins. */
  date: string;
  event: string;
  round: string;
  /** Unvollständiges Datum aus dem geladenen PGN („2024.??.??"): gilt, solange das Feld leer bleibt. */
  rawDate?: string;
  /** Aus dem geladenen PGN mitgenommen — im Dialog nicht zu sehen. */
  site?: string;
}

const STANDARD_START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -';
const FULL_DATE = /^(\d{4})\.(\d{2})\.(\d{2})$/;

/** PGN-Platzhalter („?", „??", „-") zählen als leer. */
function known(v: string | undefined): string {
  const t = (v ?? '').trim();
  return !t || /^(\?+|-)$/.test(t) ? '' : t;
}

/** Vorbelegung des Dialogs aus den Kopfdaten eines geladenen PGN (`null` = nichts geladen). */
export function detailsFromHeaders(h: Record<string, string> | null | undefined): GameDetails {
  const rawResult = (h?.['Result'] ?? '').trim();
  const rawDate = (h?.['Date'] ?? '').trim();
  const full = FULL_DATE.exec(rawDate);
  const hasYear = /^\d{4}/.test(rawDate);
  return {
    white: known(h?.['White']),
    black: known(h?.['Black']),
    result: (PGN_RESULTS as readonly string[]).includes(rawResult) ? rawResult as PgnResult : '*',
    date: full ? `${full[1]}-${full[2]}-${full[3]}` : '',
    event: known(h?.['Event']),
    round: known(h?.['Round']),
    rawDate: !full && hasYear ? rawDate : undefined,
    site: known(h?.['Site']) || undefined,
  };
}

/** Beginnt der Baum in der Grundstellung? (Halbzug- und Zugzähler zählen nicht.) */
export function isStandardStart(root: AnalysisNode): boolean {
  return root.fen.split(' ').slice(0, 4).join(' ') === STANDARD_START;
}

function tag(name: string, value: string): string {
  const clean = value.replace(/[\r\n]+/g, ' ').replace(/\\/g, '\\\\').replace(/"/g, '\\"');
  return `[${name} "${clean}"]`;
}

const GLYPH_NAG: Record<string, number> = { '!': 1, '?': 2, '!!': 3, '??': 4, '!?': 5, '?!': 6 };
/** Die in PGN festgelegten Stellungsbewertungen; was hier fehlt (→, ⇆ …), hat keinen NAG und fällt weg. */
const EVAL_NAG: Record<string, number> = { '=': 10, '∞': 13, '⩲': 14, '⩱': 15, '±': 16, '∓': 17, '+−': 18, '−+': 19 };

function moveWords(node: AnalysisNode, withNumber: boolean, out: string[]): void {
  const before = node.parent!.fen;
  const { number, white } = moveNumberOf(before);
  if (white) out.push(`${number}.`);
  else if (withNumber) out.push(`${number}...`);
  out.push(node.san);
  if (node.glyph && GLYPH_NAG[node.glyph]) out.push(`$${GLYPH_NAG[node.glyph]}`);
  if (node.evaluation && EVAL_NAG[node.evaluation]) out.push(`$${EVAL_NAG[node.evaluation]}`);
  const comment = node.comment?.replace(/[{}]/g, '').replace(/\s+/g, ' ').trim();
  if (comment) out.push(`{${comment}}`);
}

/** `first` (unter `parent`) und alles dahinter; Geschwister eines Hauptzugs stehen als `( … )` direkt hinter ihm. */
function writeLine(parent: AnalysisNode, first: AnalysisNode, out: string[], mainlineOnly: boolean): void {
  let node = parent;
  let next: AnalysisNode | undefined = first;
  let needNumber = true;
  while (next) {
    moveWords(next, needNumber, out);
    needNumber = !!next.comment;
    if (!mainlineOnly && node.children[0] === next && node.children.length > 1) {
      for (const alt of node.children.slice(1)) {
        out.push('(');
        writeLine(node, alt, out, false);
        out.push(')');
      }
      needNumber = true;
    }
    node = next;
    next = node.children[0];
  }
}

function moveText(root: AnalysisNode, mainlineOnly: boolean): string[] {
  const out: string[] = [];
  if (root.children[0]) writeLine(root, root.children[0], out, mainlineOnly);
  return out;
}

/** Umbruch bei 80 Zeichen; Klammern kleben am Nachbarwort. */
function wrap(words: string[]): string {
  const text = words.join(' ').replace(/\( /g, '(').replace(/ \)/g, ')');
  const lines: string[] = [];
  let line = '';
  for (const w of text.split(' ')) {
    if (line && line.length + 1 + w.length > 80) { lines.push(line); line = w; }
    else line = line ? `${line} ${w}` : w;
  }
  if (line) lines.push(line);
  return lines.join('\n');
}

/**
 * Der Baum als PGN. `mainlineOnly`: nur die Hauptlinie — für „Meine Partien" und die Vereinsdatenbank, die ohnehin nur
 * sie übernehmen (Server `PgnParser.TryExtractUciMainline`).
 */
export function treeToPgn(root: AnalysisNode, details: GameDetails, opts: { mainlineOnly?: boolean } = {}): string {
  const date = details.date ? details.date.replace(/-/g, '.') : details.rawDate || '????.??.??';
  const headers = [
    tag('Event', details.event.trim() || '?'),
    tag('Site', details.site?.trim() || '?'),
    tag('Date', date),
    tag('Round', details.round.trim() || '?'),
    tag('White', details.white.trim() || '?'),
    tag('Black', details.black.trim() || '?'),
    tag('Result', details.result),
  ];
  if (!isStandardStart(root)) headers.push(tag('SetUp', '1'), tag('FEN', root.fen));
  return `${headers.join('\n')}\n\n${wrap([...moveText(root, !!opts.mainlineOnly), details.result])}\n`;
}

/** Dateiname „Weiss_Schwarz_2026_11_09.pgn"; ohne Namen „analyse.pgn". */
export function gameFileName(d: GameDetails): string {
  const names = [d.white.trim(), d.black.trim()].filter(Boolean).join(' ');
  return pgnFileName(names || 'analyse', d.date);
}
