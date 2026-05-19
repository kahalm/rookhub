import { ParsedGame, START_FEN, parsePgnText } from './pgn-parser';

export type { ParsedGame } from './pgn-parser';

export class PgnViewerService {
  games: ParsedGame[] = [];
  currentGameIndex = 0;
  currentMoveIndex = -1;

  get currentGame(): ParsedGame | null {
    return this.games[this.currentGameIndex] ?? null;
  }

  get currentFen(): string {
    const game = this.currentGame;
    if (!game) return START_FEN;
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
    this.games = parsePgnText(pgnText);
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
}
