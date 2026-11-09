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

interface Known { fen: string; path: LiveMove[]; }

const START = /(^|[\s(„"'])(\d{1,3})(\.{3}|…|\.)\s?/g;
const MARKER = /^(\d{1,3})(\.{3}|…|\.)\s?/;
const TOKEN = /^[^\s,;:()„"“”]+/;

function fullmove(fen: string): number { return parseInt(fen.split(' ')[5] ?? '1', 10) || 1; }
function blackToMove(fen: string): boolean { return fen.split(' ')[1] === 'b'; }

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
 * eine neue Folge an der zuletzt genannten Stellung an, deren Zugnummer und Seite passen und in der ihr erster Zug
 * legal ist. Was nicht passt, bleibt Text — eine Folge, die nicht stimmt, wird nie klickbar.
 */
export function explanationParts(text: string, rootFen: string): ExplanationPart[] {
  const parts: ExplanationPart[] = [];
  const known: Known[] = [{ fen: rootFen, path: [] }];
  let last = 0;
  START.lastIndex = 0;
  let m: RegExpExecArray | null;
  while ((m = START.exec(text))) {
    const pos = m.index + m[1].length;
    if (pos < last) continue;
    const number = parseInt(m[2], 10);
    const black = m[3] !== '.';
    let best: { from: Known; seq: Parsed } | null = null;
    for (let k = known.length - 1; k >= 0; k--) {
      const cand = known[k];
      if (fullmove(cand.fen) !== number || blackToMove(cand.fen) !== black) continue;
      const seq = readSequence(text, pos, cand.fen);
      if (seq.moves.length) { best = { from: cand, seq }; break; }
    }
    if (!best) continue;
    if (pos > last) parts.push({ text: text.slice(last, pos) });
    const line = [...best.from.path, ...best.seq.moves.map(x => x.move)];
    best.seq.moves.forEach((x, idx) => {
      if (x.gap) parts.push({ text: x.gap });
      const at = best!.from.path.length + idx + 1;
      parts.push({ text: x.text, line, at });
      known.push({ fen: x.move.fen, path: line.slice(0, at) });
    });
    last = best.seq.end;
    START.lastIndex = last;
  }
  if (last < text.length) parts.push({ text: text.slice(last) });
  return parts;
}
