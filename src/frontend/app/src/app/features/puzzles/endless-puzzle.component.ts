import { Component, OnDestroy, OnInit, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { SnackbarService } from '../../core/snackbar.service';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { ReviewNavComponent } from './review-nav.component';
import { VizCardComponent } from './viz-card.component';
import { ThemePickerComponent } from './theme-picker.component';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleService, PuzzleDto, PuzzleRatingRange } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { EndlessStorageService, EndlessConfig, EndlessSession } from './endless-storage.service';
import { takeFromPool, takeNearestFromPool, buildEndlessRunWindows, autoFasttrackThresholds, fasttrackSteps, ENDLESS_RATING_WINDOW } from './endless-prefetch.util';
import { OfflineService } from '../../core/offline.service';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { AuthService } from '../../core/auth.service';
import { PreferencesService } from '../../core/preferences.service';
import { BOARD_THEMES, PIECE_SETS, ThemeMode, applyThemeMode, clearCrazyStyles, clearVisualizationHide } from './board-theme.util';
import { applyUci } from './puzzle-move.util';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';

// AWAITING_USER_MOVE = first move only (no buttons)
// THINKING = opponent responding (buttons visible, board locked)
// PLAYING = user's turn after first move (buttons visible, board active)
type EndlessState = 'CONFIG' | 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE'
  | 'THINKING' | 'PLAYING' | 'SOLVED' | 'FAILED' | 'GAME_OVER' | 'EXHAUSTED';

interface EndlessPuzzleAttempt {
  puzzleNumber: number;
  puzzleId: number;
  lichessId: string;
  rating: number;
  solved: boolean;
  themes?: string;
  /** Start-/Endzeit dieses Puzzles als Unix-Millis (fürs serverseitige Logging). */
  startedAt: number;
  endedAt: number;
}

@Component({
  selector: 'app-endless-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatProgressSpinnerModule, MatSlideToggleModule,
    MatDialogModule, TranslateModule, PuzzleBoardComponent, ReviewNavComponent, VizCardComponent, ThemePickerComponent
  ],
  templateUrl: './endless-puzzle.component.html',
  styleUrls: ['./endless-puzzle.component.scss'],
})
export class EndlessPuzzleComponent extends BasePuzzleSolver implements OnDestroy, OnInit {
  get screen(): 'config' | 'play' | 'gameover' | 'exhausted' {
    if (this.state === 'EXHAUSTED') return 'exhausted';
    if (this.state === 'GAME_OVER') return 'gameover';
    if (this.state === 'CONFIG') return 'config';
    return 'play';
  }

  config: EndlessConfig = { startElo: 700, themes: '', stockfishDepth: 16 };

  lives = 3;
  level = 0;
  solved = 0;
  maxRatingReached = 0;
  isNewHighscore = false;
  highscore = 0;

  // Board theme
  boardTheme = 'brown';
  readonly boardThemes = BOARD_THEMES;

  pieceSet = 'cburnett';
  showSettings = false;
  themeMode: ThemeMode = 'fixed';
  readonly pieceSets = PIECE_SETS;

  // Help
  showHelp = false;

  // Eval
  showEval = false;
  evalLoading = false;
  initialEval = '';

  // Session timer
  sessionSeconds = 0;
  private sessionInterval?: ReturnType<typeof setInterval>;
  private sessionStart = 0;

  // Session history
  sessionHistory: EndlessSession[] = [];
  currentSessionMistakes: number[] = [];
  currentSessionPuzzles: EndlessPuzzleAttempt[] = [];
  showThemes = false;

  // Fasttrack
  fasttrackPhase1Step = 0;
  fasttrackPhase2Step = 0;
  fasttrackAvgFirst = 0;
  fasttrackAvgSecond = 0;
  fasttrackAutoFirst = 0;
  fasttrackAutoSecond = 0;

  // Dynamic rating
  _currentMinRating = 0;

  // Resume
  activeGameState: any = null;

  // Archive
  lastSessionId: number | null = null;
  lastSessionArchived = false;
  archiving = false;

  // Puzzle DB range
  puzzleRange: PuzzleRatingRange = { min: 100, max: 3000 };

  // Board (Brett/Viz/Lös-State in BasePuzzleSolver)
  puzzle: PuzzleDto | null = null;
  private initialFen = '';
  private prefetchedPuzzle: PuzzleDto | null = null;
  /** Vorab geladene Puzzles für Offline-Spiel (ein ganzer Run). */
  private offlinePool: PuzzleDto[] = [];
  reviewingWrongPuzzle = false;
  gaveUp = false;
  private puzzleStartTime = 0;

  // Zuletzt gelöstes Puzzle — für „Letztes Puzzle analysieren" (bleibt auch nach dem
  // Auto-Advance auf das nächste Puzzle erhalten, da der SOLVED-Status nur kurz sichtbar ist).
  lastSolvedPuzzleId: number | null = null;
  private lastSolvedFen: string | null = null;
  private lastSolvedMoves = '';
  private lastSolvedOrientation: 'white' | 'black' = 'white';

  constructor(
    private puzzleService: PuzzleService,
    stockfish: StockfishService,
    private storage: EndlessStorageService,
    public authService: AuthService,
    private prefs: PreferencesService,
    public router: Router,
    private route: ActivatedRoute,
    private dialog: MatDialog,
    private translate: TranslateService,
    private offline: OfflineService,
    private snackbar: SnackbarService,
    private offlineQueue: OfflineQueueService
  ) {
    super(stockfish);
    this.state = 'CONFIG';
    // Load board theme from preferences service
    this.boardTheme = this.prefs.boardTheme;
    this.pieceSet = this.prefs.pieceSet;
    this.themeMode = this.prefs.themeMode;
    this.visualizationMode = this.prefs.visualization;
    // 1. Load from localStorage immediately (no latency)
    this.config = this.storage.loadConfig(this.config);
    this.highscore = this.storage.loadHighscore();
    this.sessionHistory = this.storage.loadSessionHistory();
    this.offlinePool = this.storage.loadOfflinePool();   // evtl. vorhandener Run-Cache (Offline/Resume)
    this.computeFasttrackSteps();

    // Load local active game state for immediate display
    const localGame = this.storage.loadActiveGameLocal();
    if (localGame) this.activeGameState = localGame;
    // Defensiv: ein resumebarer Run mit 0 Lives existiert nicht — entweder
    // Zombie-State aus aelterer Logik oder Race vor endGame(). Aufraeumen.
    if (this.activeGameState && this.activeGameState.lives <= 0) {
      this.activeGameState = null;
      this.storage.saveActiveGameLocal(null);
    }

    // 2. Load from server (async) and merge
    this.storage.loadFromServer().subscribe(serverData => {
      if (serverData) {
        if (serverData.progress || serverData.sessions.length > 0) {
          const merged = this.storage.mergeServerData(
            this.config, this.highscore, this.sessionHistory, serverData
          );
          this.config = this.storage.loadConfig(merged.config);
          this.highscore = merged.highscore;
          this.sessionHistory = merged.history;
          this.computeFasttrackSteps();

          // Server active game state takes priority
          if (serverData.progress?.activeGameState) {
            try { this.activeGameState = JSON.parse(serverData.progress.activeGameState); } catch {}
          }
          // Auch Server-State auf 0-Lives-Zombie pruefen (Legacy aus aelteren Builds).
          if (this.activeGameState && this.activeGameState.lives <= 0) {
            this.activeGameState = null;
            this.storage.saveActiveGameLocal(null);
            this.storage.saveProgressImmediate(this.config, this.highscore, null);
          }
        } else {
          // Server empty: migrate localStorage data up (one-time)
          this.storage.migrateLocalToServer(this.config, this.highscore, this.sessionHistory);
        }
      }
    });

    this.puzzleService.getRatingRange().subscribe({
      next: r => {
        this.puzzleRange = r;
        this.clampConfig();
        // Schon beim Öffnen der Config einen Run vorab laden, damit Endless später
        // auch offline gestartet werden kann (nur online + wenn noch kein Cache da ist).
        if (navigator.onLine && this.offlinePool.length === 0) this.prefetchRun();
      },
      error: () => {}
    });
    this.stockfish.init().catch(() => {});
  }

  ngOnInit(): void {
    // Rückkehr aus dem Analysemodus (?resume=1): laufenden Run direkt fortsetzen statt in der
    // Übersicht zu landen. Bei normalem Einstieg (kein resume-Param) bleibt der Resume-Banner,
    // damit man Fortsetzen/Archivieren wählen kann. Der Konstruktor hat 0-Leben-Zombies bereits
    // bereinigt → activeGameState != null bedeutet: noch Leben übrig.
    if (this.route.snapshot.queryParamMap.get('resume') === '1' && this.activeGameState && this.state === 'CONFIG') {
      this.resumeGame();
    }
  }

  ngOnDestroy(): void {
    this.stopSessionTimer();
    this.abortSolver();
    clearCrazyStyles();
    clearVisualizationHide();
  }

  // --- Config ---

  get currentMinRating(): number { return this._currentMinRating; }
  get currentMaxRating(): number { return this._currentMinRating + ENDLESS_RATING_WINDOW; }
  get currentRating(): number { return this._currentMinRating; }

  get currentPhaseLabel(): string {
    if (this.solved < 5) return this.translate.instant('endless.game.phaseLabel', { phase: 1, step: this.fasttrackPhase1Step });
    if (this.solved < 10) return this.translate.instant('endless.game.phaseLabel', { phase: 2, step: this.fasttrackPhase2Step });
    return this.translate.instant('endless.game.phaseLabel', { phase: 3, step: 20 });
  }

  private clampConfig(): void {
    this.config.startElo = Math.max(this.puzzleRange.min, Math.min(this.puzzleRange.max, this.config.startElo));
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

  setVisualizationLevel(level: number): void {
    this.visualizationMode = level;
    this.prefs.setVisualization(level);
    if (this.puzzle && this.isSolving) this.setupPuzzle(this.puzzle);  // laufendes Puzzle neu starten
  }

  // ===== Hooks für BasePuzzleSolver =====
  protected override get depth(): number { return this.config.stockfishDepth; }

  protected override onSetupStart(): void {
    const applied = applyThemeMode(this.themeMode, this.prefs.boardTheme, this.prefs.pieceSet);
    this.boardTheme = applied.boardTheme;
    this.pieceSet = applied.pieceSet;
  }

  protected override onSolvingBegins(): void {
    this.initialFen = this.chess.fen();
    this.puzzleStartTime = Date.now();
  }

  protected override handleSolved(alternative: boolean): void { this.puzzleSolved(alternative); }
  protected override handleFailed(): void { this.loseLife(); }

  // --- Game lifecycle ---

  sharePuzzle(): void {
    if (!this.puzzle) return;
    const url = `${window.location.origin}/puzzles/${this.puzzle.id}`;
    this.dialog.open(SharePuzzleDialogComponent, { data: { url }, width: '400px' });
  }

  startGame(): void {
    this.clampConfig();
    this.saveConfig();
    this.lives = 3;
    this.level = 0;
    this.solved = 0;
    this._currentMinRating = this.config.startElo;
    this.maxRatingReached = this.config.startElo;
    this.isNewHighscore = false;
    this.prefetchedPuzzle = null;
    this.sessionSeconds = 0;
    this.currentSessionMistakes = [];
    this.currentSessionPuzzles = [];
    this.activeGameState = null;
    this.lastSessionId = null;
    this.lastSessionArchived = false;
    this.computeFasttrackSteps();
    this.startSessionTimer();
    this.syncActiveGameToServer();
    this.prefetchRun();
    this.loadPuzzle();
  }

  /**
   * Lädt im Hintergrund einen ganzen Run an Puzzles vorab und legt ihn im Storage ab,
   * damit auch offline gepuzzelt werden kann. Run-Größe = max(gelöste der letzten 5 Runs) + 10.
   */
  private prefetchRun(): void {
    const runs = Math.max(1, this.offline.endlessRuns);   // konfigurierbar im Profil (Standard 2)
    const windows = buildEndlessRunWindows(this.config, this.sessionHistory, this.puzzleRange.max, runs);
    if (!windows.length) return;
    const themes = this.config.themes.trim() || undefined;
    this.puzzleService.getRandomBatch(windows, themes).subscribe({
      next: pool => { this.offlinePool = pool || []; this.storage.saveOfflinePool(this.offlinePool); },
      error: () => { /* offline/Fehler: bestehenden Pool behalten */ }
    });
  }

  resumeGame(): void {
    if (!this.activeGameState) return;
    const g = this.activeGameState;
    this.lives = g.lives ?? 3;
    this.solved = g.solved ?? 0;
    this.level = g.level ?? 0;
    this._currentMinRating = g.currentMinRating ?? this.config.startElo;
    this.maxRatingReached = g.maxRatingReached ?? this._currentMinRating;
    this.sessionSeconds = g.sessionSeconds ?? 0;
    this.currentSessionMistakes = g.mistakes ?? [];
    this.currentSessionPuzzles = [];
    this.isNewHighscore = false;
    this.prefetchedPuzzle = null;
    this.lastSessionId = null;
    this.lastSessionArchived = false;
    this.computeFasttrackSteps();
    // Resume timer from where it left off
    this.sessionStart = Date.now() - this.sessionSeconds * 1000;
    this.sessionInterval = setInterval(() => {
      this.sessionSeconds = Math.floor((Date.now() - this.sessionStart) / 1000);
    }, 1000);
    this.loadPuzzle();
  }

  archiveAndStartNew(): void {
    if (!this.activeGameState) { this.startGame(); return; }
    const g = this.activeGameState;
    const session: EndlessSession = {
      timestamp: Date.now(),
      config: { ...this.config },
      totalSolved: g.solved ?? 0,
      maxRating: g.maxRatingReached ?? 0,
      durationSeconds: g.sessionSeconds ?? 0,
      mistakeAtRatings: g.mistakes ?? []
    };
    this.storage.recordSessionToServer(session).subscribe(id => {
      if (id && this.authService.isLoggedIn) {
        this.storage.archiveSession(id).subscribe();
      }
      this.activeGameState = null;
      this.storage.saveActiveGameLocal(null);
      this.storage.saveProgressImmediate(this.config, this.highscore, null);
      this.sessionHistory = this.storage.recordSession(this.sessionHistory, session);
      this.startGame();
    });
  }

  archiveLastSession(): void {
    if (!this.lastSessionId || this.archiving) return;
    this.archiving = true;
    this.storage.archiveSession(this.lastSessionId).subscribe(() => {
      this.lastSessionArchived = true;
      this.archiving = false;
    });
  }

  playAgain(): void {
    this.state = 'CONFIG';
    this.lastSessionId = null;
    this.lastSessionArchived = false;
    this.computeFasttrackSteps();
  }

  backToPuzzles(): void { this.router.navigate(['/puzzles']); }

  openPuzzle(id: number): void {
    this.router.navigate(['/puzzles', id]);
  }

  // --- Loading ---

  private loadPuzzle(): void {
    this.state = 'LOADING';
    this.alternativeSolve = false;
    this.showEval = false;
    this.showThemes = false;
    this.initialEval = '';
    this.currentEval = '';

    // Check if we exceeded the max puzzle rating
    if (this._currentMinRating > this.puzzleRange.max) {
      this.stopSessionTimer();
      this.checkHighscore();
      this.recordSession();
      this.storage.saveActiveGameLocal(null);
      this.storage.saveProgressImmediate(this.config, this.highscore, null);
      this.state = 'EXHAUSTED';
      return;
    }

    const min = this._currentMinRating;
    const max = this._currentMinRating + ENDLESS_RATING_WINDOW;

    // Offline: ausschließlich aus dem vorab geladenen Run-Pool bedienen.
    if (!navigator.onLine) {
      const pooled = takeFromPool(this.offlinePool, min, max)
        ?? takeNearestFromPool(this.offlinePool, (min + max) / 2);   // sonst rating-nächstes statt blind shift()
      if (pooled) {
        this.storage.saveOfflinePool(this.offlinePool);
        this.prefetchedPuzzle = null;
        this.onPuzzleLoaded(pooled);
      } else if (this.solved === 0) {
        // Offline gestartet, aber kein Run gecacht → kein „Run beendet", sondern Hinweis + zurück zur Config.
        this.stopSessionTimer();
        this.storage.saveActiveGameLocal(null);
        this.snackbar.info(this.translate.instant('endless.offlineNoCache'), { action: 'common.ok', duration: 5000 });
        this.state = 'CONFIG';
      } else {
        // Mitten im Run offline und Pool leer → Run hier regulär beenden.
        this.stopSessionTimer();
        this.checkHighscore();
        this.recordSession();
        this.storage.saveActiveGameLocal(null);
        this.state = 'EXHAUSTED';
      }
      return;
    }

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
      error: () => this.onPuzzleLoadError()
    });
  }

  private onPuzzleLoadError(): void {
    // No puzzles in this range — skip to next range
    this._currentMinRating += this.getCurrentStep();
    this.level++;
    // Check if we've gone beyond the max
    if (this._currentMinRating > this.puzzleRange.max) {
      this.stopSessionTimer();
      this.checkHighscore();
      this.recordSession();
      this.storage.saveActiveGameLocal(null);
      this.storage.saveProgressImmediate(this.config, this.highscore, null);
      this.state = 'EXHAUSTED';
      return;
    }
    this.loadPuzzle();
  }

  private onPuzzleLoaded(puzzle: PuzzleDto): void {
    this.puzzle = puzzle;
    this.trackMaxRating(puzzle.rating);
    this.setupPuzzle(puzzle);
    this.prefetchNext();
  }

  private prefetchNext(): void {
    const nextSolved = this.solved + 1;
    const nextStep = this.getStepForSolved(nextSolved);
    const min = this._currentMinRating + nextStep;
    const max = min + ENDLESS_RATING_WINDOW;
    const themes = this.config.themes.trim() || undefined;
    this.puzzleService.getRandom(min, max, themes).subscribe({
      next: p => this.prefetchedPuzzle = p,
      error: () => this.prefetchedPuzzle = null
    });
  }

  // --- Puzzle setup ---

  private setupPuzzle(puzzle: PuzzleDto): void {
    this.reviewingWrongPuzzle = false;
    this.gaveUp = false;
    this.reviewMode = false;
    // Lös-Automat (Setup, Zug-Handling, Stockfish, Viz) aus BasePuzzleSolver.
    this.setupSolver(puzzle.fen, puzzle.moves, 0);
  }

  private puzzleSolved(alternative: boolean): void {
    this.alternativeSolve = alternative;
    this.state = 'SOLVED';
    this.solved++;
    if (this.puzzle) {
      this.pushSessionPuzzle(true);
      // Für „Letztes Puzzle analysieren" merken (überlebt den Auto-Advance).
      this.lastSolvedPuzzleId = this.puzzle.id;
      this.lastSolvedFen = this.puzzle.fen;
      this.lastSolvedMoves = this.puzzle.moves;
      this.lastSolvedOrientation = this.orientation;
    }
    this.recordAttempt(true);
    this.syncActiveGameToServer();
    this.updateBoard();
    this.enterSolutionReview();

    if (alternative) {
      // Don't auto-advance — let user choose Continue or Show Solution
      return;
    }

    this.autoAdvanceTimer = setTimeout(() => {
      this._currentMinRating += this.getCurrentStep();
      this.level++;
      this.loadPuzzle();
    }, 800);
  }

  continueAfterSolve(): void {
    this.reviewMode = false;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);  // pending Auto-Advance verwerfen
    this._currentMinRating += this.getCurrentStep();
    this.level++;
    this.loadPuzzle();
  }

  continueAfterWrong(): void {
    this.reviewMode = false;
    this.reviewingWrongPuzzle = false;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    if (this.lives <= 0) {
      this.endGame();
    } else {
      this.prefetchedPuzzle = null;
      this.loadPuzzle();
    }
  }

  /** Aktuelles Puzzle (z.B. nach dem Aufgeben) im Analysemodus öffnen. */
  analyzeCurrentPuzzle(): void {
    if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; }
    if (!this.puzzle) return;
    this.router.navigate(['/analysis'], {
      queryParams: {
        fen: this.puzzle.fen,
        moves: this.puzzle.moves.split(' ').filter(m => m).join(','),
        orientation: this.orientation,
        from: '/puzzles/endless?resume=1',   // Rückkehr setzt den laufenden Run fort (siehe ngOnInit)
      },
    });
  }

  /** Zuletzt gelöstes Puzzle im Analysemodus öffnen (auch nach dem Auto-Advance verfügbar). */
  reviewLastPuzzle(): void {
    if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; }
    if (!this.lastSolvedFen) return;
    this.router.navigate(['/analysis'], {
      queryParams: {
        fen: this.lastSolvedFen,
        moves: this.lastSolvedMoves.split(' ').filter(m => m).join(','),
        orientation: this.lastSolvedOrientation,
        from: '/puzzles/endless?resume=1',   // Rückkehr setzt den laufenden Run fort (siehe ngOnInit)
      },
    });
  }

  showIntendedSolution(): void {
    if (!this.puzzle) return;
    if (this.state === 'FAILED') this.reviewingWrongPuzzle = true;
    this.state = 'SOLVED';
    this.reviewMode = true;
    this.reviewGoTo(0);
  }

  override get reviewTotal(): number {
    return this.puzzle ? this.puzzle.moves.split(' ').filter(m => m).length : 0;
  }

  reviewNext(): void { if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; } this.reviewGoTo(this.reviewIndex + 1); }
  reviewPrev(): void { if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; } this.reviewGoTo(this.reviewIndex - 1); }

  protected override reviewGoTo(index: number): void {
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
  }

  /** Zug aufs Brett anwenden ohne lastMove-Highlight (Review-Aufbau). */
  private applyUci(uci: string): void {
    applyUci(this.chess, uci);
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    if (this.state !== 'SOLVED' && this.state !== 'FAILED') return;
    if (e.key === 'ArrowLeft') this.reviewPrev();
    if (e.key === 'ArrowRight') this.reviewNext();
  }

  private loseLife(): void {
    this.currentSessionMistakes.push(this._currentMinRating);
    this.pushSessionPuzzle(false);
    this.lives--;
    this.recordAttempt(false);
    // Bei 0 Lives ist der Run faktisch vorbei — nicht den Zombie-State (0 Lives) auf den
    // Server schreiben. endGame() raeumt nach Klick auf Continue endgueltig auf; falls der
    // User vorher die Seite verlaesst, ist dann kein 0-Lives-Run als "unfinished" gemerkt.
    if (this.lives > 0) {
      this.syncActiveGameToServer();
    } else {
      this.storage.saveActiveGameLocal(null);
      this.storage.saveProgressImmediate(this.config, this.highscore, null);
    }
    this.state = 'FAILED';
    this.updateBoard();
    this.enterSolutionReview();
  }

  // --- Buttons ---

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
        this.initialEval = await this.stockfish.getEval(this.initialFen, this.config.stockfishDepth);
      }
      this.currentEval = await this.stockfish.getEval(this.chess.fen(), this.config.stockfishDepth);
    } catch {}
    this.evalLoading = false;
  }

  onDepthChange(): void {
    if (this.config.stockfishDepth < 1) this.config.stockfishDepth = 1;
    if (this.config.stockfishDepth > 24) this.config.stockfishDepth = 24;
    this.saveConfig();
  }

  resetPuzzle(): void {
    if (!this.puzzle) return;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.currentEval = '';
    this.initialEval = '';
    this.showEval = false;
    // Reset costs a life
    this.lives--;
    if (this.lives <= 0) {
      this.currentSessionMistakes.push(this._currentMinRating);
      this.pushSessionPuzzle(false);
      this.recordAttempt(false);
      // 0 Lives = Run ist vorbei. Active-State (mit jetzt veralteten Werten) auf
      // dem Server loeschen, damit kein "Unfinished run | 0 lives"-Zombie zurueckbleibt.
      this.storage.saveActiveGameLocal(null);
      this.storage.saveProgressImmediate(this.config, this.highscore, null);
      this.state = 'FAILED';
      this.updateBoard();
      this.enterSolutionReview();
      return;
    }
    this.setupPuzzle(this.puzzle);
  }

  giveUp(): void {
    this.abortSolver();
    this.gaveUp = true;
    this.loseLife();
    // Wie Standard/Buch: die Lösung nach dem Aufgeben automatisch von vorne durchspielen.
    this.playSolutionFromStart();
  }

  /** Dasselbe Puzzle erneut versuchen. Kostet KEIN (weiteres) Leben — der Fehlversuch hat
   *  bereits eines gekostet. Nur sinnvoll, solange noch Leben übrig sind. */
  retry(): void {
    if (!this.puzzle || this.lives <= 0) return;
    this.currentEval = '';
    this.initialEval = '';
    this.showEval = false;
    this.setupPuzzle(this.puzzle);
  }

  private endGame(): void {
    this.stopSessionTimer();
    this.checkHighscore();
    this.recordSession();
    // Clear active game and sync final state to server
    this.storage.saveActiveGameLocal(null);
    this.storage.saveProgressImmediate(this.config, this.highscore, null);
    this.state = 'GAME_OVER';
  }

  // --- Step calculation ---

  private getCurrentStep(): number {
    return this.getStepForSolved(this.solved);
  }

  private getStepForSolved(solvedCount: number): number {
    if (solvedCount <= 5) return this.fasttrackPhase1Step;
    if (solvedCount <= 10) return this.fasttrackPhase2Step;
    return 20;
  }

  private computeFasttrackSteps(): void {
    const auto = autoFasttrackThresholds(this.config, this.sessionHistory);
    this.fasttrackAutoFirst = auto.first;
    this.fasttrackAutoSecond = auto.second;
    // Manuelle Overrides aus der Config, sonst Auto-Werte
    this.fasttrackAvgFirst = this.config.fasttrackThreshold1 ?? this.fasttrackAutoFirst;
    this.fasttrackAvgSecond = this.config.fasttrackThreshold2 ?? this.fasttrackAutoSecond;
    this.recalcStepsFromThresholds();
  }

  onThresholdChange(): void {
    // Persist manual overrides
    this.config.fasttrackThreshold1 = this.fasttrackAvgFirst !== this.fasttrackAutoFirst
      ? this.fasttrackAvgFirst : undefined;
    this.config.fasttrackThreshold2 = this.fasttrackAvgSecond !== this.fasttrackAutoSecond
      ? this.fasttrackAvgSecond : undefined;
    this.recalcStepsFromThresholds();
  }

  resetThreshold(which: number): void {
    if (which === 1) {
      this.fasttrackAvgFirst = this.fasttrackAutoFirst;
      this.config.fasttrackThreshold1 = undefined;
    } else {
      this.fasttrackAvgSecond = this.fasttrackAutoSecond;
      this.config.fasttrackThreshold2 = undefined;
    }
    this.recalcStepsFromThresholds();
  }

  private recalcStepsFromThresholds(): void {
    const steps = fasttrackSteps(this.config.startElo, this.fasttrackAvgFirst, this.fasttrackAvgSecond);
    this.fasttrackPhase1Step = steps.phase1Step;
    this.fasttrackPhase2Step = steps.phase2Step;
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
    if (!this.puzzle) return;
    const timeSpent = this.puzzleStartTime > 0 ? Math.floor((Date.now() - this.puzzleStartTime) / 1000) : 0;
    const log = this.moveLog.length > 0 ? JSON.stringify(this.moveLog) : undefined;
    const id = this.puzzle.id;
    const loggedIn = this.authService.isLoggedIn;
    const url = loggedIn ? `/api/puzzles/${id}/attempt` : `/api/puzzles/${id}/attempt/anonymous`;
    const body: Record<string, unknown> = {
      solved, timeSpentSeconds: timeSpent, moveLog: log ?? null, visualizationLevel: this.visualizationMode,
      screenWidth: window.innerWidth, screenHeight: window.innerHeight,
    };
    if (!loggedIn) body['sessionId'] = this.puzzleService.ensureSessionId();
    // Offline gelöste Endless-Puzzles nicht verlieren → vormerken (Sync bei Reconnect).
    if (!navigator.onLine) { this.offlineQueue.enqueue('POST', url, body); return; }
    const obs = loggedIn
      ? this.puzzleService.recordAttempt(id, solved, timeSpent, log, this.visualizationMode)
      : this.puzzleService.recordAnonymousAttempt(id, solved, timeSpent, log, this.visualizationMode);
    obs.subscribe({ error: () => this.offlineQueue.enqueue('POST', url, body) });
  }

  // --- localStorage ---

  private saveConfig(): void {
    this.storage.saveConfig(this.config);
    this.storage.saveProgressToServer(this.config, this.highscore, null);
  }

  private checkHighscore(): void {
    const result = this.storage.checkHighscore(this.maxRatingReached, this.highscore);
    this.highscore = result.highscore;
    if (result.isNew) this.isNewHighscore = true;
  }

  private syncActiveGameToServer(): void {
    const gameState = {
      lives: this.lives,
      solved: this.solved,
      level: this.level,
      currentMinRating: this._currentMinRating,
      maxRatingReached: this.maxRatingReached,
      sessionSeconds: this.sessionSeconds,
      mistakes: this.currentSessionMistakes
    };
    this.storage.saveActiveGameLocal(gameState);
    this.storage.saveProgressToServer(this.config, this.highscore, gameState);
  }

  private recordSession(): void {
    const session: EndlessSession = {
      timestamp: Date.now(),
      config: { ...this.config },
      totalSolved: this.solved,
      maxRating: this.maxRatingReached,
      durationSeconds: this.sessionSeconds,
      mistakeAtRatings: [...this.currentSessionMistakes]
    };
    this.sessionHistory = this.storage.recordSession(this.sessionHistory, session);
    // Per-Puzzle-Daten (mit Start-/Lösungszeit) nur an den Server für das Logging mitgeben,
    // nicht in die lokale History (würde localStorage aufblähen).
    this.storage.recordSessionToServer(session, this.currentSessionPuzzles).subscribe(id => {
      if (id) this.lastSessionId = id;
    });
  }

  /** Hängt das aktuelle Puzzle (mit Start-/Endzeit) an die Session-Liste an. */
  private pushSessionPuzzle(solved: boolean): void {
    if (!this.puzzle) return;
    const now = Date.now();
    this.currentSessionPuzzles.push({
      puzzleNumber: this.currentSessionPuzzles.length + 1,
      puzzleId: this.puzzle.id,
      lichessId: this.puzzle.lichessId,
      rating: this.puzzle.rating,
      solved,
      themes: this.puzzle.themes,
      startedAt: this.puzzleStartTime > 0 ? this.puzzleStartTime : now,
      endedAt: now,
    });
  }
}
