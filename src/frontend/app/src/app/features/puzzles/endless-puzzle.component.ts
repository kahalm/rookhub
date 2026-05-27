import { Component, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { PuzzleService, PuzzleDto } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { AuthService } from '../../core/auth.service';
import { Chess, Square } from 'chess.js';
import { Color, Key } from 'chessground/types';

type EndlessState = 'CONFIG' | 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE'
  | 'CORRECT' | 'REFUTATION' | 'REFUTATION_USER' | 'WRONG' | 'GAME_OVER';

interface EndlessConfig {
  startElo: number;
  step: number;
  rangeWidth: number;
  themes: string;
}

const CONFIG_KEY = 'rookhub_endless_config';
const HIGHSCORE_KEY = 'rookhub_endless_highscore';

@Component({
  selector: 'app-endless-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatProgressSpinnerModule, PuzzleBoardComponent
  ],
  template: `
    <div class="endless-page">
      @switch (screen) {
        @case ('config') {
          <div class="config-screen">
            <mat-card class="config-card">
              <mat-card-header>
                <mat-card-title>Endless Puzzle Mode</mat-card-title>
                <mat-card-subtitle>Progressive difficulty — how far can you go?</mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <div class="config-fields">
                  <mat-form-field appearance="outline">
                    <mat-label>Start Rating</mat-label>
                    <input matInput type="number" [(ngModel)]="config.startElo" min="100" max="3000" step="50">
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Step Size</mat-label>
                    <input matInput type="number" [(ngModel)]="config.step" min="5" max="200" step="5">
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Range Width</mat-label>
                    <input matInput type="number" [(ngModel)]="config.rangeWidth" min="5" max="500" step="5">
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Themes (optional)</mat-label>
                    <input matInput [(ngModel)]="config.themes" placeholder="e.g. fork pin">
                  </mat-form-field>
                </div>

                <div class="level-preview">
                  <h4>Level Preview</h4>
                  <div class="preview-levels">
                    @for (lvl of previewLevels; track lvl.level) {
                      <span class="preview-chip">Lv {{ lvl.level }}: {{ lvl.min }}–{{ lvl.max }}</span>
                    }
                  </div>
                </div>

                <div class="lives-display config-lives">
                  @for (i of [1,2,3]; track i) {
                    <mat-icon class="heart full">favorite</mat-icon>
                  }
                </div>

                @if (highscore > 0) {
                  <div class="highscore-badge">
                    <mat-icon>emoji_events</mat-icon>
                    Highscore: {{ highscore }} Rating
                  </div>
                }

                <button mat-raised-button color="primary" class="start-btn" (click)="startGame()">
                  <mat-icon>play_arrow</mat-icon>
                  Start
                </button>
              </mat-card-content>
            </mat-card>
          </div>
        }

        @case ('play') {
          <div class="play-screen">
            <div class="board-section">
              <app-puzzle-board
                [fen]="boardFen"
                [orientation]="orientation"
                [turnColor]="turnColor"
                [dests]="dests"
                [lastMove]="lastMove"
                [viewOnly]="state !== 'AWAITING_USER_MOVE' && state !== 'REFUTATION_USER'"
                [check]="isCheck"
                (moveMade)="onMoveMade($event)"
              />
            </div>

            <div class="info-section">
              <mat-card class="status-card">
                <mat-card-content>
                  @switch (state) {
                    @case ('LOADING') {
                      <div class="status-center">
                        <mat-spinner diameter="40"></mat-spinner>
                        <p>Loading puzzle...</p>
                      </div>
                    }
                    @case ('SETUP') {
                      <div class="status-center">
                        <p class="status-text">Watch the opponent's move...</p>
                      </div>
                    }
                    @case ('AWAITING_USER_MOVE') {
                      <div class="status-center">
                        <p class="status-text">Your turn! Find the best move.</p>
                      </div>
                    }
                    @case ('CORRECT') {
                      <div class="status-center solved">
                        <mat-icon class="result-icon">check_circle</mat-icon>
                        @if (alternativeSolve) {
                          <p class="status-text">Checkmate!</p>
                          <p class="alt-hint">Alternative solution — the puzzle had a different intended line.</p>
                        } @else {
                          <p class="status-text">Correct!</p>
                        }
                      </div>
                    }
                    @case ('REFUTATION') {
                      <div class="status-center refutation">
                        <mat-spinner diameter="24"></mat-spinner>
                        <p class="status-text">Stockfish is thinking...</p>
                        @if (showEval && currentEval) {
                          <p class="eval-display">{{ currentEval }}</p>
                        }
                        <div class="refutation-actions">
                          <button mat-button (click)="toggleEval()">
                            <mat-icon>analytics</mat-icon>
                            {{ showEval ? 'Hide Eval' : 'Show Eval' }}
                          </button>
                          <button mat-button (click)="resetPuzzle()">
                            <mat-icon>replay</mat-icon>
                            Reset
                          </button>
                          <button mat-button color="warn" (click)="giveUp()">
                            <mat-icon>flag</mat-icon>
                            Give Up
                          </button>
                        </div>
                      </div>
                    }
                    @case ('REFUTATION_USER') {
                      <div class="status-center refutation">
                        <p class="status-text">Your move...</p>
                        @if (showEval && currentEval) {
                          <p class="eval-display">{{ currentEval }}</p>
                        }
                        <div class="refutation-actions">
                          <button mat-button (click)="toggleEval()">
                            <mat-icon>analytics</mat-icon>
                            {{ showEval ? 'Hide Eval' : 'Show Eval' }}
                          </button>
                          <button mat-button (click)="resetPuzzle()">
                            <mat-icon>replay</mat-icon>
                            Reset
                          </button>
                          <button mat-button color="warn" (click)="giveUp()">
                            <mat-icon>flag</mat-icon>
                            Give Up
                          </button>
                        </div>
                      </div>
                    }
                    @case ('WRONG') {
                      <div class="status-center failed">
                        <mat-icon class="result-icon">cancel</mat-icon>
                        <p class="status-text">Wrong!</p>
                      </div>
                    }
                  }
                </mat-card-content>
              </mat-card>

              <mat-card class="stats-card">
                <mat-card-content>
                  <div class="stats-grid">
                    <div class="stat">
                      <span class="stat-value">{{ currentRating }}</span>
                      <span class="stat-label">Rating</span>
                    </div>
                    <div class="stat">
                      <span class="stat-value">{{ level }}</span>
                      <span class="stat-label">Level</span>
                    </div>
                    <div class="stat">
                      <span class="stat-value">{{ solved }}</span>
                      <span class="stat-label">Solved</span>
                    </div>
                    <div class="stat">
                      <span class="stat-value">{{ formatTime(sessionSeconds) }}</span>
                      <span class="stat-label">Time</span>
                    </div>
                  </div>
                  <div class="lives-display">
                    @for (i of [1,2,3]; track i) {
                      <mat-icon [class]="i <= lives ? 'heart full' : 'heart empty'">
                        {{ i <= lives ? 'favorite' : 'favorite_border' }}
                      </mat-icon>
                    }
                  </div>
                </mat-card-content>
              </mat-card>

              @if (puzzle) {
                <mat-card class="info-card">
                  <mat-card-content>
                    <div class="puzzle-info">
                      <span class="rating-badge">Puzzle Rating: {{ puzzle.rating }}</span>
                      <span class="level-badge">Level {{ level }} ({{ currentMinRating }}–{{ currentMaxRating }})</span>
                      @if (puzzle.themes) {
                        <div class="themes">
                          @for (theme of puzzle.themes.split(' '); track theme) {
                            <span class="theme-chip">{{ theme }}</span>
                          }
                        </div>
                      }
                    </div>
                  </mat-card-content>
                </mat-card>
              }
            </div>
          </div>
        }

        @case ('gameover') {
          <div class="gameover-screen">
            <mat-card class="gameover-card">
              <mat-card-header>
                <mat-card-title>Game Over</mat-card-title>
              </mat-card-header>
              <mat-card-content>
                <div class="gameover-stats">
                  <div class="gameover-stat">
                    <mat-icon>trending_up</mat-icon>
                    <span class="go-value">{{ maxRatingReached }}</span>
                    <span class="go-label">Max Rating</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>extension</mat-icon>
                    <span class="go-value">{{ solved }}</span>
                    <span class="go-label">Puzzles Solved</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>stacked_line_chart</mat-icon>
                    <span class="go-value">{{ level }}</span>
                    <span class="go-label">Level Reached</span>
                  </div>
                  <div class="gameover-stat">
                    <mat-icon>timer</mat-icon>
                    <span class="go-value">{{ formatTime(sessionSeconds) }}</span>
                    <span class="go-label">Time</span>
                  </div>
                </div>
                @if (isNewHighscore) {
                  <div class="new-highscore">
                    <mat-icon>emoji_events</mat-icon>
                    New Highscore!
                  </div>
                }
                <div class="gameover-actions">
                  <button mat-raised-button color="primary" (click)="playAgain()">
                    <mat-icon>replay</mat-icon>
                    Play Again
                  </button>
                  <button mat-button (click)="backToPuzzles()">
                    <mat-icon>arrow_back</mat-icon>
                    Back to Puzzles
                  </button>
                </div>
              </mat-card-content>
            </mat-card>
          </div>
        }
      }
    </div>
  `,
  styles: [`
    .endless-page { padding: 1rem; max-width: 1200px; margin: 0 auto; }

    /* Config Screen */
    .config-screen { display: flex; justify-content: center; padding-top: 2rem; }
    .config-card { max-width: 500px; width: 100%; }
    .config-fields { display: grid; grid-template-columns: 1fr 1fr; gap: 0 1rem; margin-top: 1rem; }
    .config-fields mat-form-field:last-child { grid-column: 1 / -1; }
    .level-preview { margin-bottom: 1rem; }
    .level-preview h4 { margin: 0 0 0.5rem; color: rgba(0,0,0,0.6); font-size: 0.9em; }
    .preview-levels { display: flex; flex-wrap: wrap; gap: 0.5rem; }
    .preview-chip {
      background: rgba(0,0,0,0.06); border-radius: 12px; padding: 4px 12px;
      font-size: 0.85em; font-variant-numeric: tabular-nums;
    }
    .config-lives { justify-content: center; margin: 1rem 0; }
    .highscore-badge {
      display: flex; align-items: center; gap: 0.5rem; justify-content: center;
      color: #ff9800; font-weight: 500; margin-bottom: 1rem;
    }
    .start-btn { width: 100%; height: 48px; font-size: 1.1em; }

    /* Play Screen */
    .play-screen { display: flex; gap: 1.5rem; align-items: flex-start; }
    .board-section { flex: 0 0 auto; width: min(60vw, 560px); min-width: 280px; }
    .info-section { flex: 1; min-width: 250px; display: flex; flex-direction: column; gap: 1rem; }
    .status-card { min-height: 80px; }
    .status-center { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 0.75rem 0; }
    .status-text { font-size: 1.1em; font-weight: 500; margin: 0; }
    .result-icon { font-size: 48px; width: 48px; height: 48px; }
    .solved .result-icon { color: #4caf50; }
    .failed .result-icon { color: #f44336; }
    .refutation .status-text { color: #ff9800; }
    .alt-hint { font-size: 0.85em; color: rgba(0,0,0,0.6); margin: 0; text-align: center; }
    .eval-display {
      font-size: 1.4em; font-weight: bold; font-variant-numeric: tabular-nums;
      margin: 0; padding: 4px 12px; border-radius: 6px; background: rgba(0,0,0,0.06);
    }
    .refutation-actions { display: flex; gap: 0.25rem; flex-wrap: wrap; justify-content: center; margin-top: 0.25rem; }
    .stats-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 0.5rem; text-align: center; }
    .stat-value { font-size: 1.3em; font-weight: bold; display: block; }
    .stat-label { font-size: 0.8em; color: rgba(0,0,0,0.6); }
    .lives-display { display: flex; gap: 0.25rem; justify-content: center; margin-top: 0.75rem; }
    .heart { font-size: 28px; width: 28px; height: 28px; }
    .heart.full { color: #f44336; }
    .heart.empty { color: rgba(0,0,0,0.2); }
    .puzzle-info { display: flex; flex-direction: column; gap: 0.5rem; }
    .rating-badge { font-weight: bold; font-size: 1.1em; }
    .level-badge { font-size: 0.9em; color: rgba(0,0,0,0.6); }
    .themes { display: flex; flex-wrap: wrap; gap: 0.25rem; }
    .theme-chip {
      background: rgba(0,0,0,0.08); border-radius: 12px; padding: 2px 10px;
      font-size: 0.85em; white-space: nowrap;
    }

    /* Game Over Screen */
    .gameover-screen { display: flex; justify-content: center; padding-top: 2rem; }
    .gameover-card { max-width: 500px; width: 100%; text-align: center; }
    .gameover-stats { display: grid; grid-template-columns: repeat(2, 1fr); gap: 1.5rem; margin: 1.5rem 0; }
    .gameover-stat { display: flex; flex-direction: column; align-items: center; gap: 0.25rem; }
    .gameover-stat mat-icon { color: rgba(0,0,0,0.5); }
    .go-value { font-size: 1.5em; font-weight: bold; }
    .go-label { font-size: 0.85em; color: rgba(0,0,0,0.6); }
    .new-highscore {
      display: flex; align-items: center; gap: 0.5rem; justify-content: center;
      color: #ff9800; font-size: 1.2em; font-weight: bold; margin-bottom: 1rem;
    }
    .gameover-actions { display: flex; flex-direction: column; gap: 0.5rem; margin-top: 1rem; }

    @media (max-width: 768px) {
      .play-screen { flex-direction: column; }
      .board-section { width: 100%; }
      .stats-grid { grid-template-columns: repeat(2, 1fr); }
      .config-fields { grid-template-columns: 1fr; }
    }
  `]
})
export class EndlessPuzzleComponent implements OnDestroy {
  get screen(): 'config' | 'play' | 'gameover' {
    if (this.state === 'GAME_OVER') return 'gameover';
    if (this.state === 'CONFIG') return 'config';
    return 'play';
  }

  state: EndlessState = 'CONFIG';
  config: EndlessConfig = { startElo: 700, step: 40, rangeWidth: 40, themes: '' };

  // Game state
  lives = 3;
  level = 0;
  solved = 0;
  maxRatingReached = 0;
  isNewHighscore = false;
  highscore = 0;
  alternativeSolve = false;

  // Refutation state
  inRefutation = false;
  showEval = false;
  currentEval = '';

  // Session timer
  sessionSeconds = 0;
  private sessionInterval?: ReturnType<typeof setInterval>;
  private sessionStart = 0;

  // Board state
  puzzle: PuzzleDto | null = null;
  boardFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  orientation: Color = 'white';
  turnColor: Color = 'white';
  dests: Map<Key, Key[]> = new Map();
  lastMove?: [Key, Key];
  isCheck = false;

  // Puzzle solving
  private chess = new Chess();
  private solutionMoves: string[] = [];
  private moveIndex = 0;
  private prefetchedPuzzle: PuzzleDto | null = null;
  private autoAdvanceTimer?: ReturnType<typeof setTimeout>;
  private refutationAborted = false;

  constructor(
    private puzzleService: PuzzleService,
    private stockfish: StockfishService,
    private authService: AuthService,
    private router: Router
  ) {
    this.loadConfig();
    this.loadHighscore();
    this.stockfish.init().catch(() => {});
  }

  ngOnDestroy(): void {
    this.stopSessionTimer();
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.stockfish.destroy();
  }

  // --- Config helpers ---

  get previewLevels(): { level: number; min: number; max: number }[] {
    const levels = [];
    for (let i = 0; i < 3; i++) {
      const min = this.config.startElo + i * this.config.step;
      levels.push({ level: i, min, max: min + this.config.rangeWidth });
    }
    return levels;
  }

  get currentMinRating(): number {
    return this.config.startElo + this.level * this.config.step;
  }

  get currentMaxRating(): number {
    return this.currentMinRating + this.config.rangeWidth;
  }

  get currentRating(): number {
    return this.currentMinRating;
  }

  // --- Game lifecycle ---

  startGame(): void {
    this.saveConfig();
    this.lives = 3;
    this.level = 0;
    this.solved = 0;
    this.maxRatingReached = this.config.startElo;
    this.isNewHighscore = false;
    this.prefetchedPuzzle = null;
    this.sessionSeconds = 0;
    this.startSessionTimer();
    this.loadPuzzle();
  }

  playAgain(): void {
    this.state = 'CONFIG';
  }

  backToPuzzles(): void {
    this.router.navigate(['/puzzles']);
  }

  // --- Puzzle loading ---

  private loadPuzzle(): void {
    this.state = 'LOADING';
    this.inRefutation = false;
    this.alternativeSolve = false;
    this.currentEval = '';
    const min = this.currentMinRating;
    const max = this.currentMaxRating;

    if (this.prefetchedPuzzle &&
        this.prefetchedPuzzle.rating >= min &&
        this.prefetchedPuzzle.rating <= max) {
      const p = this.prefetchedPuzzle;
      this.prefetchedPuzzle = null;
      this.onPuzzleLoaded(p);
      return;
    }

    this.prefetchedPuzzle = null;
    const themes = this.config.themes.trim() || undefined;
    this.puzzleService.getRandom(min, max, themes).subscribe({
      next: p => this.onPuzzleLoaded(p),
      error: () => this.endGame()
    });
  }

  private onPuzzleLoaded(puzzle: PuzzleDto): void {
    this.puzzle = puzzle;
    this.trackMaxRating(puzzle.rating);
    this.setupPuzzle(puzzle);
    this.prefetchNext();
  }

  private prefetchNext(): void {
    const nextLevel = this.level + 1;
    const min = this.config.startElo + nextLevel * this.config.step;
    const max = min + this.config.rangeWidth;
    const themes = this.config.themes.trim() || undefined;
    this.puzzleService.getRandom(min, max, themes).subscribe({
      next: p => this.prefetchedPuzzle = p,
      error: () => this.prefetchedPuzzle = null
    });
  }

  // --- Puzzle setup & moves ---

  private setupPuzzle(puzzle: PuzzleDto): void {
    this.solutionMoves = puzzle.moves.split(' ');
    this.moveIndex = 0;
    this.chess = new Chess(puzzle.fen);
    this.inRefutation = false;
    this.refutationAborted = false;

    const setupMove = this.solutionMoves[0];
    const setupFrom = setupMove.substring(0, 2) as Square;
    const piece = this.chess.get(setupFrom);
    this.orientation = piece?.color === 'w' ? 'black' : 'white';

    this.updateBoard();
    this.state = 'SETUP';

    setTimeout(() => {
      if (this.state !== 'SETUP') return;
      this.playMove(this.solutionMoves[0]);
      this.moveIndex = 1;
      this.state = 'AWAITING_USER_MOVE';
      this.updateBoard();
    }, 600);
  }

  onMoveMade(event: { orig: Key; dest: Key }): void {
    if (this.state === 'REFUTATION_USER') {
      this.onRefutationUserMove(event);
      return;
    }

    if (this.state !== 'AWAITING_USER_MOVE') return;

    const expectedUci = this.solutionMoves[this.moveIndex];
    const userUci = event.orig + event.dest;

    if (userUci === expectedUci.substring(0, 4)) {
      this.playMove(expectedUci);
      this.moveIndex++;

      if (this.moveIndex >= this.solutionMoves.length) {
        this.onPuzzleSolved(false);
      } else {
        this.updateBoard();
        setTimeout(() => {
          this.playMove(this.solutionMoves[this.moveIndex]);
          this.moveIndex++;
          this.updateBoard();

          if (this.moveIndex >= this.solutionMoves.length) {
            this.onPuzzleSolved(false);
          } else {
            this.state = 'AWAITING_USER_MOVE';
          }
        }, 400);
      }
    } else {
      this.startRefutation(event.orig, event.dest);
    }
  }

  private onPuzzleSolved(alternative: boolean): void {
    this.alternativeSolve = alternative;
    this.state = 'CORRECT';
    this.solved++;
    this.inRefutation = false;
    this.recordAttempt(true);
    this.updateBoard();

    this.autoAdvanceTimer = setTimeout(() => {
      this.level++;
      this.loadPuzzle();
    }, alternative ? 1500 : 800);
  }

  // --- Refutation flow (endless Stockfish play) ---

  private startRefutation(orig: Key, dest: Key): void {
    // Play the user's wrong move on chess.js
    const from = orig as string as Square;
    const to = dest as string as Square;
    const moves = this.chess.moves({ verbose: true });
    const matchingMove = moves.find(m => m.from === from && m.to === to);
    if (matchingMove) {
      this.chess.move(matchingMove);
    } else {
      try { this.chess.move({ from, to, promotion: 'q' }); } catch { /* ignore */ }
    }
    this.lastMove = [orig, dest];

    this.inRefutation = true;
    this.refutationAborted = false;

    if (this.chess.isGameOver()) {
      this.onRefutationGameOver();
      return;
    }

    this.playStockfishResponse();
  }

  private async playStockfishResponse(): Promise<void> {
    if (this.refutationAborted) return;
    this.state = 'REFUTATION';
    this.updateBoard();

    try {
      const result = await this.stockfish.getBestMove(this.chess.fen(), 12);
      if (this.refutationAborted) return;

      this.currentEval = result.eval;
      this.playMove(result.move);
      this.updateBoard();

      if (this.chess.isGameOver()) {
        this.onRefutationGameOver();
        return;
      }

      // Let user play next
      this.autoAdvanceTimer = setTimeout(() => {
        if (this.refutationAborted) return;
        this.state = 'REFUTATION_USER';
        this.updateBoard();
      }, 400);
    } catch {
      if (!this.refutationAborted) this.loseLife();
    }
  }

  private onRefutationUserMove(event: { orig: Key; dest: Key }): void {
    const from = event.orig as string as Square;
    const to = event.dest as string as Square;

    const moves = this.chess.moves({ verbose: true });
    const matchingMove = moves.find(m => m.from === from && m.to === to);
    if (matchingMove) {
      this.chess.move(matchingMove);
    } else {
      try { this.chess.move({ from, to, promotion: 'q' }); } catch { return; }
    }
    this.lastMove = [event.orig, event.dest];

    if (this.chess.isGameOver()) {
      this.updateBoard();
      this.onRefutationGameOver();
      return;
    }

    this.playStockfishResponse();
  }

  private onRefutationGameOver(): void {
    // User checkmated Stockfish?
    if (this.chess.isCheckmate()) {
      // Whose turn is it? The loser's.
      const loserColor = this.chess.turn(); // side that is checkmated
      const userColor = this.orientation === 'white' ? 'w' : 'b';

      if (loserColor !== userColor) {
        // User mated Stockfish → alternative solve!
        this.onPuzzleSolved(true);
        return;
      }
    }
    // Stockfish mated user, or draw → lose life
    this.loseLife();
  }

  private loseLife(): void {
    this.lives--;
    this.inRefutation = false;
    this.recordAttempt(false);
    this.state = 'WRONG';
    this.updateBoard();

    if (this.lives <= 0) {
      this.autoAdvanceTimer = setTimeout(() => this.endGame(), 1200);
    } else {
      this.prefetchedPuzzle = null;
      this.autoAdvanceTimer = setTimeout(() => this.loadPuzzle(), 1200);
    }
  }

  // --- Refutation buttons ---

  toggleEval(): void {
    this.showEval = !this.showEval;
    if (this.showEval && !this.currentEval && this.state === 'REFUTATION_USER') {
      this.stockfish.getEval(this.chess.fen(), 12)
        .then(ev => this.currentEval = ev)
        .catch(() => {});
    }
  }

  resetPuzzle(): void {
    if (!this.puzzle) return;
    this.refutationAborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.currentEval = '';
    this.setupPuzzle(this.puzzle);
  }

  giveUp(): void {
    this.refutationAborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.loseLife();
  }

  private endGame(): void {
    this.stopSessionTimer();
    this.checkHighscore();
    this.state = 'GAME_OVER';
  }

  // --- Board helpers ---

  private playMove(uci: string): void {
    const from = uci.substring(0, 2) as Square;
    const to = uci.substring(2, 4) as Square;
    const promotion = uci.length > 4 ? uci[4] as 'q' | 'r' | 'b' | 'n' : undefined;
    this.chess.move({ from, to, promotion });
    this.lastMove = [from as Key, to as Key];
  }

  private updateBoard(): void {
    this.boardFen = this.chess.fen();
    this.turnColor = this.chess.turn() === 'w' ? 'white' : 'black';
    this.isCheck = this.chess.isCheck();
    const interactive = this.state === 'AWAITING_USER_MOVE' || this.state === 'REFUTATION_USER';
    this.dests = interactive ? this.calcDests() : new Map();
  }

  private calcDests(): Map<Key, Key[]> {
    const dests = new Map<Key, Key[]>();
    for (const m of this.chess.moves({ verbose: true })) {
      const from = m.from as Key;
      if (!dests.has(from)) dests.set(from, []);
      dests.get(from)!.push(m.to as Key);
    }
    return dests;
  }

  // --- Timer ---

  private startSessionTimer(): void {
    this.sessionStart = Date.now();
    this.sessionSeconds = 0;
    this.sessionInterval = setInterval(() => {
      this.sessionSeconds = Math.floor((Date.now() - this.sessionStart) / 1000);
    }, 1000);
  }

  private stopSessionTimer(): void {
    if (this.sessionInterval) {
      clearInterval(this.sessionInterval);
      this.sessionInterval = undefined;
    }
  }

  formatTime(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return m > 0 ? `${m}:${s.toString().padStart(2, '0')}` : `${s}s`;
  }

  // --- Tracking ---

  private trackMaxRating(rating: number): void {
    if (rating > this.maxRatingReached) this.maxRatingReached = rating;
  }

  private recordAttempt(solved: boolean): void {
    if (!this.puzzle || !this.authService.isLoggedIn) return;
    this.puzzleService.recordAttempt(this.puzzle.id, solved, 0).subscribe();
  }

  // --- localStorage ---

  private loadConfig(): void {
    try {
      const raw = localStorage.getItem(CONFIG_KEY);
      if (raw) this.config = { ...this.config, ...JSON.parse(raw) };
    } catch {}
  }

  private saveConfig(): void {
    try { localStorage.setItem(CONFIG_KEY, JSON.stringify(this.config)); } catch {}
  }

  private loadHighscore(): void {
    try {
      const raw = localStorage.getItem(HIGHSCORE_KEY);
      if (raw) this.highscore = parseInt(raw, 10) || 0;
    } catch {}
  }

  private checkHighscore(): void {
    if (this.maxRatingReached > this.highscore) {
      this.highscore = this.maxRatingReached;
      this.isNewHighscore = true;
      try { localStorage.setItem(HIGHSCORE_KEY, String(this.highscore)); } catch {}
    }
  }
}
