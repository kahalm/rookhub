import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatInputModule } from '@angular/material/input';
import { Router } from '@angular/router';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { PuzzleService, PuzzleDto, PuzzleStatsDto } from './puzzle.service';
import { AuthService } from '../../core/auth.service';
import { Chess, Square } from 'chess.js';
import { Color, Key } from 'chessground/types';
import { of } from 'rxjs';

type PuzzleState = 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE' | 'SOLVED' | 'FAILED';

@Component({
  selector: 'app-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatSelectModule, MatFormFieldModule, MatInputModule, MatProgressSpinnerModule,
    MatChipsModule, MatSlideToggleModule, PuzzleBoardComponent
  ],
  template: `
    <div class="puzzle-page">
      <div class="puzzle-layout">
        <div class="board-section">
          <app-puzzle-board
            [fen]="boardFen"
            [orientation]="orientation"
            [turnColor]="turnColor"
            [dests]="dests"
            [lastMove]="lastMove"
            [viewOnly]="state !== 'AWAITING_USER_MOVE'"
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
                    <p class="timer">{{ formatTime(elapsedSeconds) }}</p>
                  </div>
                }
                @case ('SOLVED') {
                  <div class="status-center solved">
                    <mat-icon class="result-icon">check_circle</mat-icon>
                    <p class="status-text">Correct!</p>
                    <p class="timer">{{ formatTime(elapsedSeconds) }}</p>
                    <button mat-raised-button color="primary" (click)="loadNext()">Next Puzzle</button>
                  </div>
                }
                @case ('FAILED') {
                  <div class="status-center failed">
                    <mat-icon class="result-icon">cancel</mat-icon>
                    <p class="status-text">Incorrect</p>
                    <div class="fail-actions">
                      <button mat-button (click)="retry()">Retry</button>
                      <button mat-button (click)="showSolution()">Show Solution</button>
                      <button mat-raised-button color="primary" (click)="loadNext()">Next Puzzle</button>
                    </div>
                  </div>
                }
              }
            </mat-card-content>
          </mat-card>

          @if (puzzle) {
            <mat-card class="info-card">
              <mat-card-content>
                <div class="puzzle-info">
                  <span class="rating-badge">Rating: {{ puzzle.rating }}</span>
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

          @if (stats) {
            <mat-card class="stats-card">
              <mat-card-header>
                <mat-card-title>Your Stats</mat-card-title>
              </mat-card-header>
              <mat-card-content>
                <div class="stats-grid">
                  <div class="stat">
                    <span class="stat-value">{{ stats.solved }}/{{ stats.totalAttempts }}</span>
                    <span class="stat-label">Solved</span>
                  </div>
                  <div class="stat">
                    <span class="stat-value">{{ stats.accuracy }}%</span>
                    <span class="stat-label">Accuracy</span>
                  </div>
                  <div class="stat">
                    <span class="stat-value">{{ stats.currentStreak }}</span>
                    <span class="stat-label">Streak</span>
                  </div>
                  <div class="stat">
                    <span class="stat-value">{{ stats.bestStreak }}</span>
                    <span class="stat-label">Best</span>
                  </div>
                </div>
              </mat-card-content>
            </mat-card>
          }

          <mat-card class="filter-card">
            <mat-card-header>
              <mat-card-title>Filters</mat-card-title>
            </mat-card-header>
            <mat-card-content>
              <div class="filter-row">
                <mat-form-field appearance="outline" class="filter-field">
                  <mat-label>Min Rating</mat-label>
                  <input matInput type="number" [(ngModel)]="minRating" placeholder="600">
                </mat-form-field>
                <mat-form-field appearance="outline" class="filter-field">
                  <mat-label>Max Rating</mat-label>
                  <input matInput type="number" [(ngModel)]="maxRating" placeholder="3000">
                </mat-form-field>
              </div>
              <div class="filter-actions">
                @if (isLoggedIn) {
                  <mat-slide-toggle [(ngModel)]="excludeSolved">Skip solved</mat-slide-toggle>
                }
                <button mat-raised-button color="primary" (click)="loadNext()">Load Puzzle</button>
              </div>
            </mat-card-content>
          </mat-card>

          <button mat-stroked-button color="accent" class="endless-btn" (click)="goEndless()">
            <mat-icon>all_inclusive</mat-icon>
            Endless Mode
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .puzzle-page { padding: 1rem; max-width: 1200px; margin: 0 auto; }
    .puzzle-layout { display: flex; gap: 1.5rem; align-items: flex-start; }
    .board-section { flex: 0 0 auto; width: min(60vw, 560px); min-width: 280px; }
    .info-section { flex: 1; min-width: 250px; display: flex; flex-direction: column; gap: 1rem; }
    .status-card { min-height: 120px; }
    .status-center { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 1rem 0; }
    .status-text { font-size: 1.1em; font-weight: 500; margin: 0; }
    .timer { font-size: 1.5em; font-weight: bold; font-variant-numeric: tabular-nums; margin: 0; }
    .result-icon { font-size: 48px; width: 48px; height: 48px; }
    .solved .result-icon { color: #4caf50; }
    .failed .result-icon { color: #f44336; }
    .fail-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; justify-content: center; }
    .puzzle-info { display: flex; flex-direction: column; gap: 0.5rem; }
    .rating-badge { font-weight: bold; font-size: 1.1em; }
    .themes { display: flex; flex-wrap: wrap; gap: 0.25rem; }
    .theme-chip {
      background: rgba(0,0,0,0.08); border-radius: 12px; padding: 2px 10px;
      font-size: 0.85em; white-space: nowrap;
    }
    .stats-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 0.5rem; text-align: center; }
    .stat-value { font-size: 1.3em; font-weight: bold; display: block; }
    .stat-label { font-size: 0.8em; color: rgba(0,0,0,0.6); }
    .filter-row { display: flex; gap: 0.5rem; }
    .filter-field { flex: 1; }
    .filter-actions { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
    .endless-btn { width: 100%; height: 44px; font-size: 1em; }
    .endless-btn mat-icon { margin-right: 0.25rem; }

    @media (max-width: 768px) {
      .puzzle-layout { flex-direction: column; }
      .board-section { width: 100%; }
      .stats-grid { grid-template-columns: repeat(2, 1fr); }
    }
  `]
})
export class PuzzleComponent implements OnInit, OnDestroy {
  state: PuzzleState = 'LOADING';
  puzzle: PuzzleDto | null = null;
  stats: PuzzleStatsDto | null = null;

  boardFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  orientation: Color = 'white';
  turnColor: Color = 'white';
  dests: Map<Key, Key[]> = new Map();
  lastMove?: [Key, Key];
  isCheck = false;

  minRating?: number;
  maxRating?: number;
  excludeSolved = false;

  elapsedSeconds = 0;
  private timerInterval?: ReturnType<typeof setInterval>;
  private startTime = 0;

  private chess = new Chess();
  private solutionMoves: string[] = [];
  private moveIndex = 0;
  private attemptRecorded = false;
  private nextPuzzle: PuzzleDto | null = null;

  constructor(private puzzleService: PuzzleService, private authService: AuthService, private router: Router) {}

  get isLoggedIn(): boolean { return this.authService.isLoggedIn; }

  goEndless(): void {
    this.router.navigate(['/puzzles/endless']);
  }

  ngOnInit(): void {
    this.loadNext();
    if (this.isLoggedIn) {
      this.puzzleService.getStats().subscribe(s => this.stats = s);
    }
  }

  ngOnDestroy(): void {
    this.stopTimer();
  }

  loadNext(): void {
    this.state = 'LOADING';
    this.attemptRecorded = false;
    this.stopTimer();
    this.elapsedSeconds = 0;

    const source$ = this.nextPuzzle
      ? of(this.nextPuzzle)
      : this.puzzleService.getRandom(this.minRating, this.maxRating, undefined, this.excludeSolved);
    this.nextPuzzle = null;

    source$.subscribe({
        next: puzzle => {
          this.puzzle = puzzle;
          this.setupPuzzle(puzzle);
          this.prefetchNext();
        },
        error: () => {
          this.state = 'LOADING';
          this.puzzle = null;
        }
      });
  }

  private prefetchNext(): void {
    this.puzzleService.getRandom(this.minRating, this.maxRating, undefined, this.excludeSolved)
      .subscribe({ next: p => this.nextPuzzle = p, error: () => {} });
  }

  private setupPuzzle(puzzle: PuzzleDto): void {
    this.solutionMoves = puzzle.moves.split(' ');
    this.moveIndex = 0;
    this.chess = new Chess(puzzle.fen);

    // Determine orientation: user plays OPPOSITE color of the setup move
    const setupMove = this.solutionMoves[0];
    const setupFrom = setupMove.substring(0, 2) as Square;
    const piece = this.chess.get(setupFrom);
    this.orientation = piece?.color === 'w' ? 'black' : 'white';

    this.updateBoard();
    this.state = 'SETUP';

    // Auto-play the setup move after delay
    setTimeout(() => {
      this.playMove(this.solutionMoves[0]);
      this.moveIndex = 1;
      this.state = 'AWAITING_USER_MOVE';
      this.updateBoard();
      this.startTimer();
    }, 600);
  }

  onMoveMade(event: { orig: Key; dest: Key }): void {
    if (this.state !== 'AWAITING_USER_MOVE') return;

    const expectedUci = this.solutionMoves[this.moveIndex];
    const userUci = event.orig + event.dest;

    // Check for promotion
    const expectedFrom = expectedUci.substring(0, 2) as Square;
    const expectedTo = expectedUci.substring(2, 4) as Square;
    const expectedPromo = expectedUci.length > 4 ? expectedUci[4] : undefined;

    if (userUci === expectedUci.substring(0, 4)) {
      // Correct move
      this.playMove(expectedUci);
      this.moveIndex++;

      if (this.moveIndex >= this.solutionMoves.length) {
        // Puzzle solved
        this.state = 'SOLVED';
        this.stopTimer();
        this.updateBoard();
        this.recordAttempt(true);
      } else {
        // Play opponent response
        this.updateBoard();
        setTimeout(() => {
          this.playMove(this.solutionMoves[this.moveIndex]);
          this.moveIndex++;
          this.updateBoard();

          if (this.moveIndex >= this.solutionMoves.length) {
            this.state = 'SOLVED';
            this.stopTimer();
            this.recordAttempt(true);
          } else {
            this.state = 'AWAITING_USER_MOVE';
          }
        }, 400);
      }
    } else {
      // Wrong move - reset to position before user's move
      this.state = 'FAILED';
      this.stopTimer();
      this.updateBoard();
      this.recordAttempt(false);
    }
  }

  retry(): void {
    if (!this.puzzle) return;
    this.setupPuzzle(this.puzzle);
  }

  showSolution(): void {
    if (!this.puzzle) return;

    // Replay from current position
    const remaining = this.solutionMoves.slice(this.moveIndex);
    let i = 0;
    const playNext = () => {
      if (i >= remaining.length) {
        this.state = 'FAILED';
        return;
      }
      this.playMove(remaining[i]);
      i++;
      this.updateBoard();
      if (i < remaining.length) {
        setTimeout(playNext, 600);
      }
    };
    playNext();
  }

  formatTime(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return m > 0 ? `${m}:${s.toString().padStart(2, '0')}` : `${s}s`;
  }

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

    if (this.state === 'AWAITING_USER_MOVE') {
      this.dests = this.calcDests();
    } else {
      this.dests = new Map();
    }
  }

  private calcDests(): Map<Key, Key[]> {
    const dests = new Map<Key, Key[]>();
    const moves = this.chess.moves({ verbose: true });
    for (const m of moves) {
      const from = m.from as Key;
      if (!dests.has(from)) dests.set(from, []);
      dests.get(from)!.push(m.to as Key);
    }
    return dests;
  }

  private startTimer(): void {
    this.startTime = Date.now();
    this.elapsedSeconds = 0;
    this.timerInterval = setInterval(() => {
      this.elapsedSeconds = Math.floor((Date.now() - this.startTime) / 1000);
    }, 1000);
  }

  private stopTimer(): void {
    if (this.timerInterval) {
      clearInterval(this.timerInterval);
      this.timerInterval = undefined;
    }
  }

  private recordAttempt(solved: boolean): void {
    if (!this.puzzle || this.attemptRecorded || !this.isLoggedIn) return;
    this.attemptRecorded = true;
    this.puzzleService.recordAttempt(this.puzzle.id, solved, this.elapsedSeconds).subscribe(() => {
      this.puzzleService.getStats().subscribe(s => this.stats = s);
    });
  }
}
