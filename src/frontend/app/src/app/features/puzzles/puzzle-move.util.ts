import { Chess, Move, Square } from 'chess.js';
import { Key } from 'chessground/types';

/**
 * Gemeinsame, reine Schach-/Brett-Helfer für die Puzzle-Modi (Normal, Endless, Buch).
 * Vorher in allen 3 Komponenten identisch dupliziert — hier zentral, leichter pflegbar/testbar.
 */

type Promo = 'q' | 'r' | 'b' | 'n';

/** UCI-String (z.B. "e7e8q") in From/To/Promotion zerlegen. */
export function parseUci(uci: string): { from: Square; to: Square; promotion?: Promo } {
  return {
    from: uci.substring(0, 2) as Square,
    to: uci.substring(2, 4) as Square,
    promotion: uci.length > 4 ? (uci[4] as Promo) : undefined,
  };
}

/** UCI-Zug auf das Brett anwenden; gibt den chess.js-Move zurück (für SAN etc.). */
export function applyUci(chess: Chess, uci: string): Move {
  return chess.move(parseUci(uci));
}

/**
 * Freien (vom User gewählten) Zug von orig→dest anwenden. Promotion default Dame.
 * Gibt den Move zurück oder null, wenn illegal (Aufrufer überspringt dann).
 */
export function tryFreeMove(chess: Chess, orig: Key, dest: Key, promotion?: string): Move | null {
  const from = orig as string as Square;
  const to = dest as string as Square;
  try {
    if (promotion) {
      return chess.move({ from, to, promotion: promotion as Promo });
    }
    const match = chess.moves({ verbose: true }).find(m => m.from === from && m.to === to);
    if (!match) return null;                                   // illegaler Zug
    // Umwandlungszug ohne explizit gewählte Figur → Dame (chess.js' erster Verbose-Move
    // wäre sonst der Springer). Sonst der gefundene reguläre Zug.
    return match.promotion ? chess.move({ from, to, promotion: 'q' }) : chess.move(match);
  } catch {
    return null;
  }
}

/**
 * Legale Züge der aktuellen Stellung als chessground-dests-Map (from → [to,...]).
 * <p>`enPassantForced` (Anarchy-Modus, `?anarchy=max`): Ist in der Stellung ein En-passant-Schlag
 * möglich (chess.js-Flag `e`), werden NUR diese Züge zurückgegeben — „en passant is forced". Sonst
 * (kein En passant verfügbar) bleiben alle legalen Züge erlaubt.</p>
 */
export function calcDests(chess: Chess, enPassantForced = false): Map<Key, Key[]> {
  const moves = chess.moves({ verbose: true });
  const list = enPassantForced && moves.some(m => (m.flags as string).includes('e'))
    ? moves.filter(m => (m.flags as string).includes('e'))
    : moves;
  const dests = new Map<Key, Key[]>();
  for (const m of list) {
    const from = m.from as Key;
    if (!dests.has(from)) dests.set(from, []);
    dests.get(from)!.push(m.to as Key);
  }
  return dests;
}

/**
 * SAN-Zugliste mit korrekten Zugnummern formatieren (für den Visualisierungs-Modus),
 * ab einer Startstellung (Farbe am Zug + Vollzug-Nummer). Beispiel: "10... O-O 11. Qh5".
 */
export function formatSanList(moves: string[], startWhite: boolean, startNum: number): string {
  if (!moves.length) return '';
  const parts: string[] = [];
  let num = startNum;
  let white = startWhite;
  let first = true;
  for (const san of moves) {
    if (white) { parts.push(`${num}.`, san); }
    else { if (first) parts.push(`${num}...`); parts.push(san); num++; }
    white = !white;
    first = false;
  }
  return parts.join(' ');
}

/**
 * Wie {@link formatSanList}, aber Gegnerzüge (immer Index 1, 3, 5… in der viz-Sequenz —
 * der erste Zug ist stets der User) werden in `<strong>` eingebettet.
 * Ausgabe ist sicheres HTML (nur SAN-Notation + Zugnummern + strong-Tags).
 */
export function formatSanListHtml(moves: string[], startWhite: boolean, startNum: number): string {
  if (!moves.length) return '';
  const parts: string[] = [];
  let num = startNum;
  let white = startWhite;
  let first = true;
  for (let i = 0; i < moves.length; i++) {
    const san = moves[i];
    const isOpponent = i % 2 === 1;
    const sanHtml = isOpponent ? `<strong>${san}</strong>` : san;
    if (white) { parts.push(`${num}.`, sanHtml); }
    else { if (first) parts.push(`${num}...`); parts.push(sanHtml); num++; }
    white = !white;
    first = false;
  }
  return parts.join(' ');
}
