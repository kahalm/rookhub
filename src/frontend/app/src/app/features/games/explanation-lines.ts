import { Chess } from 'chess.js';
import { parseKeyboardMove } from '../../shared/chess/keyboard-move.util';
import { LiveMove } from './live-engine-session';

/**
 * Ein Stück einer Fehler-Erklärung (0.915.0, Wunsch 2026-11-09: „die lines in der erklärung klickbar wäre toll"):
 * entweder reiner Text oder ein Zug einer Zugfolge. Ein Zug trägt die ganze Folge, zu der er gehört, ab der Stellung
 * der Aufgabe (`line`) und seinen Platz darin (`at` = Zahl der Züge bis einschließlich ihm) — ein Klick zeigt die
 * Stellung nach ihm, mit ← und → läuft man durch die Folge.
 */
export interface ExplanationPart { text: string; line?: LiveMove[]; at?: number; }

/**
 * Eine bekannte Stellung und der Weg dorthin ab der Wurzel. `said` = im Text genannt (nicht nur aufgefüllt): der letzte Zug
 * des Wegs ist dann eine Nennung, aus der fehlende Zwischenzüge späterer Folgen aufgefüllt werden.
 */
export interface KnownPosition { fen: string; path: LiveMove[]; said?: boolean; }
type Known = KnownPosition;

const START = /(^|[\s(„"'])(\d{1,3})(\.{3}|…|\.)\s?/g;
const MARKER = /^(\d{1,3})(\.{3}|…|\.)\s?/;
const TOKEN = /^[^\s,;:()„"“”]+/;

function fullmove(fen: string): number { return parseInt(fen.split(' ')[5] ?? '1', 10) || 1; }
function blackToMove(fen: string): boolean { return fen.split(' ')[1] === 'b'; }
/** Halbzüge seit Zug 1 (Weiß am Zug = gerade). */
function plyOf(fen: string): number { return (fullmove(fen) - 1) * 2 + (blackToMove(fen) ? 1 : 0); }
/** Stellung ohne Zugzähler — zum Vergleich von Stellungen auf verschiedenen Wegen. */
function placeOf(fen: string): string { return fen.split(' ').slice(0, 4).join(' '); }
/** So viele fehlende Halbzüge füllt der Zerleger höchstens auf, bevor er eine Folge an eine frühere Stellung hängt. */
const MAX_BRIDGE = 6;

/** Ein SAN-Zug (englisch, aus chess.js) in `fen` — `null` = nicht legal. */
function sanMove(fen: string, san: string): LiveMove | null {
  try {
    const chess = new Chess(fen);
    const m = chess.move(san);
    return { san: m.san, uci: m.from + m.to + (m.promotion ?? ''), fen: chess.fen(), from: m.from, to: m.to };
  } catch {
    return null;
  }
}

/** Zugfolge in SAN ab `fen` als `LiveMove`s — bricht am ersten nicht legalen Zug ab. */
export function sanLine(fen: string, sans: readonly string[]): LiveMove[] {
  const out: LiveMove[] = [];
  let cur = fen;
  for (const san of sans) {
    const m = sanMove(cur, san);
    if (!m) break;
    out.push(m);
    cur = m.fen;
  }
  return out;
}

/** Ein Wort als Zug in `fen` (englische oder deutsche/kroatische Figurenbuchstaben) — `null` = kein legaler Zug. */
function moveOf(fen: string, word: string): LiveMove | null {
  const raw = word.replace(/[.!?]+$/, '');
  if (!/^(?:[KQRBNDTLS]?[a-h]?[1-8]?[x:]?[a-h][1-8](?:=?[QRBNDTLS])?|O-O(?:-O)?|0-0(?:-0)?)[+#]?$/.test(raw)) return null;
  const k = parseKeyboardMove(fen, raw.replace(/0/g, 'O'), 'de');
  if (!k || k.needsPromotion) return null;
  try {
    const chess = new Chess(fen);
    const m = chess.move({ from: k.orig, to: k.dest, promotion: k.promotion });
    return { san: m.san, uci: m.from + m.to + (m.promotion ?? ''), fen: chess.fen(), from: m.from, to: m.to };
  } catch {
    return null;
  }
}

interface Parsed { moves: { text: string; gap: string; move: LiveMove }[]; end: number; }

/** Ab `pos` (direkt an einer Zugnummer) so viele Züge wie legal lesen, beginnend in `fen`. */
function readSequence(text: string, pos: number, fen: string): Parsed {
  const moves: Parsed['moves'] = [];
  let i = pos;
  let cur = fen;
  let gap = '';
  for (;;) {
    let j = i;
    const marker = MARKER.exec(text.slice(j));
    if (marker) j += marker[0].length;
    const tok = TOKEN.exec(text.slice(j));
    if (!tok) break;
    const word = tok[0];
    const move = moveOf(cur, word);
    if (!move) break;
    const shown = word.replace(/\.+$/, '');   // der Satzpunkt nach dem letzten Zug bleibt Text
    moves.push({ text: text.slice(i, j) + shown, gap, move });
    cur = move.fen;
    i = j + shown.length;
    const ws = /^\s+/.exec(text.slice(i));
    if (!ws || moves.length === 0) break;
    // Nach dem Leerraum muss wieder ein Zug (mit oder ohne Nummer) kommen — sonst endet die Folge vor dem Leerraum.
    gap = ws[0];
    i += gap.length;
    const peekMarker = MARKER.exec(text.slice(i));
    const peekTok = TOKEN.exec(text.slice(i + (peekMarker?.[0].length ?? 0)));
    if (!peekTok || !moveOf(cur, peekTok[0])) { i -= gap.length; break; }
  }
  return { moves, end: i };
}

/**
 * Zerlegt eine Erklärung in Text und klickbare Züge. Eine Zugfolge beginnt an einer Zugnummer („12.Sd3", „12...b6",
 * „12…b6"); wo sie anfängt, ist nicht immer die Aufgabe selbst — „Nach 12.Sdf3 kann Schwarz mit 12...b6 …" setzt
 * nach dem zuvor genannten Zug an. Deshalb merkt sich der Zerleger jede Stellung, die eine Folge erreicht, und setzt
 * eine neue Folge an der zuletzt genannten Stellung an, in der ihr erster Zug legal ist: passen Zugnummer und Seite, direkt;
 * liegt die Stellung davor (höchstens {@link MAX_BRIDGE} Halbzüge), werden die Zwischenzüge aufgefüllt (Fehlermeldung
 * 2026-11-09: „Gegen 17.Dh4 … 18.d5" landete in der Variante nach 17.Dc2) — je Zwischenstellung mit dem zuletzt im Text
 * genannten Zug derselben Nummer und Seite, der dort legal ist, sonst aus `candidates` (Linien ab der Wurzel, z. B. die beiden
 * Züge des Vergleichs samt Engine-Linie) bzw. aus der Partie. Die aufgefüllten Züge stehen in `line` vor dem angeklickten Zug,
 * damit man sieht, welche Variante gemeint ist. Was nicht passt, bleibt Text — eine Folge, die nicht stimmt, wird nie klickbar.
 *
 * Mit `game` (jede Stellung der Partie, Index = Halbzüge; Partie-Zusammenfassung): zuerst die zuletzt genannte Stellung, wenn sie
 * neben der Partie liegt (Kontext einer Variante), dann der tatsächliche Partiezug zu Nummer und Seite, dann die zuletzt
 * genannten Stellungen, zuletzt die Partiestellung als Abzweig einer Variante. `shared` hält die genannten Stellungen über
 * mehrere Textstücke desselben Absatzes.
 */
export function explanationParts(
  text: string, rootFen: string, game?: readonly Known[], shared?: Known[], candidates?: readonly (readonly LiveMove[])[],
): ExplanationPart[] {
  const parts: ExplanationPart[] = [];
  const known: Known[] = shared ?? [{ fen: rootFen, path: [] }];
  const root = game?.[0]?.fen ?? rootFen;
  const matches = (c: Known, number: number, black: boolean) => fullmove(c.fen) === number && blackToMove(c.fen) === black;

  /** Mögliche Züge für die Zwischenstellung `fen`, in dieser Reihenfolge: genannt (gleiche Nummer/Seite, zuletzt genannt
   *  zuerst), aus den Kandidatenlinien, aus der Partie — jeweils nur legale, ohne Doppelte. */
  const options = (fen: string): LiveMove[] => {
    const n = fullmove(fen);
    const b = blackToMove(fen);
    const place = placeOf(fen);
    const sans: string[] = [];
    for (let k = known.length - 1; k >= 0; k--) {
      const c = known[k];
      if (!c.said || !c.path.length) continue;
      const before = c.path.length > 1 ? c.path[c.path.length - 2].fen : root;
      if (fullmove(before) === n && blackToMove(before) === b) sans.push(c.path[c.path.length - 1].san);
    }
    for (const line of candidates ?? []) {
      line.forEach((mv, i) => { if (placeOf(i === 0 ? root : line[i - 1].fen) === place) sans.push(mv.san); });
    }
    const gi = game?.findIndex(g => placeOf(g.fen) === place) ?? -1;
    if (game && gi >= 0 && gi + 1 < game.length) sans.push(game[gi + 1].path[gi].san);
    const out: LiveMove[] = [];
    for (const san of sans) {
      const mv = sanMove(fen, san);
      if (mv && !out.some(o => o.uci === mv.uci)) out.push(mv);
    }
    return out;
  };

  /** Folge ab `cand` — direkt, wenn Nummer und Seite passen, sonst mit aufgefüllten Zwischenzügen (erste aufgehende Wahl). */
  const tryFrom = (cand: Known, pos: number, number: number, black: boolean): { from: Known; seq: Parsed } | null => {
    const gap = (number - 1) * 2 + (black ? 1 : 0) - plyOf(cand.fen);
    if (gap < 0 || gap > MAX_BRIDGE) return null;
    if (gap === 0) {
      const seq = readSequence(text, pos, cand.fen);
      return seq.moves.length ? { from: cand, seq } : null;
    }
    for (const mv of options(cand.fen)) {
      const r = tryFrom({ fen: mv.fen, path: [...cand.path, mv] }, pos, number, black);
      if (r) return r;
    }
    return null;
  };
  const onGame = (c: Known) => !!game && placeOf(game[c.path.length]?.fen ?? '') === placeOf(c.fen);

  let last = 0;
  START.lastIndex = 0;
  let m: RegExpExecArray | null;
  while ((m = START.exec(text))) {
    const pos = m.index + m[1].length;
    if (pos < last) continue;
    const number = parseInt(m[2], 10);
    const black = m[3] !== '.';
    let best: { from: Known; seq: Parsed } | null = null;
    // Partie-Zusammenfassung: steht der Text gerade in einer Variante, gilt sie vor dem Partiezug.
    const newest = known[known.length - 1];
    if (game && newest && !onGame(newest)) best = tryFrom(newest, pos, number, black);
    const index = game?.findIndex(c => matches(c, number, black)) ?? -1;
    if (!best && game && index >= 0 && index + 1 < game.length) {
      const seq = readSequence(text, pos, game[index].fen);
      if (seq.moves[0]?.move.san === game[index + 1].path[index].san) best = { from: game[index], seq };
    }
    for (let k = known.length - 1; k >= 0 && !best; k--) best = tryFrom(known[k], pos, number, black);
    if (!best && game && index >= 0) {
      const seq = readSequence(text, pos, game[index].fen);
      if (seq.moves.length) best = { from: game[index], seq };
    }
    if (!best) continue;
    if (pos > last) parts.push({ text: text.slice(last, pos) });
    const line = [...best.from.path, ...best.seq.moves.map(x => x.move)];
    best.seq.moves.forEach((x, idx) => {
      if (x.gap) parts.push({ text: x.gap });
      const at = best!.from.path.length + idx + 1;
      parts.push({ text: x.text, line, at });
      known.push({ fen: x.move.fen, path: line.slice(0, at), said: true });
    });
    last = best.seq.end;
    START.lastIndex = last;
  }
  if (last < text.length) parts.push({ text: text.slice(last) });
  return parts;
}
