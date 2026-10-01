import { Chess } from 'chess.js';
import { MAIA_TOP_P } from './maia-model';

/**
 * Kodierung für Maia-3 (nachgebaut nach `src/lib/engine/tensor.ts` der maia-platform-frontend) —
 * rein, ohne Angular, ohne Worker. Der Worker rechnet nur das Netz; alles Schachliche steht hier.
 *
 * Das Modell sieht die Stellung IMMER aus Sicht von Weiß. Ist Schwarz am Zug, wird gespiegelt
 * (Reihen umdrehen + Farben tauschen), und die Züge werden gespiegelt in den Index übersetzt —
 * die Antwort des Modells muss also zurückgespiegelt werden, bevor sie aufs Brett geht.
 *
 * Die 4352 Zug-Indizes folgen einer FORMEL (gegen alle Einträge der Upstream-Tabelle geprüft,
 * 0 Abweichungen): 4096 Von-Nach-Paare, danach 256 Umwandlungen — nur 7.→8. Reihe, weil gespiegelt
 * wird. Eine 60-KB-Tabelle wird deshalb nicht ausgeliefert.
 *
 * Rochaderechte und en passant stehen NICHT in den Tokens; die Legalität kommt allein aus chess.js.
 */

/** Kanal 0..11 je Figur: erst Weiß (PNBRQK), dann Schwarz (pnbrqk). */
const PIECES = 'PNBRQKpnbrqk';
/** Reihenfolge der Umwandlungsfiguren im Index-Block ab 4096. */
const PROMO = 'qrbn';

/** Feldnummer a1 = 0 … h8 = 63. */
const sq = (s: string): number => (s.charCodeAt(1) - 49) * 8 + (s.charCodeAt(0) - 97);

/** Index eines UCI-Zugs in der Ausgabe `logits_move` (aus WEISS-Sicht, also nach dem Spiegeln). */
export function moveIndex(uci: string): number {
  return uci.length === 4
    ? sq(uci.slice(0, 2)) * 64 + sq(uci.slice(2, 4))
    : 4096 + (uci.charCodeAt(0) - 97) * 32 + (uci.charCodeAt(2) - 97) * 4 + PROMO.indexOf(uci[4]);
}

const mirrorSq = (s: string): string => s[0] + (9 - Number(s[1]));

/** Zug an der Mitte des Bretts spiegeln (Reihe r → 9 − r, Linie bleibt, Umwandlung bleibt). */
export function mirrorUci(uci: string): string {
  return mirrorSq(uci.slice(0, 2)) + mirrorSq(uci.slice(2, 4)) + uci.slice(4);
}

/** Figurenfeld einer FEN spiegeln: Reihen in umgekehrter Reihenfolge, Groß/klein getauscht. */
export function mirrorPlacement(placement: string): string {
  return placement.split('/').reverse()
    .map(row => [...row].map(c => /[a-zA-Z]/.test(c)
      ? (c === c.toUpperCase() ? c.toLowerCase() : c.toUpperCase())
      : c).join(''))
    .join('/');
}

/** Figurenfeld → Eingabe `tokens` (64 Felder × 12 Kanäle, eine 1 je Figur). */
export function boardTokens(placement: string): Float32Array<ArrayBuffer> {
  const t = new Float32Array(64 * 12);
  const rows = placement.split('/');
  for (let r = 0; r < 8; r++) {
    const rank = 7 - r;
    let file = 0;
    for (const c of rows[r] ?? '') {
      if (c >= '1' && c <= '8') { file += Number(c); continue; }
      const channel = PIECES.indexOf(c);
      if (channel >= 0 && file < 8) t[(rank * 8 + file) * 12 + channel] = 1;
      file++;
    }
  }
  return t;
}

/** Eine kodierte Stellung: die Eingabe fürs Netz und die legalen Züge samt ihrem Index. */
export interface MaiaEncoding {
  tokens: Float32Array<ArrayBuffer>;
  /** Legale Züge als UCI in ECHTER Brett-Sicht (nicht gespiegelt) — so gehen sie aufs Brett. */
  legal: string[];
  /** Je legalem Zug sein Index in `logits_move` (bei Schwarz am Zug über den gespiegelten Zug). */
  indices: number[];
}

/** Stellung kodieren. Wirft bei unlesbarer FEN (chess.js) — der Aufrufer meldet das als Fehler. */
export function encodePosition(fen: string): MaiaEncoding {
  const chess = new Chess(fen);
  const [placement, turn] = fen.trim().split(/\s+/);
  const black = turn === 'b';
  const legal = chess.moves({ verbose: true }).map(m => m.from + m.to + (m.promotion ?? ''));
  return {
    tokens: boardTokens(black ? mirrorPlacement(placement) : placement),
    legal,
    indices: legal.map(u => moveIndex(black ? mirrorUci(u) : u)),
  };
}

/**
 * Zugverteilung aus den rohen Logits: Softmax NUR über die legalen Züge (Temperatur 1), absteigend
 * sortiert. Was das Netz illegalen Zügen zutraut, spielt keine Rolle.
 */
export function policyFromLogits(logits: ArrayLike<number>, enc: MaiaEncoding): { uci: string; p: number }[] {
  if (!enc.legal.length) return [];
  const values = enc.indices.map(i => logits[i] ?? -Infinity);
  const max = Math.max(...values);
  // Abzug des Maximums: kein Überlauf bei großen Logits. Ohne einen einzigen brauchbaren Wert (zu kurze
  // Ausgabe) wäre alles NaN — dann lieber gleichverteilt als eine Sortierung über NaN.
  const exps = Number.isFinite(max) ? values.map(v => Math.exp(v - max)) : values.map(() => 1);
  const sum = exps.reduce((a, b) => a + b, 0);
  return enc.legal
    .map((uci, k) => ({ uci, p: exps[k] / sum }))
    .sort((a, b) => b.p - a.p);
}

/**
 * Einen Zug WÜRFELN statt den wahrscheinlichsten zu nehmen — wer dieselbe Stellung mehrfach übt, soll
 * verschiedene Antworten sehen. Nucleus-Auswahl: absteigend durchgehen und Züge behalten, solange die
 * Summe der Wahrscheinlichkeiten DAVOR unter `topP` liegt (der Zug, der die Schwelle überschreitet,
 * gehört dazu; der erste immer), die behaltenen neu normieren, mit `rng()` ∈ [0, 1) würfeln.
 * Leere Verteilung → `null` (kein legaler Zug).
 */
export function pickMove(policy: { uci: string; p: number }[], rng: () => number, topP = MAIA_TOP_P): string | null {
  if (!policy.length) return null;
  const sorted = [...policy].sort((a, b) => b.p - a.p);
  const kept: { uci: string; p: number }[] = [];
  let before = 0;
  for (const move of sorted) {
    if (kept.length && before >= topP) break;
    kept.push(move);
    before += move.p;
  }
  let target = rng() * before;
  for (const move of kept) {
    if (target < move.p) return move.uci;
    target -= move.p;
  }
  return kept[kept.length - 1].uci;   // Rundungsrest: rng() knapp unter 1 landet beim letzten behaltenen
}
