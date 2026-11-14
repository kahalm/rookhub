import { readSequence } from '../explanation-lines';
import type { LiveMove } from '../live-engine-session';
import { SummaryBlock, InlinePart, gamePositions, positionsOf } from './summary-markdown.util';

/**
 * „Die Partie“ als EIN Text (Wunsch 2026-11-14: „die Partie begann als Französisch: e4 e6 … und dann alle Züge immer wieder
 * unterbrochen von Text und Untervarianten“). Der Watcher schreibt den Abschnitt `## Die Partie` als gewöhnliche Absätze:
 * die Partiezüge der Reihe nach mit Zugnummer im Text, Untervarianten in runden Klammern. Die Seite verlässt sich dabei
 * nicht auf die Schreibweise, sondern prüft jeden Zug gegen das PGN:
 *
 * - Eine Zugfolge beginnt an einer Zugnummer („12.Sd3“, „12...b6“, „12…b6“) und läuft, solange legale Züge folgen.
 * - Außerhalb von Klammern setzt sie an der Partiestellung dieser Zugnummer an. In einer Klammer zuerst an einer Stellung,
 *   die dieselbe Klammer schon erreicht hat (Fortsetzung oder Abzweig der Variante), sonst ebenfalls an der Partiestellung.
 * - Partiezug ist, was von dort Zug für Zug der Partie folgt; alles andere ist Variantenzug. Was nirgends legal ist, bleibt Text.
 *
 * Gerendert wird über das Template (nie innerHTML). Dieselben Regeln prüft der Watcher vor dem Abliefern
 * (`formulare-bot/lib/check_summary_merged.py`), dort streng: alle Partiezüge lückenlos, Varianten nur in Klammern.
 */

/** Ein Stück des verschmolzenen Texts: Text, ein Zug (`move`) oder die Marke fürs Ergebnis hinter dem letzten Partiezug. */
export interface GameTextPart {
  text: string; bold?: boolean; italic?: boolean; code?: boolean;
  /** Klickbarer Zug: Zugfolge ab Partieanfang bis zu ihm (`at`); `ply` nur bei einem Partiezug (Stellung danach). */
  move?: { line: LiveMove[]; at: number; ply?: number };
  /** Zugzeichen hinter dem Zug („!“, „??“ …), aus `text` herausgelöst. */
  glyph?: string;
  mark?: 'good' | 'bad';
  result?: boolean;
}

/** Ein Lauf des Absatzes: gewöhnlicher Text mit Partiezügen oder eine Klammer mit Variantenzügen. */
export interface GameTextSegment { variation: boolean; parts: GameTextPart[]; }

/** Überschrift des verschmolzenen Abschnitts (der Watcher schreibt Deutsch; Englisch für Handläufe). */
const GAME_HEADING = /^(die\s+partie|partieverlauf|the\s+game)\b/i;
const START = /(^|[\s(„"'])(\d{1,3})(\.{3}|…|\.)\s?/g;
/** Ab diesem Anteil der Partiezüge gilt der Abschnitt als verschmolzen — sonst bleibt alles wie bei alten Texten. */
const MIN_COVERAGE = 0.5;

interface Known { fen: string; path: LiveMove[]; gamePly?: number; }

function fullmove(fen: string): number { return parseInt(fen.split(' ')[5] ?? '1', 10) || 1; }
function blackToMove(fen: string): boolean { return fen.split(' ')[1] === 'b'; }

/** Zugzeichen vom Zugtext trennen: „19…Sc3!!“ → „19…Sc3“ + „!!“ (grün), „23.Se2?“ → rot; „!?“ bleibt ohne Farbe. */
function splitGlyph(text: string): Pick<GameTextPart, 'text' | 'glyph' | 'mark'> {
  const m = /^(.*?[^!?])([!?]{1,2})$/.exec(text);
  if (!m) return { text };
  const glyph = m[2];
  const mark = glyph === '!' || glyph === '!!' ? 'good' : glyph === '!?' ? undefined : 'bad';
  return mark ? { text: m[1], glyph, mark } : { text: m[1], glyph };
}

/** Zerleger für die Absätze EINES Abschnitts: merkt sich über Textstücke hinweg Klammertiefe und Variantenstellungen. */
class GameTextReader {
  private depth = 0;
  private known: Known[] = [];
  readonly plies = new Set<number>();
  private readonly game: Known[];

  constructor(positions: ReturnType<typeof positionsOf>) {
    this.game = gamePositions(positions).map((g, i) => ({ ...g, gamePly: i }));
  }

  /** Klammern im überlesenen Text zählen; mit der schließenden Klammer endet der Zusammenhang der Variante. */
  private skip(text: string): void {
    for (const ch of text) {
      if (ch === '(') this.depth++;
      else if (ch === ')' && this.depth > 0 && --this.depth === 0) this.known = [];
    }
  }

  paragraph(parts: InlinePart[]): GameTextSegment[] {
    this.depth = 0;
    this.known = [];
    const flat: GameTextPart[] = parts.flatMap(p => p.code ? [p] : this.read(p));
    return segments(flat);
  }

  private read(part: InlinePart): GameTextPart[] {
    const text = part.text;
    const style: Pick<GameTextPart, 'bold' | 'italic'> = { ...(part.bold ? { bold: true } : {}), ...(part.italic ? { italic: true } : {}) };
    const out: GameTextPart[] = [];
    let last = 0;
    let scanned = 0;
    START.lastIndex = 0;
    let m: RegExpExecArray | null;
    while ((m = START.exec(text))) {
      const pos = m.index + m[1].length;
      if (pos < last) continue;
      this.skip(text.slice(scanned, pos));
      scanned = pos;
      const number = parseInt(m[2], 10);
      const black = m[3] !== '.';
      const fits = (c: Known) => fullmove(c.fen) === number && blackToMove(c.fen) === black;
      const bases: Known[] = [];
      if (this.depth > 0) for (let k = this.known.length - 1; k >= 0; k--) if (fits(this.known[k])) bases.push(this.known[k]);
      const onGame = this.game.find(fits);
      if (onGame) bases.push(onGame);
      let hit: { from: Known; seq: ReturnType<typeof readSequence> } | null = null;
      for (const from of bases) {
        const seq = readSequence(text, pos, from.fen);
        if (seq.moves.length) { hit = { from, seq }; break; }
      }
      if (!hit) continue;
      if (pos > last) out.push({ ...style, text: text.slice(last, pos) });
      const line = [...hit.from.path, ...hit.seq.moves.map(x => x.move)];
      let gamePly = hit.from.gamePly;
      hit.seq.moves.forEach((x, idx) => {
        if (x.gap) out.push({ ...style, text: x.gap });
        const at = hit!.from.path.length + idx + 1;
        const next = gamePly === undefined ? undefined : this.game[gamePly + 1];
        gamePly = next && next.path[at - 1].uci === x.move.uci ? at : undefined;
        if (gamePly !== undefined) this.plies.add(gamePly);
        out.push({ ...style, ...splitGlyph(x.text), move: { line, at, ply: gamePly } });
        this.known.push({ fen: x.move.fen, path: line.slice(0, at), gamePly });
      });
      last = scanned = hit.seq.end;
      START.lastIndex = last;
    }
    this.skip(text.slice(scanned));
    if (last < text.length) out.push({ ...style, text: text.slice(last) });
    return out;
  }
}

/** Flache Teile an den äußeren Klammern in Läufe schneiden; eine Klammer ohne Zug („(−3,9)“) bleibt gewöhnlicher Text. */
function segments(flat: GameTextPart[]): GameTextSegment[] {
  const out: GameTextSegment[] = [];
  let cur: GameTextSegment = { variation: false, parts: [] };
  let depth = 0;
  const close = () => {
    if (cur.parts.length) out.push(cur.variation && !cur.parts.some(p => p.move) ? { variation: false, parts: cur.parts } : cur);
  };
  const start = (variation: boolean) => { close(); cur = { variation, parts: [] }; };
  for (const p of flat) {
    if (p.move || p.code) { cur.parts.push(p); continue; }
    let from = 0;
    for (let i = 0; i < p.text.length; i++) {
      const ch = p.text[i];
      if (ch === '(' && depth++ === 0) {
        if (i > from) cur.parts.push({ ...p, text: p.text.slice(from, i) });
        start(true);
        from = i;
      } else if (ch === ')' && depth > 0 && --depth === 0) {
        cur.parts.push({ ...p, text: p.text.slice(from, i + 1) });
        start(false);
        from = i + 1;
      }
    }
    if (from < p.text.length) cur.parts.push({ ...p, text: p.text.slice(from) });
  }
  close();
  // Nachbarn gleicher Art (Klammer ohne Zug zwischen zwei Textläufen) wieder zusammenlegen.
  return out.reduce<GameTextSegment[]>((acc, s) => {
    const prev = acc[acc.length - 1];
    if (prev && !prev.variation && !s.variation) prev.parts.push(...s.parts); else acc.push(s);
    return acc;
  }, []);
}

/**
 * Den Abschnitt „## Die Partie“ in verschmolzene Absätze (`gameText`) umsetzen. `merged` = es gibt ihn und er enthält
 * mindestens die Hälfte der Partiezüge — nur dann entfällt die von der Seite eingesetzte Zugliste. Ohne den Abschnitt
 * (alte Zusammenfassungen) kommen die Blöcke unverändert zurück.
 */
export function mergeGameSection(blocks: SummaryBlock[], positions: ReturnType<typeof positionsOf>): { blocks: SummaryBlock[]; merged: boolean } {
  const total = positions.sans.length;
  const text = (b: SummaryBlock) => b.kind === 'heading' ? b.parts.map(p => p.text).join('').trim() : '';
  const at = blocks.findIndex(b => b.kind === 'heading' && b.level === 2 && GAME_HEADING.test(text(b)));
  if (at < 0 || !total) return { blocks, merged: false };
  let end = at + 1;
  while (end < blocks.length && !(blocks[end].kind === 'heading' && (blocks[end] as { level: number }).level <= 2)) end++;
  const reader = new GameTextReader(positions);
  const section = blocks.slice(at + 1, end).map((b): SummaryBlock => b.kind === 'paragraph' ? { kind: 'gameText', segments: reader.paragraph(b.parts) } : b);
  if (reader.plies.size < total * MIN_COVERAGE) return { blocks, merged: false };
  // Ergebnis hinter den letzten Partiezug; steht der nicht im Text, zeigt die Seite keins (die Kopfzeile nennt es ohnehin).
  const texts = section.filter((b): b is SummaryBlock & { kind: 'gameText' } => b.kind === 'gameText');
  let placed = false;
  for (const b of texts) for (const s of b.segments) {
    const i = s.variation ? -1 : s.parts.findIndex(p => p.move?.ply === total);
    if (i >= 0 && !placed) { s.parts.splice(i + 1, 0, { text: '', result: true }); placed = true; }
  }
  return { blocks: [...blocks.slice(0, at + 1), ...section, ...blocks.slice(end)], merged: true };
}
