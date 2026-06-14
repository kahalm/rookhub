import { Component, OnInit, OnDestroy, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatMenuModule } from '@angular/material/menu';
import { Router, ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { PuzzleRatingCardComponent } from './puzzle-rating-card.component';
import { PuzzleStatusCardComponent } from './puzzle-status-card.component';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleSettingsDialogComponent, PuzzleSettingsDialogData, PuzzleSettingsDialogResult } from './puzzle-settings-dialog.component';
import { PuzzleService, PuzzleDto, PuzzleStatsDto, PuzzleRatingRange } from './puzzle.service';
import { OfflineService, PUZZLE_POOL_KEY } from '../../core/offline.service';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { DIFFICULTY_OFFSET, puzzleWindow } from './puzzle-window.util';
import { takeFromPool, takeNearestFromPool } from './endless-prefetch.util';
import { StockfishService } from './stockfish.service';
import { AuthService } from '../../core/auth.service';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { ChallengeService } from '../../core/challenge.service';
import { RevengeService } from '../../core/revenge.service';
import { Friend } from '../../core/models';
import { BOARD_THEMES, PIECE_SETS, ThemeMode, applyThemeMode, clearCrazyStyles, clearVisualizationHide } from './board-theme.util';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { applyUci } from './puzzle-move.util';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { of } from 'rxjs';

type PuzzleState = 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE' | 'THINKING' | 'PLAYING' | 'SOLVED' | 'FAILED' | 'ERROR';


@Component({
  selector: 'app-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatProgressSpinnerModule, MatMenuModule, MatDialogModule, TranslateModule, PuzzleBoardComponent,
    PuzzleRatingCardComponent, PuzzleStatusCardComponent
  ],
  templateUrl: './puzzle.component.html',
  styleUrls: ['./puzzle.component.scss'],
})
export class PuzzleComponent extends BasePuzzleSolver implements OnInit, OnDestroy {
  // state, boardFen, orientation, turnColor, dests, lastMove, isCheck, onSolutionPath,
  // alternativeSolve, mouseslipUsed, currentEval, visualizationMode, vizMoves, chess,
  // solutionMoves, moveIndex, autoAdvanceTimer, aborted, moveLog, moveStartTime → BasePuzzleSolver
  puzzle: PuzzleDto | null = null;
  stats: PuzzleStatsDto | null = null;
  private ratingRangeBounds: PuzzleRatingRange | null = null;

  boardTheme = 'brown';

  difficulty: 'sehr_leicht' | 'leicht' | 'normal' | 'schwer' | 'sehr_schwer' = 'normal';
  excludeSolved = false;
  /** „5 schwächste Themen trainieren" — filtert Puzzles auf die schwächsten Themen des Users (ODER). */
  worstTagsEnabled = false;
  private worstThemes: string[] = [];
  stockfishDepth = 16;

  elapsedSeconds = 0;
  private timerInterval?: ReturnType<typeof setInterval>;
  private startTime = 0;

  private attemptRecorded = false;
  private nextPuzzle: PuzzleDto | null = null;
  lastEloChange: number | null = null;

  // Eval
  showEval = false;
  evalLoading = false;
  initialEval = '';
  private initialFen = '';

  private routePuzzleId: number | null = null;

  /** Gesetzt, wenn dieses Puzzle aus einer Freundes-Challenge geöffnet wurde (?challengeId=…). */
  private challengeId: number | null = null;
  private challengeResolved = false;

  /** Gesetzt, wenn dieses Puzzle als Revanche an einem gescheiterten Puzzle dieses Users geöffnet wurde (?revengeUserId=…). */
  private revengeUserId: number | null = null;
  private revengeNotified = false;
  /** Freunde für das „An Freund schicken"-Menü (faul geladen beim Öffnen). */
  challengeFriends: Friend[] = [];

  lastSolvedPuzzleId: number | null = null;
  private lastSolvedFen: string | null = null;
  private lastSolvedMoves = '';
  private lastSolvedOrientation: 'white' | 'black' = 'white';
  /** True wenn der User aufgegeben hat. Brett wird zurueckgesetzt damit er die Loesung
   *  selber durchspielen kann; im AWAITING/PLAYING/THINKING-State zeigt das Status-Panel
   *  einen Hinweis statt "Your turn!". Reset bei loadNext/retry. */
  gaveUp = false;

  constructor(
    private puzzleService: PuzzleService,
    stockfish: StockfishService,
    private authService: AuthService,
    private prefs: PreferencesService,
    private router: Router,
    private route: ActivatedRoute,
    private dialog: MatDialog,
    private offline: OfflineService,
    private offlineQueue: OfflineQueueService,
    private snackbar: SnackbarService,
    private challengeService: ChallengeService,
    private revengeService: RevengeService,
    private translate: TranslateService,
    private http: HttpClient
  ) {
    super(stockfish);
    this.loadConfig();
    this.offlinePuzzlePool = this.loadOfflinePool();
    this.stockfish.init().catch(() => {});
  }

  // ===== Offline-Puzzle-Pool (Standard-Modus) =====
  private offlinePuzzlePool: PuzzleDto[] = [];
  /** „Offline, nichts gespeichert" — Pool war noch nie befüllt (z.B. Seite erstmals offline geöffnet). */
  offlineNoCache = false;
  /** „Offline, alle gespielt" — Pool ist leer geworden, aber wir hatten zuvor mindestens 1 Puzzle gezeigt.
   *  In dem Fall bietet das UI explizit an, das letzte Puzzle nochmal zu spielen — sonst sitzt der Nutzer
   *  vor einer „Verbinden Sie sich erneut"-Wand, obwohl er noch mit dem zuletzt gesehenen Puzzle üben könnte. */
  offlinePoolExhausted = false;
  /** Zuletzt geladenes Puzzle (egal ob gelöst, fehlgeschlagen, oder noch im Spiel). Wird für „Letztes nochmal" verwendet. */
  private lastShownPuzzle: PuzzleDto | null = null;
  /** ID des zuvor angezeigten Puzzles (für „vorheriges Puzzle teilen"). */
  private previousPuzzleId: number | null = null;

  private loadOfflinePool(): PuzzleDto[] {
    try { return JSON.parse(localStorage.getItem(PUZZLE_POOL_KEY) || '[]') || []; } catch { return []; }
  }
  private saveOfflinePool(): void {
    try { localStorage.setItem(PUZZLE_POOL_KEY, JSON.stringify(this.offlinePuzzlePool)); } catch { /* ignore */ }
  }

  /** Lädt im Hintergrund N Puzzles auf der aktuellen Schwierigkeit für Offline-Spiel. */
  private prefetchOfflinePool(): void {
    const n = this.offline.puzzleCount;
    if (n <= 0 || !navigator.onLine || this.offlinePuzzlePool.length >= n) return;   // nur auffüllen
    const r = this.ratingRange();
    const windows = Array.from({ length: n }, () => ({ minRating: r.min, maxRating: r.max }));
    this.puzzleService.getRandomBatch(windows, undefined, this.excludeSolved, this.worstThemesParam).subscribe({
      next: pool => { this.offlinePuzzlePool = pool || []; this.saveOfflinePool(); },
      error: () => { /* offline/Fehler: bestehenden Pool behalten */ }
    });
  }

  // ===== Hooks für BasePuzzleSolver =====
  protected override get depth(): number { return this.stockfishDepth; }

  protected override onSetupStart(): void {
    const applied = applyThemeMode(this.themeMode, this.prefs.boardTheme, this.prefs.pieceSet);
    this.boardTheme = applied.boardTheme;
    this.pieceSet = applied.pieceSet;
  }

  protected override onSolvingBegins(): void {
    this.initialFen = this.chess.fen();
    this.startTimer();
    this.moveStartTime = Date.now();
  }

  protected override handleSolved(): void {
    this.state = 'SOLVED';
    this.stopTimer();
    this.updateBoard();
    this.recordAttempt(true);
    this.lastSolvedPuzzleId = this.puzzle?.id ?? null;
    this.lastSolvedFen = this.puzzle?.fen ?? null;
    this.lastSolvedMoves = this.puzzle?.moves ?? '';
    this.lastSolvedOrientation = this.orientation;
    this.enterSolutionReview();
    this.startSolvedCountdown(() => this.loadNext());
  }

  protected override handleFailed(): void {
    this.state = 'FAILED';
    this.stopTimer();
    this.updateBoard();
    this.recordAttempt(false);
    this.enterSolutionReview();
  }

  readonly boardThemes = BOARD_THEMES;

  pieceSet = 'cburnett';
  themeMode: ThemeMode = 'fixed';
  readonly pieceSets = PIECE_SETS;

  get isLoggedIn(): boolean { return this.authService.isLoggedIn; }

  goEndless(): void {
    this.router.navigate(['/puzzles/endless']);
  }

  sharePuzzle(): void {
    if (!this.puzzle) return;
    const url = `${window.location.origin}/puzzles/${this.puzzle.id}`;
    const previousUrl = this.previousPuzzleId
      ? `${window.location.origin}/puzzles/${this.previousPuzzleId}`
      : undefined;
    this.dialog.open(SharePuzzleDialogComponent, { data: { url, previousUrl }, width: '400px' });
  }

  /** Aktuelle Stellung + komplette Zugfolge des Puzzles im Analysemodus öffnen. */
  analyze(): void {
    if (!this.puzzle) return;
    const moves = this.puzzle.moves.split(' ').filter(m => m);
    // Zurück zur Puzzle-Liste (ohne ID) – mit ID würde dasselbe Puzzle neu geladen statt das nächste.
    this.router.navigate(['/analysis'], {
      queryParams: { fen: this.puzzle.fen, moves: moves.join(','), orientation: this.orientation, from: '/puzzles' },
    });
  }

  ngOnInit(): void {
    // Offen-Zustand der Einstellungen über Puzzle-Wechsel/Re-Init hinweg behalten.
    this.loadSettingsOpen();

    const idParam = this.route.snapshot.paramMap.get('id');
    if (idParam) {
      this.routePuzzleId = Number(idParam);
    }

    // Aus einer Freundes-Challenge geöffnet → nach dem Versuch das Ergebnis zurückmelden.
    const challengeParam = this.route.snapshot.queryParamMap.get('challengeId');
    if (challengeParam) {
      this.challengeId = Number(challengeParam) || null;
    }

    // Als Revanche an einem gescheiterten Puzzle eines Freundes geöffnet → den Freund informieren.
    const revengeParam = this.route.snapshot.queryParamMap.get('revengeUserId');
    if (revengeParam) {
      this.revengeUserId = Number(revengeParam) || null;
    }

    const stats$ = this.isLoggedIn
      ? this.puzzleService.getStats(this.visualizationMode)
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
        // Schwächste Themen vor dem ersten Puzzle laden, damit der Filter schon greift.
        error: () => this.ensureWorstThemes(() => this.loadNext()),
        complete: () => this.ensureWorstThemes(() => this.loadNext()),
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
    this.clearSolutionPlay();
    this.abortSolver();
    clearCrazyStyles();
    clearVisualizationHide();
  }

  loadNext(): void {
    this.state = 'LOADING';
    this.offlineNoCache = false;
    this.offlinePoolExhausted = false;
    this.attemptRecorded = false;
    this.gaveUp = false;
    this.stopTimer();
    this.stopCountdown();
    this.clearSolutionPlay();
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
    } else if (!navigator.onLine) {
      // Offline: aus dem vorab geladenen Pool bedienen.
      const r = this.ratingRange();
      const pooled = takeFromPool(this.offlinePuzzlePool, r.min, r.max)
        ?? takeNearestFromPool(this.offlinePuzzlePool, (r.min + r.max) / 2);
      if (!pooled) {
        // Pool ist leer. Falls wir vorher schon mindestens ein Puzzle hatten, ist der Vorrat
        // verbraucht (Pool exhausted) — dann „Letztes nochmal spielen" anbieten statt einfach
        // „nichts gespeichert" zu zeigen. Sonst (Erstaufruf offline ohne Cache): no-cache.
        if (this.lastShownPuzzle) this.offlinePoolExhausted = true;
        else this.offlineNoCache = true;
        this.state = 'ERROR';
        this.puzzle = null;
        return;
      }
      this.saveOfflinePool();
      source$ = of(pooled);
    } else {
      const r = this.ratingRange();
      source$ = this.puzzleService.getRandom(r.min, r.max, undefined, this.excludeSolved, this.worstThemesParam);
    }

    source$.subscribe({
        next: puzzle => {
          // Bisher angezeigtes Puzzle als „vorheriges" merken (für Teilen-Dialog).
          if (this.puzzle && this.puzzle.id !== puzzle.id) this.previousPuzzleId = this.puzzle.id;
          this.puzzle = puzzle;
          this.lastShownPuzzle = puzzle;
          this.setupPuzzle(puzzle);
          this.prefetchNext();
          this.prefetchOfflinePool();
        },
        error: () => {
          this.state = 'ERROR';
          this.puzzle = null;
        }
      });
  }

  /** Spielt das zuletzt geladene Puzzle nochmal (Fallback wenn der Offline-Pool aufgebraucht ist). */
  replayLastPuzzle(): void {
    if (!this.lastShownPuzzle) return;
    this.offlineNoCache = false;
    this.offlinePoolExhausted = false;
    this.attemptRecorded = false;
    this.gaveUp = false;
    this.elapsedSeconds = 0;
    this.alternativeSolve = false;
    this.lastEloChange = null;
    this.showEval = false;
    this.initialEval = '';
    this.currentEval = '';
    this.puzzle = this.lastShownPuzzle;
    this.setupPuzzle(this.lastShownPuzzle);
  }

  private prefetchNext(): void {
    const r = this.ratingRange();
    this.puzzleService.getRandom(r.min, r.max, undefined, this.excludeSolved, this.worstThemesParam)
      .subscribe({ next: p => this.nextPuzzle = p, error: () => {} });
  }

  /** Rating-Fenster aus aktueller Elo + Schwierigkeits-Offset (±RATING_WINDOW). */
  private ratingRange(): { min: number; max: number } {
    return puzzleWindow(this.stats?.puzzleElo ?? 1500, this.difficulty, this.ratingRangeBounds);
  }

  onDifficultyChange(): void {
    this.nextPuzzle = null;  // vorab geladenes Puzzle hatte die alte Schwierigkeit
    this.offlinePuzzlePool = [];   // Offline-Pool galt für die alte Schwierigkeit → neu füllen
    this.saveOfflinePool();
    this.prefetchOfflinePool();
    this.saveConfig();
  }

  private setupPuzzle(puzzle: PuzzleDto): void {
    this.reviewMode = false;
    this.reviewIndex = 0;
    // Lös-Automat (Setup, Zug-Handling, Stockfish, Viz) kommt aus BasePuzzleSolver.
    this.setupSolver(puzzle.fen, puzzle.moves, 0);
  }

  giveUp(): void {
    if (!this.puzzle) return;
    this.abortSolver();
    this.stopTimer();
    // Fehlversuch aufzeichnen (Elo-Loss + Statistik), falls noch nicht geschehen.
    if (!this.attemptRecorded) this.recordAttempt(false);
    this.gaveUp = true;
    this.state = 'FAILED';
    this.playSolutionFromStart();
  }

  retry(): void {
    if (!this.puzzle) return;
    this.clearSolutionPlay();
    this.attemptRecorded = false;
    this.gaveUp = false;
    this.setupPuzzle(this.puzzle);
  }

  override get reviewTotal(): number {
    return this.puzzle ? this.puzzle.moves.split(' ').filter(m => m).length : 0;
  }

  reviewNext(): void { this.stopCountdown(); this.clearSolutionPlay(); this.reviewGoTo(this.reviewIndex + 1); }
  reviewPrev(): void { this.stopCountdown(); this.clearSolutionPlay(); this.reviewGoTo(this.reviewIndex - 1); }

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


  reviewLastPuzzle(): void {
    // Direkt in den Analysemodus mit dem zuletzt gelösten Puzzle (Stellung + Zugfolge).
    if (this.lastSolvedFen) {
      this.router.navigate(['/analysis'], {
        queryParams: {
          fen: this.lastSolvedFen,
          moves: this.lastSolvedMoves.split(' ').filter(m => m).join(','),
          orientation: this.lastSolvedOrientation,
          from: this.puzzle?.id ? '/puzzles/' + this.puzzle.id : undefined,
        },
      });
      return;
    }
    if (this.lastSolvedPuzzleId) {
      this.router.navigate(['/puzzles', this.lastSolvedPuzzleId]);
    }
  }

  formatTime(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return m > 0 ? `${m}:${s.toString().padStart(2, '0')}` : `${s}s`;
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
    const id = this.puzzle.id;
    const url = this.isLoggedIn ? `/api/puzzles/${id}/attempt` : `/api/puzzles/${id}/attempt/anonymous`;
    const body: Record<string, unknown> = {
      solved, timeSpentSeconds: this.elapsedSeconds, moveLog: log ?? null,
      visualizationLevel: this.visualizationMode,
      evalShown: this.evalShown, vizShowCount: this.vizShowCount,
      screenWidth: window.innerWidth, screenHeight: window.innerHeight,
    };
    if (!this.isLoggedIn) body['sessionId'] = this.puzzleService.ensureSessionId();
    if (!navigator.onLine) {
      // Offline gelöst → für späteres Hochladen vormerken (Stats aktualisieren sich beim Sync).
      this.offlineQueue.enqueue('POST', url, body);
      return;
    }
    if (this.isLoggedIn) {
      this.puzzleService.recordAttempt(id, solved, this.elapsedSeconds, log, this.visualizationMode, this.evalShown, this.vizShowCount).subscribe({
        next: res => {
          if (res.eloChange != null) this.lastEloChange = res.eloChange;
          this.puzzleService.getStats(this.visualizationMode).subscribe(s => this.stats = s);
        },
        error: () => this.offlineQueue.enqueue('POST', url, body),
      });
      this.resolveChallengeIfNeeded(solved);
      this.notifyRevengeIfNeeded(solved);
    } else {
      this.puzzleService.recordAnonymousAttempt(id, solved, this.elapsedSeconds, log, this.visualizationMode, this.evalShown, this.vizShowCount).subscribe({
        next: () => this.puzzleService.getAnonymousStats().subscribe(s => this.stats = s),
        error: () => this.offlineQueue.enqueue('POST', url, body),
      });
    }
  }

  /** Meldet das Ergebnis genau einmal an eine offene Challenge zurück (fire-and-forget). */
  private resolveChallengeIfNeeded(solved: boolean): void {
    if (this.challengeId == null || this.challengeResolved) return;
    this.challengeResolved = true;
    this.challengeService.resolve(this.challengeId, solved, this.elapsedSeconds).subscribe({ next: () => {}, error: () => {} });
  }

  /** Informiert genau einmal den Freund, dessen gescheitertes Puzzle gerade gerächt wurde (fire-and-forget). */
  private notifyRevengeIfNeeded(solved: boolean): void {
    if (this.revengeUserId == null || this.revengeNotified || !this.puzzle) return;
    this.revengeNotified = true;
    this.revengeService.recordResult(this.revengeUserId, this.puzzle.id, solved).subscribe({ next: () => {}, error: () => {} });
  }

  /** Freundesliste fürs „An Freund schicken"-Menü faul laden (nur einmal). */
  loadFriendsForChallenge(): void {
    if (this.challengeFriends.length > 0) return;
    this.http.get<Friend[]>('/api/friends').subscribe({ next: f => this.challengeFriends = f, error: () => {} });
  }

  /** Aktuelles Puzzle als Challenge an einen Freund schicken. */
  sendChallenge(friendUserId: number): void {
    if (!this.puzzle) return;
    this.challengeService.send(friendUserId, this.puzzle.id).subscribe({
      next: () => this.snackbar.success(this.translate.instant('puzzles.challenge.sent')),
      error: (err) => this.snackbar.info(err.error?.message || this.translate.instant('puzzles.challenge.error')),
    });
  }

  toggleEval(): void {
    this.showEval = !this.showEval;
    if (this.showEval) {
      this.markEvalShown();
      if (this.state === 'PLAYING' || this.state === 'AWAITING_USER_MOVE') this.refreshEval();
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
    this.clearSolutionPlay();
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.currentEval = '';
    this.initialEval = '';
    this.showEval = false;
    this.setupPuzzle(this.puzzle);
  }

  // --- Config persistence ---

  private loadConfig(): void {
    this.boardTheme = this.prefs.boardTheme;
    this.pieceSet = this.prefs.pieceSet;
    this.themeMode = this.prefs.themeMode;
    this.stockfishDepth = this.prefs.stockfishDepth;
    this.visualizationMode = this.prefs.visualization;
    this.vizArrowEnabled = this.prefs.vizArrow;
    const d = this.prefs.puzzleDifficulty;
    if (d && d in DIFFICULTY_OFFSET) this.difficulty = d as typeof this.difficulty;
    this.worstTagsEnabled = this.prefs.puzzleWorstTags;
  }

  /** Aktive Schwächen-Themen für die Sichtbar-Anzeige während des Lösens (leer, wenn Filter aus). */
  get activeWorstThemes(): string[] {
    return this.worstTagsEnabled ? this.worstThemes : [];
  }

  /** True, wenn das gerade GELÖSTE Puzzle dieses Thema trägt — fürs Einfärben des passenden Chips. */
  themeMatched(theme: string): boolean {
    return this.state === 'SOLVED' && !!this.puzzle?.themes
      && this.puzzle.themes.split(/\s+/).includes(theme);
  }

  /** Themen-Param für die Puzzle-Auswahl, wenn „schwächste Themen trainieren" aktiv ist (sonst undefined). */
  private get worstThemesParam(): string | undefined {
    return this.worstTagsEnabled && this.worstThemes.length ? this.worstThemes.join(' ') : undefined;
  }

  /** Lädt die schwächsten Themen (nur eingeloggt + aktiviert); ruft danach cb. */
  private ensureWorstThemes(cb: () => void): void {
    if (!this.worstTagsEnabled || !this.isLoggedIn || this.worstThemes.length) { cb(); return; }
    this.puzzleService.getWorstThemes().subscribe({
      next: themes => { this.worstThemes = themes; cb(); },
      error: () => cb(),
    });
  }

  setVisualizationLevel(level: number): void {
    this.visualizationMode = level;
    this.prefs.setVisualization(level);
    if (this.isLoggedIn) {
      this.puzzleService.getStats(level).subscribe(s => this.stats = s);
    }
    if (this.puzzle) this.setupPuzzle(this.puzzle);  // Modus-Wechsel = Puzzle neu starten
  }

  setVizArrowEnabled(val: boolean): void {
    this.vizArrowEnabled = val;
    if (!val) this.clearVizOpponentArrow();
    this.prefs.setVizArrow(val);
  }

  saveConfig(): void {
    this.prefs.setStockfishDepth(this.stockfishDepth);
    this.prefs.setPuzzleDifficulty(this.difficulty);
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

  openSettingsDialog(): void {
    const ref = this.dialog.open(PuzzleSettingsDialogComponent, {
      data: {
        mode: 'standard',
        boardTheme: this.prefs.boardTheme,
        pieceSet: this.prefs.pieceSet,
        themeMode: this.themeMode,
        visualizationMode: this.visualizationMode,
        vizArrowEnabled: this.vizArrowEnabled,
        stockfishDepth: this.stockfishDepth,
        difficulty: this.difficulty,
        excludeSolved: this.excludeSolved,
        worstTags: this.worstTagsEnabled,
        isLoggedIn: this.isLoggedIn,
        puzzleElo: this.stats?.puzzleElo,
      } as PuzzleSettingsDialogData,
      width: '360px',
      maxWidth: '95vw',
    });
    ref.afterClosed().subscribe((result: PuzzleSettingsDialogResult | null) => {
      if (!result) return;
      this.setBoardTheme(result.boardTheme);
      this.setPieceSet(result.pieceSet);
      this.setThemeMode(result.themeMode);
      this.setVisualizationLevel(result.visualizationMode);
      this.setVizArrowEnabled(result.vizArrowEnabled);
      if (result.stockfishDepth !== undefined) {
        this.stockfishDepth = result.stockfishDepth;
        this.prefs.setStockfishDepth(this.stockfishDepth);
      }
      if (result.difficulty !== undefined && result.difficulty !== this.difficulty) {
        this.difficulty = result.difficulty as typeof this.difficulty;
        this.prefs.setPuzzleDifficulty(this.difficulty);
        this.onDifficultyChange();
        this.loadNext();
      }
      if (result.excludeSolved !== undefined) {
        this.excludeSolved = result.excludeSolved;
      }
      if (result.worstTags !== undefined && result.worstTags !== this.worstTagsEnabled) {
        this.worstTagsEnabled = result.worstTags;
        this.prefs.setPuzzleWorstTags(this.worstTagsEnabled);
        // Vorab geladenes Puzzle + Offline-Pool galten für den alten Filter → neu aufbauen.
        this.nextPuzzle = null;
        this.offlinePuzzlePool = [];
        this.saveOfflinePool();
        this.ensureWorstThemes(() => { this.prefetchOfflinePool(); this.loadNext(); });
      }
    });
  }
}
