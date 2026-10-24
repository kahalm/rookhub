import { ParsedGame } from '../../shared/pgn-viewer/pgn-parser';
import { whiteToMove } from '../../shared/chess/fen-side';

/**
 * Erste DREI FEN-Felder (Brett, Seite am Zug, Rochade) — Transpositions-Match unabhängig von
 * Zugzahl/50-Züge-Zähler UND vom En-passant-Feld. Spiegel von
 * `RepertoirePositionLookupService.NormalizeKey` (Server-Stellungssuche); beide Seiten müssen
 * gleich normalisieren.
 *
 * WARUM ep NICHT im Schlüssel steht: dieselbe Stellung entsteht mit und ohne ep-Recht, je nachdem
 * ob die Linie mit einem Doppelschritt endet oder über zwei Einzelschritte dorthin kommt. Mit ep im
 * Schlüssel filterte das Brett solche Linien auf 0 heraus („kommt nicht vor"), während der Knopf
 * „In welchen Repertoires?" sie sehr wohl fand — der Server lässt das Feld genau deshalb weg. Der
 * Fall ist nicht exotisch: schon in den ersten sechs Halbzügen gibt es 226 Stellungen, die sich bei
 * identischem Brett, Seite und Rochade nur im ep-Feld unterscheiden.
 */
export function normalizeFen(fen: string): string {
  const parts = fen.split(' ');
  return parts.length >= 3 ? parts.slice(0, 3).join(' ') : fen;
}

export interface PositionFilterResult {
  /** gameIndex-Menge der Linien, die die Stellung enthalten. */
  gameIndices: Set<number>;
  /** Je gameIndex der fens-Index des ersten Treffers (0 = Startstellung der Linie). */
  plyByGame: Map<number, number>;
}

/** Sucht die Stellung in allen Linien (Hauptlinien-FENs, Transpositionen zählen). */
export function findPositionInGames(games: ParsedGame[], fen: string): PositionFilterResult {
  const target = normalizeFen(fen);
  const gameIndices = new Set<number>();
  const plyByGame = new Map<number, number>();
  games.forEach((game, i) => {
    const at = game.fens.findIndex(f => normalizeFen(f) === target);
    if (at >= 0) { gameIndices.add(i); plyByGame.set(i, at); }
  });
  return { gameIndices, plyByGame };
}

/**
 * Gemeinsame Ausgangsstellung aller Linien (b1-rk-repertoire-filterbrett-fen-start): ein Endspiel-Repertoire startet
 * ab einer FEN — das Filterbrett muss dort stehen, sonst trifft es nie eine Linie. Ohne Linien `fallback`;
 * starten die Linien aus verschiedenen Stellungen, `null` (dann gibt es keinen gemeinsamen Filter).
 */
export function commonStartFen(startFens: readonly string[], fallback: string): string | null {
  if (!startFens.length) return fallback;
  const first = normalizeFen(startFens[0]);
  return startFens.every(f => normalizeFen(f) === first) ? startFens[0] : null;
}

/** SAN-Folge mit Zugnummern („1. e4 e5 2. Nf3 …"); ab einer FEN mit deren Zugnummer, Schwarz am Zug „1… Kd8". */
export function formatSansWithNumbers(sans: string[], startFen?: string): string {
  const f = (startFen ?? '').split(' ');
  const blackFirst = !whiteToMove(startFen);
  const firstNo = Math.max(1, parseInt(f[5] ?? '', 10) || 1);
  const parts: string[] = [];
  sans.forEach((san, i) => {
    const ply = i + (blackFirst ? 1 : 0);          // 0 = Weiß am Zug im ersten Zug
    const no = firstNo + Math.floor(ply / 2);
    if (ply % 2 === 0) parts.push(`${no}.`);
    else if (i === 0) parts.push(`${no}…`);
    parts.push(san);
  });
  return parts.join(' ');
}
