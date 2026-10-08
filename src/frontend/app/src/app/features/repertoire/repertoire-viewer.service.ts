import { Injectable } from '@angular/core';
import { Move } from 'chess.js';
import { ParsedGame, ParsedGameWithSource, START_FEN, parsePgnTextWithSource, parsePgnTextWithSourceAsync } from '../../shared/pgn-viewer/pgn-parser';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { sideOfLastMove, TrainColor } from './repertoire-color.util';
import { isInfoLineGame } from './repertoire-info-line.util';
import { startNumbering } from './repertoire-move-format.util';

export interface RepertoireLine {
  gameIndex: number;
  summary: string;
  opening: string;
  white: string;
  black: string;
  result: string;
  moveCount: number;
  /** Chapter-Label (Chessable-Konvention: Black-Header) — für Gruppierung in der Lines-Ansicht. */
  chapter: string;
  /** Stabiler SR-Schlüssel (identisch zum Trainer) — für Pool-/Fälligkeits-Anzeige + Aktionen. */
  lineKey: string;
  /** Info-Linie (siehe {@link isInfoLineGame}): nur durchklicken, nicht trainieren. */
  isInfo?: boolean;
  /** Start-FEN der Linie (für die Trainingsfarb-Erkennung). */
  startFen: string;
  /** Seite des letzten Halbzugs — Signal für die automatische Trainingsfarbe je Kapitel. */
  lastMoveSide: TrainColor | null;
}

@Injectable()
export class RepertoireViewerService {
  games: ParsedGame[] = [];
  /** Originaltext je Spiel, parallel zu {@link games} — Varianten/Kommentare/Marker unverändert (Linien-Download). */
  rawGames: string[] = [];
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
    // Varianten als Kommentartext behalten: die Linienansicht macht ihre Züge klickbar.
    this.loadParsed(parsePgnTextWithSource(pgnText, { foldVariations: true }));
  }

  /** Das GANZE Repertoire in Portionen lesen (0.712.0) — vorher endete die Linienliste nach 2 MB/500 Partien. */
  async loadPgnAsync(pgnText: string, onProgress?: (done: number, total: number) => void): Promise<void> {
    this.loadParsed(await parsePgnTextWithSourceAsync(pgnText, { foldVariations: true }, onProgress));
  }

  private loadParsed(parsed: ParsedGameWithSource[]): void {
    this.games = parsed.map(p => p.game);
    this.rawGames = parsed.map(p => p.raw);
    this.lines = this.games.map((game, i) => this.buildLine(game, i, isInfoLineGame(this.rawGames[i])));
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

  private buildLine(game: ParsedGame, index: number, isInfo: boolean): RepertoireLine {
    const moves = game.moves;
    // Nummerierung ab der Startstellung des Abschnitts: eine Linie mit [FEN] (z. B. eine Übung aus einer Modellpartie)
    // zeigt „11. d3 Nf6 12. …" statt „1. d3 Nf6 2. …".
    const { side, fullMove } = startNumbering(game.fens[0]);
    const summaryMoves: string[] = [];
    let num = fullMove;
    let toMove = side;
    for (let i = 0; i < Math.min(moves.length, 8); i++) {
      if (toMove === 'w') summaryMoves.push(`${num}.`);
      else if (i === 0) summaryMoves.push(`${num}…`);
      summaryMoves.push(moves[i].san);
      if (toMove === 'b') num++;
      toMove = toMove === 'w' ? 'b' : 'w';
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
      chapter: (game.headers['Black'] || '').trim(),
      lineKey: lineKeyFromSans(moves.map(m => m.san)),
      isInfo,
      startFen: game.fens[0],
      lastMoveSide: sideOfLastMove(game.fens[0], moves.length),
    };
  }
}
