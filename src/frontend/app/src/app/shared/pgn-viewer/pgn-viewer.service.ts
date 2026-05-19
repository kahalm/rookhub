import { Chess, Move } from 'chess.js';

export interface ParsedGame {
  headers: Record<string, string>;
  moves: Move[];
  fens: string[];
}

export class PgnViewerService {
  games: ParsedGame[] = [];
  currentGameIndex = 0;
  currentMoveIndex = -1;

  get currentGame(): ParsedGame | null {
    return this.games[this.currentGameIndex] ?? null;
  }

  get currentFen(): string {
    const game = this.currentGame;
    if (!game) return 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
    if (this.currentMoveIndex < 0) return game.fens[0];
    return game.fens[this.currentMoveIndex + 1];
  }

  get lastMove(): [string, string] | undefined {
    const game = this.currentGame;
    if (!game || this.currentMoveIndex < 0) return undefined;
    const move = game.moves[this.currentMoveIndex];
    return [move.from, move.to];
  }

  loadPgn(pgnText: string): void {
    this.games = this.splitAndParse(pgnText);
    this.currentGameIndex = 0;
    this.currentMoveIndex = -1;
  }

  selectGame(index: number): void {
    if (index >= 0 && index < this.games.length) {
      this.currentGameIndex = index;
      this.currentMoveIndex = -1;
    }
  }

  goToStart(): void {
    this.currentMoveIndex = -1;
  }

  goBack(): void {
    if (this.currentMoveIndex >= 0) {
      this.currentMoveIndex--;
    }
  }

  goForward(): void {
    const game = this.currentGame;
    if (game && this.currentMoveIndex < game.moves.length - 1) {
      this.currentMoveIndex++;
    }
  }

  goToEnd(): void {
    const game = this.currentGame;
    if (game && game.moves.length > 0) {
      this.currentMoveIndex = game.moves.length - 1;
    }
  }

  goToMove(index: number): void {
    const game = this.currentGame;
    if (game && index >= -1 && index < game.moves.length) {
      this.currentMoveIndex = index;
    }
  }

  private splitAndParse(pgnText: string): ParsedGame[] {
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

        const fens: string[] = ['rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1'];
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
}
