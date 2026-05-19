import { Move } from 'chess.js';
import { ParsedGame, START_FEN, parsePgnText } from '../../shared/pgn-viewer/pgn-parser';

export interface RepertoireLine {
  gameIndex: number;
  summary: string;
  opening: string;
  white: string;
  black: string;
  result: string;
  moveCount: number;
}

export class RepertoireViewerService {
  games: ParsedGame[] = [];
  lines: RepertoireLine[] = [];
  selectedLineIndex = -1;
  currentMoveIndex = -1;

  get selectedGame(): ParsedGame | null {
    if (this.selectedLineIndex < 0) return null;
    const line = this.lines[this.selectedLineIndex];
    return this.games[line.gameIndex] ?? null;
  }

  get currentMoves(): Move[] {
    return this.selectedGame?.moves ?? [];
  }

  get currentComments(): { [moveIndex: number]: string } {
    return this.selectedGame?.comments ?? {};
  }

  get currentFen(): string {
    const game = this.selectedGame;
    if (!game) return START_FEN;
    if (this.currentMoveIndex < 0) return game.fens[0];
    return game.fens[this.currentMoveIndex + 1];
  }

  get lastMove(): [string, string] | undefined {
    const game = this.selectedGame;
    if (!game || this.currentMoveIndex < 0) return undefined;
    const move = game.moves[this.currentMoveIndex];
    return [move.from, move.to];
  }

  loadPgn(pgnText: string): void {
    this.games = parsePgnText(pgnText);
    this.lines = this.games.map((game, i) => this.buildLine(game, i));
    this.selectedLineIndex = -1;
    this.currentMoveIndex = -1;
  }

  selectLine(index: number): void {
    if (index >= 0 && index < this.lines.length) {
      this.selectedLineIndex = index;
      this.currentMoveIndex = -1;
    }
  }

  deselectLine(): void {
    this.selectedLineIndex = -1;
    this.currentMoveIndex = -1;
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
    const game = this.selectedGame;
    if (game && this.currentMoveIndex < game.moves.length - 1) {
      this.currentMoveIndex++;
    }
  }

  goToEnd(): void {
    const game = this.selectedGame;
    if (game && game.moves.length > 0) {
      this.currentMoveIndex = game.moves.length - 1;
    }
  }

  goToMove(index: number): void {
    const game = this.selectedGame;
    if (game && index >= -1 && index < game.moves.length) {
      this.currentMoveIndex = index;
    }
  }

  private buildLine(game: ParsedGame, index: number): RepertoireLine {
    const moves = game.moves;
    const summaryMoves: string[] = [];
    for (let i = 0; i < Math.min(moves.length, 8); i++) {
      if (i % 2 === 0) summaryMoves.push(`${Math.floor(i / 2) + 1}.`);
      summaryMoves.push(moves[i].san);
    }
    if (moves.length > 8) summaryMoves.push('...');

    return {
      gameIndex: index,
      summary: summaryMoves.join(' '),
      opening: game.headers['Opening'] || game.headers['ECO'] || '',
      white: game.headers['White'] || '?',
      black: game.headers['Black'] || '?',
      result: game.headers['Result'] || '*',
      moveCount: Math.ceil(moves.length / 2),
    };
  }
}
