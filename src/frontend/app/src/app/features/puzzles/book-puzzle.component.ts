import { Component, OnInit, OnDestroy, ViewChild, ElementRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatChipsModule } from '@angular/material/chips';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, Router } from '@angular/router';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleService, BookPuzzleDto } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { PreferencesService } from '../../core/preferences.service';
import { BOARD_THEMES, PIECE_SETS, ThemeMode, applyThemeMode, clearCrazyStyles, clearVisualizationHide } from './board-theme.util';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { applyUci } from './puzzle-move.util';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { CourseService, CourseMode } from '../courses/course.service';

type BookPuzzleState = 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE' | 'THINKING' | 'PLAYING' | 'SOLVED' | 'FAILED' | 'COURSE_DONE';

@Component({
  selector: 'app-book-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatProgressSpinnerModule, MatProgressBarModule, MatChipsModule, MatInputModule, MatFormFieldModule,
    MatTooltipModule, MatDialogModule, PuzzleBoardComponent
  ],
  template: `
    <div class="puzzle-page">
      <div class="puzzle-layout">
        <div class="board-section" [class.viz-hidden]="vizPiecesHidden && !vizShowPressed">
          <app-puzzle-board
            [fen]="boardFen"
            [actualFen]="actualFen"
            [orientation]="orientation"
            [turnColor]="turnColor"
            [dests]="dests"
            [lastMove]="lastMove"
            [viewOnly]="reviewMode || (state !== 'AWAITING_USER_MOVE' && state !== 'PLAYING' && state !== 'THINKING')"
            [premovable]="state === 'THINKING'"
            [check]="isCheck"
            [boardTheme]="boardTheme"
            [pieceSet]="pieceSet"
            [visualization]="(state !== 'SOLVED' && state !== 'FAILED') ? visualizationMode : 0"
            (moveMade)="onMoveMade($event)"
          />
        </div>

        <div class="info-section">
          @if (inCourse) {
            <mat-card class="course-card">
              <mat-card-content>
                <div class="course-head">
                  <button mat-icon-button (click)="backToCourses()" matTooltip="Zur Kursübersicht">
                    <mat-icon>arrow_back</mat-icon>
                  </button>
                  <span class="course-mode-chip">{{ courseModeKind === 'random' ? 'Zufällig' : 'Sequenziell' }}</span>
                  <span class="course-progress">{{ courseSolved }}/{{ courseTotal }} ({{ coursePercent }}%)</span>
                </div>
                <mat-progress-bar mode="determinate" [value]="coursePercent"></mat-progress-bar>
                @if (courseCompleted) {
                  <p class="course-done"><mat-icon>emoji_events</mat-icon> Kurs abgeschlossen!</p>
                  <div class="course-actions">
                    <button mat-raised-button color="primary" (click)="backToCourses()">Zur Übersicht</button>
                  </div>
                } @else if (state === 'SOLVED') {
                  <div class="course-actions">
                    <button mat-raised-button color="primary" (click)="courseNext()">
                      <mat-icon>skip_next</mat-icon> Nächstes Puzzle
                    </button>
                  </div>
                } @else if (state === 'FAILED') {
                  <div class="course-actions">
                    <button mat-button (click)="retry()"><mat-icon>replay</mat-icon> Nochmal</button>
                    <button mat-stroked-button (click)="courseNext()"><mat-icon>skip_next</mat-icon> Überspringen</button>
                  </div>
                } @else {
                  <div class="course-actions">
                    <button mat-stroked-button (click)="courseNext()"><mat-icon>skip_next</mat-icon> Überspringen</button>
                  </div>
                }
              </mat-card-content>
            </mat-card>
          }
          @if (visualizationMode && !reviewMode && state !== 'LOADING' && state !== 'SETUP') {
            <mat-card class="viz-card">
              <mat-card-content>
                <div class="viz-title"><mat-icon>visibility_off</mat-icon> Visualisierung (Level {{ visualizationMode }})</div>
                @if (vizCountdownSeconds > 0) {
                  <div class="viz-countdown">Figuren verschwinden in {{ vizCountdownSeconds }}s...</div>
                }
                <div class="viz-moves">{{ vizMoveText || 'Noch kein Zug — klick Von-Feld → Ziel-Feld.' }}</div>
                @if (vizPiecesHidden) {
                  <button class="viz-show-btn" (click)="onVizShow()">
                    {{ vizShowPressed ? 'Showing...' : 'Show' }}
                  </button>
                }
                <div class="viz-hint">{{ vizLevelDescription }}</div>
              </mat-card-content>
            </mat-card>
          }
          <mat-card class="status-card">
            <mat-card-content>
              <button mat-icon-button class="settings-gear" [class.active]="showSettings" (click)="toggleSettings()" title="Einstellungen">
                <mat-icon>settings</mat-icon>
              </button>
              @if (reviewMode && !solutionReview) {
                <div class="status-center">
                  <p class="status-text">Ganze Partie</p>
                  <div class="review-nav">
                    <button mat-icon-button (click)="reviewPrev()" [disabled]="reviewIndex === 0"><mat-icon>chevron_left</mat-icon></button>
                    <span class="review-counter">{{ reviewIndex }} / {{ reviewTotal }}</span>
                    <button mat-icon-button (click)="reviewNext()" [disabled]="reviewIndex >= reviewTotal"><mat-icon>chevron_right</mat-icon></button>
                  </div>
                  <button mat-button (click)="exitReview()"><mat-icon>close</mat-icon> Zurück zum Puzzle</button>
                </div>
              } @else {
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
                    <p class="status-text">{{ gaveUp ? 'Aufgegeben — spiel die Lösung selbst durch.' : 'Your turn! Find the best move.' }}</p>
                    <p class="timer">{{ formatTime(elapsedSeconds) }}</p>
                  </div>
                }
                @case ('THINKING') {
                  <div class="status-center">
                    <mat-spinner diameter="24"></mat-spinner>
                    <p class="status-text">Stockfish denkt...</p>
                    <div class="play-actions">
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
                    <p class="status-text">Dein Zug gegen Stockfish...</p>
                    <div class="play-actions">
                      <button mat-button (click)="resetPuzzle()">
                        <mat-icon>replay</mat-icon>
                        Reset
                      </button>
                      @if (!mouseslipUsed && !onSolutionPath) {
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
                      <p class="alt-hint">Alternative Loesung — das Puzzle hatte eine andere beabsichtigte Zugfolge.</p>
                    } @else {
                      <p class="status-text">Correct!</p>
                    }
                    <p class="timer">{{ formatTime(elapsedSeconds) }}</p>
                    <div class="review-nav">
                      <button mat-icon-button (click)="reviewPrev()" [disabled]="reviewIndex === 0"><mat-icon>chevron_left</mat-icon></button>
                      <span class="review-counter">{{ reviewIndex }} / {{ reviewTotal }}</span>
                      <button mat-icon-button (click)="reviewNext()" [disabled]="reviewIndex >= reviewTotal"><mat-icon>chevron_right</mat-icon></button>
                    </div>
                  </div>
                }
                @case ('FAILED') {
                  <div class="status-center failed">
                    <mat-icon class="result-icon">cancel</mat-icon>
                    <p class="status-text">Incorrect</p>
                    <div class="review-nav">
                      <button mat-icon-button (click)="reviewPrev()" [disabled]="reviewIndex === 0"><mat-icon>chevron_left</mat-icon></button>
                      <span class="review-counter">{{ reviewIndex }} / {{ reviewTotal }}</span>
                      <button mat-icon-button (click)="reviewNext()" [disabled]="reviewIndex >= reviewTotal"><mat-icon>chevron_right</mat-icon></button>
                    </div>
                    <div class="fail-actions">
                      <button mat-button (click)="retry()">Retry</button>
                    </div>
                  </div>
                }
              }
              }
            </mat-card-content>
          </mat-card>

          @if (puzzle) {
            <mat-card class="info-card">
              <mat-card-content>
                <div class="puzzle-meta">
                  @if (puzzle.title) {
                    <p class="meta-title">{{ puzzle.title }}</p>
                  }
                  @if (puzzle.chapter) {
                    <p class="meta-chapter">{{ puzzle.chapter }}</p>
                  }
                  @if (puzzle.comment) {
                    <p class="meta-comment">{{ puzzle.comment }}</p>
                  }
                  <div class="meta-row">
                    <span class="book-name">{{ displayBookName }}</span>
                    @if (puzzle.difficulty) {
                      <span class="difficulty-badge" [class]="'diff-' + puzzle.difficulty.toLowerCase()">{{ puzzle.difficulty }}</span>
                    }
                    @if (puzzle.bookRating) {
                      <span class="rating-badge">Schwierigkeit: {{ puzzle.bookRating }}/10</span>
                    }
                  </div>
                  @if (puzzle.tags) {
                    <div class="tags">
                      @for (tag of puzzle.tags.split(' '); track tag) {
                        <span class="tag-chip">{{ tag }}</span>
                      }
                    </div>
                  }
                  @if ((state === 'SOLVED' || state === 'FAILED') && !(reviewMode && !solutionReview)) {
                    <button mat-stroked-button class="full-game-btn" (click)="enterReview()">
                      <mat-icon>history_edu</mat-icon> Ganze Partie ansehen
                    </button>
                  }
                  <button mat-stroked-button class="share-puzzle-btn" (click)="sharePuzzle()">
                    <mat-icon>share</mat-icon> Puzzle teilen
                  </button>
                </div>
              </mat-card-content>
            </mat-card>
          }

          <mat-card class="config-card" #settingsPanel>
            <mat-card-content>
              @if (showSettings) {
              <div class="viz-slider">
                <label>Visualisierung: Level {{ visualizationMode }}</label>
                <input type="range" min="0" max="4" step="1"
                       [value]="visualizationMode"
                       (input)="setVisualizationLevel(+$any($event.target).value)">
                <div class="viz-level-desc">{{ vizLevelDescription }}</div>
              </div>
              <mat-form-field appearance="outline" class="depth-field">
                <mat-label>Stockfish Depth</mat-label>
                <input matInput type="number" [(ngModel)]="stockfishDepth" (ngModelChange)="saveConfig()" min="1" max="24" step="1">
                <mat-hint>1 (schwach) – 24 (stark)</mat-hint>
              </mat-form-field>
              <div class="theme-section">
                <div class="theme-label">Modus</div>
                <div class="theme-chips">
                  <div class="theme-chip" [class.active]="themeMode === 'fixed'" (click)="setThemeMode('fixed')">
                    <mat-icon>palette</mat-icon><span class="theme-name">Normal</span>
                  </div>
                  <div class="theme-chip" [class.active]="themeMode === 'random'" (click)="setThemeMode('random')">
                    <mat-icon>shuffle</mat-icon><span class="theme-name">Random</span>
                  </div>
                  <div class="theme-chip" [class.active]="themeMode === 'crazy'" (click)="setThemeMode('crazy')">
                    <mat-icon>auto_awesome</mat-icon><span class="theme-name">Crazy</span>
                  </div>
                </div>
                @if (themeMode === 'fixed') {
                <div class="theme-label" style="margin-top: 0.75rem;">Board Theme</div>
                <div class="theme-chips">
                  @for (t of boardThemes; track t.key) {
                    <div class="theme-chip" [class.active]="boardTheme === t.key" (click)="setBoardTheme(t.key)">
                      @if (t.img) {
                        <div class="theme-img" [style.backgroundImage]="'url(' + t.img + ')'"></div>
                      } @else {
                        <div class="theme-preview">
                          <div class="tp-light" [style.background]="t.light"></div>
                          <div class="tp-dark" [style.background]="t.dark"></div>
                        </div>
                      }
                      <span class="theme-name">{{ t.name }}</span>
                    </div>
                  }
                </div>
                <div class="theme-label" style="margin-top: 0.75rem;">Figuren</div>
                <div class="theme-chips">
                  @for (p of pieceSets; track p.key) {
                    <div class="theme-chip" [class.active]="pieceSet === p.key" (click)="setPieceSet(p.key)">
                      <div class="piece-preview" [style.backgroundImage]="'url(' + p.preview + ')'"></div>
                      <span class="theme-name">{{ p.name }}</span>
                    </div>
                  }
                </div>
                }
              </div>
              }
            </mat-card-content>
          </mat-card>
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
    .solved-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; justify-content: center; }
    .play-actions { display: flex; gap: 0.25rem; flex-wrap: wrap; justify-content: center; margin-top: 0.25rem; }
    .alt-hint { font-size: 0.85em; color: rgba(0,0,0,0.6); margin: 0; text-align: center; }
    .puzzle-meta { display: flex; flex-direction: column; gap: 0.5rem; }
    .share-puzzle-btn { margin-top: 0.5rem; }
    .meta-title { font-weight: bold; font-size: 1.1em; margin: 0; }
    .meta-chapter { color: rgba(0,0,0,0.7); margin: 0; }
    .meta-comment { font-style: italic; color: rgba(0,0,0,0.6); margin: 0; font-size: 0.9em; }
    .meta-row { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: center; }
    .book-name { font-size: 0.85em; color: rgba(0,0,0,0.5); }
    .difficulty-badge {
      font-size: 0.8em; padding: 2px 8px; border-radius: 12px; font-weight: 500;
    }
    .diff-anfänger, .diff-anfaenger { background: #c8e6c9; color: #2e7d32; }
    .diff-fortgeschritten { background: #fff3e0; color: #e65100; }
    .diff-meister { background: #ffcdd2; color: #c62828; }
    .rating-badge { font-weight: bold; font-size: 0.9em; }
    .tags { display: flex; flex-wrap: wrap; gap: 0.25rem; }
    .tag-chip {
      background: rgba(0,0,0,0.08); border-radius: 12px; padding: 2px 10px;
      font-size: 0.85em; white-space: nowrap;
    }
    .full-game-btn { margin-top: 0.5rem; align-self: flex-start; }
    .review-nav { display: flex; align-items: center; gap: 0.5rem; }
    .review-counter { font-variant-numeric: tabular-nums; min-width: 56px; text-align: center; }
    .viz-slider { margin-bottom: 0.75rem; }
    .viz-slider label { font-size: 0.9em; font-weight: 500; }
    .viz-slider input[type=range] { width: 100%; margin: 0.25rem 0; }
    .viz-level-desc { font-size: 0.8em; color: rgba(0,0,0,0.55); }
    .viz-card {}
    .viz-card .viz-title { display: flex; align-items: center; gap: 0.35rem; font-weight: 600; margin-bottom: 0.4rem; }
    .viz-card .viz-moves {
      font-family: 'Courier New', monospace; font-size: 1.05em; line-height: 1.5;
      background: rgba(0,0,0,0.04); border-radius: 6px; padding: 0.5rem 0.6rem; word-break: break-word;
    }
    .viz-card .viz-hint { font-size: 0.8em; color: rgba(0,0,0,0.55); margin-top: 0.4rem; }
    .viz-countdown { font-size: 0.9em; color: #e65100; font-weight: 500; margin-bottom: 0.25rem; }
    .viz-show-btn {
      margin-top: 0.4rem; padding: 0.35rem 1.2rem; border: 1px solid rgba(0,0,0,0.2);
      border-radius: 6px; background: #fff; cursor: pointer; font-weight: 500;
      user-select: none; touch-action: manipulation;
    }
    .viz-show-btn:active { background: #e3f2fd; }
    .depth-field { width: 100%; }
    .config-card mat-card-content { padding-bottom: 0; }
    .theme-section { margin-top: 0.75rem; }
    .theme-label { font-size: 0.85em; color: rgba(0,0,0,0.6); margin-bottom: 0.5rem; }
    .theme-chips { display: flex; gap: 0.5rem; flex-wrap: wrap; }
    .piece-preview { width: 28px; height: 28px; background-size: contain; background-repeat: no-repeat; background-position: center; }
    .theme-img { width: 32px; height: 16px; border-radius: 3px; background-size: cover; background-position: center; }
    .status-card { position: relative; }
    .settings-gear { position: absolute; top: 4px; right: 4px; z-index: 2; }
    .settings-gear.active { color: #1976d2; }
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

    .course-card .course-head { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.4rem; }
    .course-card .course-mode-chip {
      font-size: 0.75rem; font-weight: 600; padding: 2px 8px; border-radius: 12px;
      background: #e3f2fd; color: #1565c0;
    }
    .course-card .course-progress { margin-left: auto; font-variant-numeric: tabular-nums; font-size: 0.9rem; color: #444; }
    .course-card .course-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; margin-top: 0.6rem; }
    .course-card .course-done { display: flex; align-items: center; gap: 4px; color: #2e7d32; font-weight: 600; margin: 0.5rem 0 0; }
    .course-card .course-done mat-icon { font-size: 20px; width: 20px; height: 20px; }

    @media (max-width: 768px) {
      .puzzle-layout { flex-direction: column; }
      .board-section { width: 100%; }
    }
  `]
})
export class BookPuzzleComponent extends BasePuzzleSolver implements OnInit, OnDestroy {
  // state, boardFen, orientation, turnColor, dests, lastMove, isCheck, chess, solutionMoves,
  // moveIndex, startPly, autoAdvanceTimer, aborted, onSolutionPath, alternativeSolve,
  // mouseslipUsed, visualizationMode, vizMoves → BasePuzzleSolver
  puzzle: BookPuzzleDto | null = null;

  // Kursmodus: Komponente wird über /courses/:bookId/:mode aufgerufen und arbeitet ein
  // Buch puzzleweise durch; Fortschritt liegt user-bezogen in der DB.
  inCourse = false;
  courseBookId: number | null = null;
  courseModeKind: CourseMode = 'sequential';
  courseSolved = 0;
  courseTotal = 0;
  courseCompleted = false;

  stockfishDepth = 16;
  boardTheme = 'brown';
  readonly boardThemes = BOARD_THEMES;

  pieceSet = 'cburnett';
  showSettings = false;
  themeMode: ThemeMode = 'fixed';
  @ViewChild('settingsPanel', { read: ElementRef }) settingsPanel?: ElementRef<HTMLElement>;
  readonly pieceSets = PIECE_SETS;

  elapsedSeconds = 0;
  private timerInterval?: ReturnType<typeof setInterval>;
  private startTime = 0;

  /** True nach Give Up. Status-Panel zeigt einen Hinweis statt "Your turn!". */
  gaveUp = false;

  // Review-Modus „Ganze Partie" / Lösungs-Step-Through (komponentenspezifisch).
  reviewMode = false;
  reviewIndex = 0;
  solutionReview = false;

  get displayBookName(): string {
    if (!this.puzzle) return '';
    return this.puzzle.bookFileName.replace(/_firstkey\.pgn$/, '').replace(/_/g, ' ');
  }

  constructor(
    private puzzleService: PuzzleService,
    stockfish: StockfishService,
    private prefs: PreferencesService,
    private route: ActivatedRoute,
    private dialog: MatDialog,
    private courseService: CourseService,
    private router: Router
  ) {
    super(stockfish);
    this.loadConfig();
    this.stockfish.init().catch(() => {});
  }

  sharePuzzle(): void {
    if (!this.puzzle) return;
    const url = `${window.location.origin}/puzzles/book/${this.puzzle.id}`;
    this.dialog.open(SharePuzzleDialogComponent, { data: { url }, width: '400px' });
  }

  // ===== Hooks für BasePuzzleSolver =====
  protected override get depth(): number { return this.stockfishDepth; }
  protected override get stockfishErrorContinues(): boolean { return false; }  // Buch: Fehlzug → verloren

  protected override onSetupStart(): void {
    const applied = applyThemeMode(this.themeMode, this.prefs.boardTheme, this.prefs.pieceSet);
    this.boardTheme = applied.boardTheme;
    this.pieceSet = applied.pieceSet;
  }

  protected override onSolvingBegins(): void {
    this.startTimer();
  }

  protected override handleSolved(): void {
    this.state = 'SOLVED';
    this.stopTimer();
    this.updateBoard();
    this.enterSolutionReview();
    this.recordCourseSolved();
  }

  protected override handleFailed(): void {
    this.state = 'FAILED';
    this.stopTimer();
    this.updateBoard();
    this.enterSolutionReview();
  }

  ngOnInit(): void {
    const bookIdParam = this.route.snapshot.paramMap.get('bookId');
    const modeParam = this.route.snapshot.paramMap.get('mode');
    if (bookIdParam && modeParam) {
      this.inCourse = true;
      this.courseBookId = Number(bookIdParam);
      this.courseModeKind = modeParam === 'random' ? 'random' : 'sequential';
      this.loadCourseNext();
      return;
    }

    const idParam = this.route.snapshot.paramMap.get('id');
    if (idParam) {
      this.loadPuzzle(Number(idParam));
    }
  }

  ngOnDestroy(): void {
    this.stopTimer();
    this.abortSolver();
    clearCrazyStyles();
    clearVisualizationHide();
  }

  // ===== Kursmodus =====
  get coursePercent(): number {
    return this.courseTotal > 0
      ? Math.round(100 * Math.min(this.courseSolved, this.courseTotal) / this.courseTotal)
      : 0;
  }

  /** Holt das nächste Puzzle des Kurses (sequential: after=, random: exclude=). */
  private loadCourseNext(after?: number, exclude?: number): void {
    if (this.courseBookId == null) return;
    const hadPuzzle = this.puzzle != null;
    if (!hadPuzzle) this.state = 'LOADING';

    this.courseService.getNext(this.courseBookId, this.courseModeKind, after, exclude).subscribe({
      next: res => {
        this.courseSolved = res.solvedCount;
        this.courseTotal = res.total;
        if (res.completed || !res.puzzle) {
          this.courseCompleted = true;
          // Letztes Puzzle gerade gelöst: gelöstes Brett stehen lassen; sonst leeres Done-Panel.
          if (!hadPuzzle) { this.puzzle = null; this.state = 'COURSE_DONE'; }
          return;
        }
        this.courseCompleted = false;
        this.gaveUp = false;
        this.puzzle = res.puzzle;
        this.setupPuzzle(res.puzzle);
      },
      error: () => { this.state = 'LOADING'; }
    });
  }

  /** „Nächstes Puzzle" / „Überspringen": eins weiter im jeweiligen Modus. */
  courseNext(): void {
    const cur = this.puzzle?.id;
    if (this.courseModeKind === 'random') this.loadCourseNext(undefined, cur);
    else this.loadCourseNext(cur, undefined);
  }

  backToCourses(): void {
    this.router.navigate(['/courses']);
  }

  private recordCourseSolved(): void {
    if (!this.inCourse || this.courseBookId == null || !this.puzzle) return;
    this.courseService.recordResult(this.courseBookId, this.puzzle.id, true, this.courseModeKind).subscribe({
      next: p => { this.courseSolved = p.solvedCount; this.courseTotal = p.total; }
    });
  }

  private loadPuzzle(id: number): void {
    this.state = 'LOADING';
    this.stopTimer();
    this.elapsedSeconds = 0;
    this.alternativeSolve = false;
    this.gaveUp = false;

    this.puzzleService.getBookPuzzleById(id).subscribe({
      next: puzzle => {
        this.puzzle = puzzle;
        this.setupPuzzle(puzzle);
      },
      error: () => {
        this.state = 'LOADING';
        this.puzzle = null;
      }
    });
  }

  private setupPuzzle(puzzle: BookPuzzleDto): void {
    this.reviewMode = false;
    this.solutionReview = false;
    // Lös-Automat (Setup, StartPly-Vorspiel, Zug-Handling, Stockfish, Viz) aus BasePuzzleSolver.
    this.setupSolver(puzzle.fen, puzzle.moves, puzzle.startPly ?? 0);
  }

  /** Zug aufs Brett anwenden ohne lastMove-Highlight (Vorspiel/Review-Aufbau). */
  private applyUci(uci: string): void {
    applyUci(this.chess, uci);
  }

  giveUp(): void {
    this.abortSolver();
    // Puzzle zuruecksetzen, sodass der Spieler die Loesung selber durchspielen kann.
    this.gaveUp = true;
    if (this.puzzle) this.setupPuzzle(this.puzzle);
  }

  retry(): void {
    if (!this.puzzle) return;
    this.gaveUp = false;
    this.setupPuzzle(this.puzzle);
  }

  showSolution(): void {
    if (!this.puzzle) return;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.aborted = false;
    this.solutionReview = true;
    this.reviewMode = true;
    this.solutionReviewGoTo(0);
  }

  private solutionReviewGoTo(index: number): void {
    if (!this.puzzle) return;
    const allMoves = this.puzzle.moves.split(' ').filter(m => m);
    const start = Math.max(0, this.startPly);
    const solutionMoves = allMoves.slice(start);
    index = Math.max(0, Math.min(index, solutionMoves.length));
    this.reviewIndex = index;
    this.chess = new Chess(this.puzzle.fen);
    // Vorspiel still aufs Brett
    for (let i = 0; i < start; i++) this.applyUci(allMoves[i]);
    // Loesungszuege bis index
    let last: [Key, Key] | undefined;
    for (let i = 0; i < index; i++) {
      this.applyUci(solutionMoves[i]);
      last = [solutionMoves[i].substring(0, 2) as Key, solutionMoves[i].substring(2, 4) as Key];
    }
    this.lastMove = last;
    this.boardFen = this.chess.fen();
    this.turnColor = this.chess.turn() === 'w' ? 'white' : 'black';
    this.isCheck = this.chess.isCheck();
    this.dests = new Map();
  }

  private enterSolutionReview(): void {
    this.solutionReview = true;
    this.reviewMode = true;
    this.reviewIndex = this.reviewTotal;
  }

  // ---- „Ganze Partie" Review ---------------------------------------------
  /** Zeigt die komplette Partie zum Durchklicken (◀/▶), unabhängig vom Trainingsstart. */
  enterReview(): void {
    if (!this.puzzle) return;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.stopTimer();
    this.solutionReview = false;
    this.reviewMode = true;
    this.reviewGoTo(0);
  }

  get reviewTotal(): number {
    if (!this.puzzle) return 0;
    const allMoves = this.puzzle.moves.split(' ').filter(m => m);
    return this.solutionReview ? allMoves.length - Math.max(0, this.startPly) : allMoves.length;
  }

  reviewNext(): void {
    if (this.solutionReview) this.solutionReviewGoTo(this.reviewIndex + 1);
    else this.reviewGoTo(this.reviewIndex + 1);
  }
  reviewPrev(): void {
    if (this.solutionReview) this.solutionReviewGoTo(this.reviewIndex - 1);
    else this.reviewGoTo(this.reviewIndex - 1);
  }

  private reviewGoTo(index: number): void {
    if (!this.puzzle) return;
    const moves = this.puzzle.moves.split(' ').filter(m => m);
    index = Math.max(0, Math.min(index, moves.length));
    this.reviewIndex = index;
    this.chess = new Chess(this.puzzle.fen);
    let last: [Key, Key] | undefined;
    for (let i = 0; i < index; i++) {
      this.applyUci(moves[i]);
      last = [moves[i].substring(0, 2) as Key, moves[i].substring(2, 4) as Key];
    }
    this.lastMove = last;
    this.boardFen = this.chess.fen();
    this.turnColor = this.chess.turn() === 'w' ? 'white' : 'black';
    this.isCheck = this.chess.isCheck();
    this.dests = new Map();
  }

  exitReview(): void {
    this.reviewMode = false;
    this.solutionReview = false;
    if (this.puzzle) this.setupPuzzle(this.puzzle);
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    if (this.state !== 'SOLVED' && this.state !== 'FAILED' && !this.reviewMode) return;
    if (e.key === 'ArrowLeft') this.reviewPrev();
    if (e.key === 'ArrowRight') this.reviewNext();
  }

  formatTime(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return m > 0 ? `${m}:${s.toString().padStart(2, '0')}` : `${s}s`;
  }

  resetPuzzle(): void {
    if (!this.puzzle) return;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.setupPuzzle(this.puzzle);
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

  private loadConfig(): void {
    this.boardTheme = this.prefs.boardTheme;
    this.pieceSet = this.prefs.pieceSet;
    this.themeMode = this.prefs.themeMode;
    this.stockfishDepth = this.prefs.bookStockfishDepth;
    this.visualizationMode = this.prefs.visualization;
  }

  setVisualizationLevel(level: number): void {
    this.visualizationMode = level;
    this.prefs.setVisualization(level);
    if (this.puzzle) this.setupPuzzle(this.puzzle);  // Modus-Wechsel = Puzzle neu starten
  }

  saveConfig(): void {
    this.prefs.setBookStockfishDepth(this.stockfishDepth);
  }

  setBoardTheme(theme: string): void {
    this.boardTheme = theme;
    this.prefs.setBoardTheme(theme);
  }

  setPieceSet(set: string): void {
    this.pieceSet = set;
    this.prefs.setPieceSet(set);
  }

  setThemeMode(mode: ThemeMode): void {
    this.themeMode = mode;
    this.prefs.setThemeMode(mode);
    const applied = applyThemeMode(mode, this.prefs.boardTheme, this.prefs.pieceSet);
    this.boardTheme = applied.boardTheme;
    this.pieceSet = applied.pieceSet;
  }

  toggleSettings(): void {
    this.showSettings = !this.showSettings;
    if (this.showSettings) {
      setTimeout(() => this.settingsPanel?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' }), 50);
    }
  }
}
