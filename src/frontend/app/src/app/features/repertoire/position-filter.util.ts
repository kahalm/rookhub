import { ParsedGame } from '../../shared/pgn-viewer/pgn-parser';

/**
 * Erste 4 FEN-Felder (Brett, Seite am Zug, Rochade, En-passant) — Transpositions-Match
 * unabhängig von Zugzahl/50-Züge-Zähler. Spiegel von `RepertoireAnalyzeService.NormalizeFen`
 * (Server-Stellungssuche); beide Seiten müssen gleich normalisieren.
 */
export function normalizeFen(fen: string): string {
  const parts = fen.split(' ');
  return parts.length >= 4 ? parts.slice(0, 4).join(' ') : fen;
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

/** SAN-Folge mit Zugnummern („1. e4 e5 2. Nf3 …") — das Filterbrett startet aus der Grundstellung. */
export function formatSansWithNumbers(sans: string[]): string {
  const parts: string[] = [];
  sans.forEach((san, i) => {
    if (i % 2 === 0) parts.push(`${i / 2 + 1}.`);
    parts.push(san);
  });
  return parts.join(' ');
}
