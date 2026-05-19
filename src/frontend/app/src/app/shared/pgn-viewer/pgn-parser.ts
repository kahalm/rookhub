import { Chess, Move } from 'chess.js';

export interface ParsedGame {
  headers: Record<string, string>;
  moves: Move[];
  fens: string[];
}

export const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

export function parsePgnText(pgnText: string): ParsedGame[] {
  const rawGames = pgnText.split(/\n\n(?=\[Event )/);
  const parsed: ParsedGame[] = [];

  for (const raw of rawGames) {
    const trimmed = raw.trim();
    if (!trimmed) continue;

    try {
      const chess = new Chess();
      chess.loadPgn(trimmed);

      const headers = chess.getHeaders();
      const moves = chess.history({ verbose: true });

      const fens: string[] = [START_FEN];
      for (const move of moves) {
        fens.push(move.after);
      }

      parsed.push({ headers, moves, fens });
    } catch {
      // Skip unparseable games
    }
  }

  return parsed;
}
