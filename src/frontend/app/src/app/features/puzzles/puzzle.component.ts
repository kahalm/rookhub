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
import { Router, ActivatedRoute } from '@angular/router';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleService, PuzzleDto, PuzzleStatsDto, PuzzleRatingRange } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { AuthService } from '../../core/auth.service';
import { Chess, Square } from 'chess.js';
import { Color, Key } from 'chessground/types';
import { of } from 'rxjs';

type PuzzleState = 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE' | 'THINKING' | 'PLAYING' | 'SOLVED' | 'FAILED' | 'ERROR';

const PUZZLE_CONFIG_KEY = 'rookhub_puzzle_config';

// Schwierigkeit → Elo-Offset des Fenster-Zentrums; Fenster ±RATING_WINDOW um (Elo + Offset).
const DIFFICULTY_OFFSET: Record<string, number> = {
  sehr_leicht: -600, leicht: -300, normal: 0, schwer: 300, sehr_schwer: 600,
};
const RATING_WINDOW = 100;
const BOARD_THEME_KEY = 'rookhub_board_theme';

@Component({
  selector: 'app-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatSelectModule, MatFormFieldModule, MatInputModule, MatProgressSpinnerModule,
    MatChipsModule, MatSlideToggleModule, MatDialogModule, PuzzleBoardComponent
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
            [viewOnly]="state !== 'AWAITING_USER_MOVE' && state !== 'PLAYING' && state !== 'THINKING'"
            [premovable]="state === 'THINKING'"
            [check]="isCheck"
            [boardTheme]="boardTheme"
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
                @case ('ERROR') {
                  <div class="status-center failed">
                    <mat-icon class="result-icon">error_outline</mat-icon>
                    <p class="status-text">Failed to load puzzle</p>
                    <button mat-raised-button color="primary" (click)="loadNext()">
                      <mat-icon>refresh</mat-icon> Retry
                    </button>
                  </div>
                }
                @case ('SETUP') {
                  <div class="status-center">
                    <p class="status-text">Watch the opponent's move...</p>
                  </div>
                }
                @case ('AWAITING_USER_MOVE') {
                  <div class="status-center">
                    <p class="status-text">Your turn!</p>
                    <p class="timer">{{ formatTime(elapsedSeconds) }}</p>
                    @if (showEval) {
                      <div class="eval-compare">
                        <span class="eval-item"><span class="eval-label">Start</span> <span class="eval-value">{{ initialEval || '...' }}</span></span>
                        <span class="eval-arrow">→</span>
                        <span class="eval-item"><span class="eval-label">Now</span> <span class="eval-value">{{ currentEval || '...' }}</span></span>
                      </div>
                    }
                    <div class="play-actions">
                      <button mat-button (click)="toggleEval()">
                        <mat-icon>analytics</mat-icon>
                        {{ showEval ? 'Hide Eval' : 'Show Eval' }}
                      </button>
                      <button mat-button (click)="resetPuzzle()">
                        <mat-icon>replay</mat-icon>
                        Reset
                      </button>
                      @if (!mouseslipUsed) {
                        <button mat-button (click)="mouseslip()">
                          <mat-icon>mouse</mat-icon>
                          Mouseslip
                        </button>
                      }
                      <button mat-button color="warn" (click)="giveUp()">
                        <mat-icon>flag</mat-icon>
                        Give Up
                      </button>
                    </div>
                  </div>
                }
                @case ('THINKING') {
                  <div class="status-center">
                    <mat-spinner diameter="24"></mat-spinner>
                    <p class="status-text">Stockfish denkt...</p>
                    @if (showEval) {
                      <div class="eval-compare">
                        <span class="eval-item"><span class="eval-label">Start</span> <span class="eval-value">{{ initialEval || '...' }}</span></span>
                        <span class="eval-arrow">→</span>
                        <span class="eval-item"><span class="eval-label">Now</span> <span class="eval-value">{{ currentEval || '...' }}</span></span>
                      </div>
                    }
                    <div class="play-actions">
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
                @case ('PLAYING') {
                  <div class="status-center">
                    <p class="status-text">Your turn!</p>
                    <p class="timer">{{ formatTime(elapsedSeconds) }}</p>
                    @if (showEval) {
                      <div class="eval-compare">
                        @if (evalLoading) {
                          <mat-spinner diameter="16"></mat-spinner>
                        } @else {
                          <span class="eval-item"><span class="eval-label">Start</span> <span class="eval-value">{{ initialEval || '...' }}</span></span>
                          <span class="eval-arrow">→</span>
                          <span class="eval-item"><span class="eval-label">Now</span> <span class="eval-value">{{ currentEval || '...' }}</span></span>
                        }
                      </div>
                    }
                    <div class="play-actions">
                      <button mat-button (click)="toggleEval()">
                        <mat-icon>analytics</mat-icon>
                        {{ showEval ? 'Hide Eval' : 'Show Eval' }}
                      </button>
                      <button mat-button (click)="resetPuzzle()">
                        <mat-icon>replay</mat-icon>
                        Reset
                      </button>
                      @if (!mouseslipUsed) {
                        <button mat-button (click)="mouseslip()">
                          <mat-icon>mouse</mat-icon>
                          Mouseslip
                        </button>
                      }
                      <button mat-button color="warn" (click)="giveUp()">
                        <mat-icon>flag</mat-icon>
                        Give Up
                      </button>
                    </div>
                  </div>
                }
                @case ('SOLVED') {
                  <div class="status-center solved">
                    <mat-icon class="result-icon">check_circle</mat-icon>
                    @if (alternativeSolve) {
                      <p class="status-text">Schachmatt!</p>
                      <p class="alt-hint">Alternative Lösung — das Puzzle hatte eine andere beabsichtigte Zugfolge.</p>
                    } @else {
                      <p class="status-text">Correct!</p>
                    }
                    @if (lastEloChange != null) {
                      <span class="elo-change elo-up">+{{ lastEloChange }}</span>
                    }
                    <p class="timer">{{ formatTime(elapsedSeconds) }}</p>
                    <div class="solved-actions">
                      <button mat-raised-button color="primary" (click)="loadNext()">
                        Next Puzzle @if (solvedCountdown > 0) { ({{ solvedCountdown }}) }
                      </button>
                      <button mat-button (click)="showSolution()">
                        <mat-icon>visibility</mat-icon> Show Solution
                      </button>
                    </div>
                  </div>
                }
                @case ('FAILED') {
                  <div class="status-center failed">
                    <mat-icon class="result-icon">cancel</mat-icon>
                    <p class="status-text">Incorrect</p>
                    @if (lastEloChange != null) {
                      <span class="elo-change elo-down">{{ lastEloChange }}</span>
                    }
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
                  <button mat-icon-button class="share-btn" (click)="sharePuzzle()" title="Puzzle teilen">
                    <mat-icon>share</mat-icon>
                  </button>
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
                    <span class="stat-value">{{ stats.puzzleElo }}</span>
                    <span class="stat-label">Elo</span>
                  </div>
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
                  <mat-label>Schwierigkeit</mat-label>
                  <mat-select [(ngModel)]="difficulty" (ngModelChange)="onDifficultyChange()">
                    <mat-option value="sehr_leicht">Sehr leicht (Elo −600)</mat-option>
                    <mat-option value="leicht">Leicht (Elo −300)</mat-option>
                    <mat-option value="normal">Normal (Elo ±100)</mat-option>
                    <mat-option value="schwer">Schwer (Elo +300)</mat-option>
                    <mat-option value="sehr_schwer">Sehr schwer (Elo +600)</mat-option>
                  </mat-select>
                  <mat-hint>Puzzles rund um deine Elo ({{ stats?.puzzleElo ?? 1500 }})</mat-hint>
                </mat-form-field>
              </div>
              <div class="filter-row">
                <mat-form-field appearance="outline" class="filter-field">
                  <mat-label>Stockfish Depth</mat-label>
                  <input matInput type="number" [(ngModel)]="stockfishDepth" (ngModelChange)="saveConfig()" min="1" max="24" step="1">
                  <mat-hint>1 (schwach) – 24 (stark)</mat-hint>
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

          <mat-card class="theme-card">
            <mat-card-content>
              <div class="theme-label">Board Theme</div>
              <div class="theme-chips">
                @for (t of boardThemes; track t.key) {
                  <div class="theme-chip" [class.active]="boardTheme === t.key" (click)="setBoardTheme(t.key)">
                    <div class="theme-preview">
                      <div class="tp-light" [style.background]="t.light"></div>
                      <div class="tp-dark" [style.background]="t.dark"></div>
                    </div>
                    <span class="theme-name">{{ t.name }}</span>
                  </div>
                }
              </div>
            </mat-card-content>
          </mat-card>

          @if (lastSolvedPuzzleId) {
            <button mat-stroked-button class="review-btn" (click)="reviewLastPuzzle()">
              <mat-icon>history</mat-icon>
              Review last puzzle
            </button>
          }

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
    .play-actions { display: flex; gap: 0.25rem; flex-wrap: wrap; justify-content: center; margin-top: 0.25rem; }
    .eval-compare { display: flex; align-items: center; gap: 0.5rem; font-size: 0.95em; }
    .eval-item { display: flex; align-items: center; gap: 0.25rem; }
    .eval-label { font-size: 0.8em; color: rgba(0,0,0,0.5); }
    .eval-value { font-weight: bold; font-variant-numeric: tabular-nums; }
    .eval-arrow { color: rgba(0,0,0,0.4); }
    .alt-hint { font-size: 0.85em; color: rgba(0,0,0,0.6); margin: 0; text-align: center; }
    .elo-change { font-size: 1.2em; font-weight: bold; }
    .elo-up { color: #4caf50; }
    .elo-down { color: #f44336; }
    .puzzle-info { display: flex; flex-direction: column; gap: 0.5rem; position: relative; }
    .rating-badge { font-weight: bold; font-size: 1.1em; }
    .share-btn { position: absolute; top: -8px; right: -8px; }
    .themes { display: flex; flex-wrap: wrap; gap: 0.25rem; }
    .theme-chip {
      background: rgba(0,0,0,0.08); border-radius: 12px; padding: 2px 10px;
      font-size: 0.85em; white-space: nowrap;
    }
    .stats-grid { display: grid; grid-template-columns: repeat(5, 1fr); gap: 0.5rem; text-align: center; }
    .stat-value { font-size: 1.3em; font-weight: bold; display: block; }
    .stat-label { font-size: 0.8em; color: rgba(0,0,0,0.6); }
    .filter-row { display: flex; gap: 0.5rem; }
    .filter-field { flex: 1; }
    .filter-actions { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
    .solved-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; justify-content: center; }
    .review-btn { width: 100%; height: 40px; font-size: 0.9em; }
    .review-btn mat-icon { margin-right: 0.25rem; font-size: 18px; width: 18px; height: 18px; }
    .endless-btn { width: 100%; height: 44px; font-size: 1em; }
    .endless-btn mat-icon { margin-right: 0.25rem; }
    .theme-label { font-size: 0.85em; color: rgba(0,0,0,0.6); margin-bottom: 0.5rem; }
    .theme-chips { display: flex; gap: 0.5rem; flex-wrap: wrap; }
    .theme-chip {
      display: flex; flex-direction: column; align-items: center; gap: 4px;
      cursor: pointer; padding: 6px; border-radius: 8px; border: 2px solid transparent;
      transition: border-color 0.15s;
    }
    .theme-chip.active { border-color: #1976d2; }
    .theme-chip:hover { background: rgba(0,0,0,0.04); }
    .theme-preview { display: flex; width: 32px; height: 16px; border-radius: 3px; overflow: hidden; }
    .tp-light, .tp-dark { flex: 1; }
    .theme-name { font-size: 0.75em; color: rgba(0,0,0,0.7); }

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
  private ratingRangeBounds: PuzzleRatingRange | null = null;

  boardFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  orientation: Color = 'white';
  turnColor: Color = 'white';
  dests: Map<Key, Key[]> = new Map();
  lastMove?: [Key, Key];
  isCheck = false;

  boardTheme = 'brown';

  difficulty: 'sehr_leicht' | 'leicht' | 'normal' | 'schwer' | 'sehr_schwer' = 'normal';
  excludeSolved = false;
  stockfishDepth = 16;

  elapsedSeconds = 0;
  private timerInterval?: ReturnType<typeof setInterval>;
  private startTime = 0;

  private chess = new Chess();
  private solutionMoves: string[] = [];
  private moveIndex = 0;
  private attemptRecorded = false;
  private nextPuzzle: PuzzleDto | null = null;
  private autoAdvanceTimer?: ReturnType<typeof setTimeout>;
  private aborted = false;
  lastEloChange: number | null = null;
  onSolutionPath = true;
  alternativeSolve = false;
  mouseslipUsed = false;

  // Move tracking
  private moveLog: Array<{i: number, uci: string, exp: string, ms: number, ok: boolean}> = [];
  private moveStartTime = 0;

  // Eval
  showEval = false;
  evalLoading = false;
  initialEval = '';
  currentEval = '';
  private initialFen = '';

  private routePuzzleId: number | null = null;
  lastSolvedPuzzleId: number | null = null;
  solvedCountdown = 0;
  private countdownInterval?: ReturnType<typeof setInterval>;

  constructor(
    private puzzleService: PuzzleService,
    private stockfish: StockfishService,
    private authService: AuthService,
    private router: Router,
    private route: ActivatedRoute,
    private dialog: MatDialog
  ) {
    this.loadConfig();
    this.stockfish.init().catch(() => {});
  }

  readonly boardThemes = [
    { key: 'brown', name: 'Brown', light: '#f0d9b5', dark: '#b58863' },
    { key: 'blue', name: 'Blue', light: '#d4e3ed', dark: '#5882a1' },
    { key: 'green', name: 'Green', light: '#eeeed2', dark: '#769656' },
    { key: 'gray', name: 'Gray', light: '#f0f0f0', dark: '#8a8a8a' },
    { key: 'wood', name: 'Wood', light: '#e6d1a0', dark: '#8b5e3c' },
  ];

  get isLoggedIn(): boolean { return this.authService.isLoggedIn; }

  goEndless(): void {
    this.router.navigate(['/puzzles/endless']);
  }

  sharePuzzle(): void {
    if (!this.puzzle) return;
    const url = `${window.location.origin}/puzzles/${this.puzzle.id}`;
    this.dialog.open(SharePuzzleDialogComponent, { data: { url }, width: '400px' });
  }

  ngOnInit(): void {
    const idParam = this.route.snapshot.paramMap.get('id');
    if (idParam) {
      this.routePuzzleId = Number(idParam);
    }

    const stats$ = this.isLoggedIn
      ? this.puzzleService.getStats()
      : this.puzzleService.getAnonymousStats();

    if (this.routePuzzleId) {
      // Deep-Link auf ein konkretes Puzzle → sofort laden; Stats/Range nebenher.
      this.loadNext();
      stats$.subscribe({ next: s => this.stats = s, error: () => {} });
      this.puzzleService.getRatingRange().subscribe({ next: r => this.ratingRangeBounds = r, error: () => {} });
      return;
    }

    // Sonst erst Elo (stats) + DB-Rating-Bereich laden, DANN das erste Zufallspuzzle –
    // sonst würde es mit Default-Elo 1500 / ungeklemmtem Fenster gezogen.
    const loadFirst = () => {
      this.puzzleService.getRatingRange().subscribe({
        next: r => this.ratingRangeBounds = r,
        error: () => this.loadNext(),
        complete: () => this.loadNext(),
      });
    };
    stats$.subscribe({
      next: s => this.stats = s,
      error: () => loadFirst(),
      complete: () => loadFirst(),
    });
  }

  ngOnDestroy(): void {
    this.stopTimer();
    this.stopCountdown();
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.stockfish.destroy();
  }

  loadNext(): void {
    this.state = 'LOADING';
    this.attemptRecorded = false;
    this.stopTimer();
    this.stopCountdown();
    this.elapsedSeconds = 0;
    this.alternativeSolve = false;
    this.lastEloChange = null;
    this.showEval = false;
    this.initialEval = '';
    this.currentEval = '';

    let source$;
    if (this.routePuzzleId) {
      const id = this.routePuzzleId;
      this.routePuzzleId = null;
      source$ = this.puzzleService.getById(id);
    } else if (this.nextPuzzle) {
      source$ = of(this.nextPuzzle);
      this.nextPuzzle = null;
    } else {
      const r = this.ratingRange();
      source$ = this.puzzleService.getRandom(r.min, r.max, undefined, this.excludeSolved);
    }

    source$.subscribe({
        next: puzzle => {
          this.puzzle = puzzle;
          this.setupPuzzle(puzzle);
          this.prefetchNext();
        },
        error: () => {
          this.state = 'ERROR';
          this.puzzle = null;
        }
      });
  }

  private prefetchNext(): void {
    const r = this.ratingRange();
    this.puzzleService.getRandom(r.min, r.max, undefined, this.excludeSolved)
      .subscribe({ next: p => this.nextPuzzle = p, error: () => {} });
  }

  /** Rating-Fenster aus aktueller Elo + Schwierigkeits-Offset (±RATING_WINDOW). */
  private ratingRange(): { min: number; max: number } {
    const elo = this.stats?.puzzleElo ?? 1500;
    let center = elo + (DIFFICULTY_OFFSET[this.difficulty] ?? 0);
    const b = this.ratingRangeBounds;
    if (b && b.max > b.min) {
      // Zentrum so verschieben, dass das ±Fenster im echten DB-Rating-Bereich bleibt
      // (sonst leeres Ergebnis → 404 → ERROR/Retry-Schleife bei extremen Offsets).
      center = Math.min(Math.max(center, b.min + RATING_WINDOW), b.max - RATING_WINDOW);
    }
    return { min: Math.max(0, center - RATING_WINDOW), max: center + RATING_WINDOW };
  }

  onDifficultyChange(): void {
    this.nextPuzzle = null;  // vorab geladenes Puzzle hatte die alte Schwierigkeit
    this.saveConfig();
  }

  private setupPuzzle(puzzle: PuzzleDto): void {
    this.solutionMoves = puzzle.moves.split(' ');
    this.moveIndex = 0;
    this.chess = new Chess(puzzle.fen);
    this.onSolutionPath = true;
    this.aborted = false;
    this.mouseslipUsed = false;

    const setupMove = this.solutionMoves[0];
    const setupFrom = setupMove.substring(0, 2) as Square;
    const piece = this.chess.get(setupFrom);
    this.orientation = piece?.color === 'w' ? 'black' : 'white';

    this.updateBoard();
    this.state = 'SETUP';
    this.moveLog = [];

    setTimeout(() => {
      if (this.state !== 'SETUP') return;
      this.playMove(this.solutionMoves[0]);
      this.moveIndex = 1;
      this.state = 'AWAITING_USER_MOVE';
      this.initialFen = this.chess.fen();
      this.updateBoard();
      this.startTimer();
      this.moveStartTime = Date.now();
    }, 600);
  }

  onMoveMade(event: { orig: Key; dest: Key; promotion?: string }): void {
    if (this.state === 'PLAYING') {
      this.handleOffPathMove(event);
      return;
    }
    if (this.state !== 'AWAITING_USER_MOVE') return;

    if (this.onSolutionPath) {
      const expectedUci = this.solutionMoves[this.moveIndex];
      const userUci = event.orig + event.dest + (event.promotion || '');
      const thinkMs = Date.now() - this.moveStartTime;

      if (userUci === expectedUci.substring(0, userUci.length)) {
        // Correct move
        this.moveLog.push({ i: this.moveIndex, uci: expectedUci, exp: expectedUci, ms: thinkMs, ok: true });
        this.playMove(expectedUci);
        this.moveIndex++;

        if (this.moveIndex >= this.solutionMoves.length) {
          this.state = 'SOLVED';
          this.stopTimer();
          this.updateBoard();
          this.recordAttempt(true);
          this.lastSolvedPuzzleId = this.puzzle?.id ?? null;
          this.startSolvedCountdown();
        } else {
          // Play opponent response
          this.state = 'THINKING';
          this.updateBoard();
          this.autoAdvanceTimer = setTimeout(() => {
            if (this.aborted) return;
            this.playMove(this.solutionMoves[this.moveIndex]);
            this.moveIndex++;
            this.updateBoard();

            if (this.moveIndex >= this.solutionMoves.length) {
              this.state = 'SOLVED';
              this.stopTimer();
              this.recordAttempt(true);
              this.lastSolvedPuzzleId = this.puzzle?.id ?? null;
              this.startSolvedCountdown();
            } else {
              this.state = 'AWAITING_USER_MOVE';
              this.moveStartTime = Date.now();
              this.updateBoard();
            }
          }, 400);
        }
      } else {
        // Wrong move — leave solution path, play against Stockfish
        this.moveLog.push({ i: this.moveIndex, uci: userUci, exp: expectedUci, ms: thinkMs, ok: false });
        if (!this.playFreeMove(event.orig, event.dest, event.promotion)) return;
        this.onSolutionPath = false;
        if (this.chess.isGameOver()) { this.handleGameOver(); return; }
        this.opponentRespond();
      }
    } else {
      this.handleOffPathMove(event);
    }
  }

  private handleOffPathMove(event: { orig: Key; dest: Key; promotion?: string }): void {
    if (!this.playFreeMove(event.orig, event.dest, event.promotion)) return;
    if (this.chess.isGameOver()) { this.handleGameOver(); return; }
    this.opponentRespond();
  }

  private async opponentRespond(): Promise<void> {
    this.state = 'THINKING';
    this.updateBoard();

    try {
      const result = await this.stockfish.getBestMove(this.chess.fen(), this.stockfishDepth);
      if (this.aborted) return;
      this.currentEval = result.eval;
      this.playMove(result.move);
      this.updateBoard();

      if (this.chess.isGameOver()) {
        this.handleGameOver();
        return;
      }

      this.autoAdvanceTimer = setTimeout(() => {
        if (this.aborted) return;
        this.state = 'PLAYING';
        this.updateBoard();
      }, 400);
    } catch {
      if (!this.aborted) {
        // Stockfish error (e.g. timeout at high depth) — let user continue playing
        this.state = 'PLAYING';
        this.updateBoard();
      }
    }
  }

  private handleGameOver(): void {
    if (this.chess.isCheckmate()) {
      const loserColor = this.chess.turn();
      const userColor = this.orientation === 'white' ? 'w' : 'b';
      if (loserColor !== userColor) {
        // User checkmated Stockfish — alternative solve
        this.alternativeSolve = true;
        this.state = 'SOLVED';
        this.stopTimer();
        this.updateBoard();
        this.recordAttempt(true);
        this.lastSolvedPuzzleId = this.puzzle?.id ?? null;
        this.startSolvedCountdown();
        return;
      }
    }
    // Stockfish checkmated user or draw
    this.state = 'FAILED';
    this.stopTimer();
    this.updateBoard();
    this.recordAttempt(false);
  }

  mouseslip(): void {
    if (this.mouseslipUsed || this.onSolutionPath) return;
    this.mouseslipUsed = true;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    if (this.state === 'PLAYING') {
      this.chess.undo(); // Stockfish response
      this.chess.undo(); // User's wrong move
    } else {
      this.chess.undo();
    }
    this.aborted = false;
    this.state = 'PLAYING';
    this.updateBoard();
  }

  giveUp(): void {
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.state = 'FAILED';
    this.stopTimer();
    this.updateBoard();
    this.recordAttempt(false);
  }

  retry(): void {
    if (!this.puzzle) return;
    this.attemptRecorded = false;
    this.setupPuzzle(this.puzzle);
  }

  showSolution(): void {
    if (!this.puzzle) return;
    this.stopCountdown();

    // Reset to puzzle start and replay full solution
    this.solutionMoves = this.puzzle.moves.split(' ');
    this.chess = new Chess(this.puzzle.fen);
    this.playMove(this.solutionMoves[0]);
    this.updateBoard();

    let i = 1;
    const playNext = () => {
      if (i >= this.solutionMoves.length) return;
      this.playMove(this.solutionMoves[i]);
      i++;
      this.updateBoard();
      if (i < this.solutionMoves.length) {
        this.autoAdvanceTimer = setTimeout(playNext, 600);
      }
    };
    this.autoAdvanceTimer = setTimeout(playNext, 400);
  }

  private startSolvedCountdown(): void {
    this.solvedCountdown = 3;
    this.countdownInterval = setInterval(() => {
      this.solvedCountdown--;
      if (this.solvedCountdown <= 0) {
        this.stopCountdown();
        this.loadNext();
      }
    }, 1000);
  }

  private stopCountdown(): void {
    if (this.countdownInterval) {
      clearInterval(this.countdownInterval);
      this.countdownInterval = undefined;
    }
    this.solvedCountdown = 0;
  }

  reviewLastPuzzle(): void {
    if (this.lastSolvedPuzzleId) {
      this.router.navigate(['/puzzles', this.lastSolvedPuzzleId]);
    }
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

  private playFreeMove(orig: Key, dest: Key, promotion?: string): boolean {
    const from = orig as string as Square;
    const to = dest as string as Square;
    if (promotion) {
      try { this.chess.move({ from, to, promotion: promotion as 'q' | 'r' | 'b' | 'n' }); } catch { return false; }
    } else {
      const moves = this.chess.moves({ verbose: true });
      const match = moves.find(m => m.from === from && m.to === to);
      if (match) {
        this.chess.move(match);
      } else {
        try { this.chess.move({ from, to, promotion: 'q' }); } catch { return false; }
      }
    }
    this.lastMove = [orig, dest];
    return true;
  }

  private updateBoard(): void {
    this.boardFen = this.chess.fen();
    this.turnColor = this.chess.turn() === 'w' ? 'white' : 'black';
    this.isCheck = this.chess.isCheck();
    const interactive = (this.state === 'AWAITING_USER_MOVE' || this.state === 'PLAYING') && this.turnColor === this.orientation;
    this.dests = interactive ? this.calcDests() : new Map();
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
    if (!this.puzzle || this.attemptRecorded) return;
    this.attemptRecorded = true;
    const log = this.moveLog.length > 0 ? JSON.stringify(this.moveLog) : undefined;
    if (this.isLoggedIn) {
      this.puzzleService.recordAttempt(this.puzzle.id, solved, this.elapsedSeconds, log).subscribe(res => {
        if (res.eloChange != null) this.lastEloChange = res.eloChange;
        this.puzzleService.getStats().subscribe(s => this.stats = s);
      });
    } else {
      this.puzzleService.recordAnonymousAttempt(this.puzzle.id, solved, this.elapsedSeconds, log).subscribe(() => {
        this.puzzleService.getAnonymousStats().subscribe(s => this.stats = s);
      });
    }
  }

  toggleEval(): void {
    this.showEval = !this.showEval;
    if (this.showEval && (this.state === 'PLAYING' || this.state === 'AWAITING_USER_MOVE')) {
      this.refreshEval();
    }
  }

  private async refreshEval(): Promise<void> {
    this.evalLoading = true;
    try {
      if (!this.initialEval && this.initialFen) {
        this.initialEval = await this.stockfish.getEval(this.initialFen, this.stockfishDepth);
      }
      this.currentEval = await this.stockfish.getEval(this.chess.fen(), this.stockfishDepth);
    } catch {}
    this.evalLoading = false;
  }

  resetPuzzle(): void {
    if (!this.puzzle) return;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.currentEval = '';
    this.initialEval = '';
    this.showEval = false;
    this.setupPuzzle(this.puzzle);
  }

  // --- Config persistence ---

  private loadConfig(): void {
    try {
      const raw = localStorage.getItem(PUZZLE_CONFIG_KEY);
      if (raw) {
        const saved = JSON.parse(raw);
        if (saved.stockfishDepth) this.stockfishDepth = saved.stockfishDepth;
        if (saved.difficulty && saved.difficulty in DIFFICULTY_OFFSET) this.difficulty = saved.difficulty;
      }
    } catch {}
    if (this.stockfishDepth < 1) this.stockfishDepth = 16;
    if (this.stockfishDepth > 24) this.stockfishDepth = 24;
    try {
      this.boardTheme = localStorage.getItem(BOARD_THEME_KEY) || 'brown';
    } catch {}
  }

  saveConfig(): void {
    try {
      localStorage.setItem(PUZZLE_CONFIG_KEY, JSON.stringify({ stockfishDepth: this.stockfishDepth, difficulty: this.difficulty }));
    } catch {}
  }

  setBoardTheme(theme: string): void {
    this.boardTheme = theme;
    try { localStorage.setItem(BOARD_THEME_KEY, theme); } catch {}
  }
}
