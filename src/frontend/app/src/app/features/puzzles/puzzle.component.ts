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
import { ChallengeFriendsComponent } from './challenge-friends.component';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleSettingsDialogComponent, PuzzleSettingsDialogData, PuzzleSettingsDialogResult } from './puzzle-settings-dialog.component';
import { PuzzleService, PuzzleDto, PuzzleStatsDto, PuzzleRatingRange } from './puzzle.service';
import { OfflineService, PUZZLE_POOL_KEY } from '../../core/offline.service';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { DIFFICULTY_OFFSET, puzzleWindow } from './puzzle-window.util';
import { classifyStandardFirstMove, FirstMoveHint } from './puzzle-hints.util';
import { takeFromPool, takeNearestFromPool } from './endless-prefetch.util';
import { StockfishService } from './stockfish.service';
import { AuthService } from '../../core/auth.service';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { ChallengeService } from '../../core/challenge.service';
import { RevengeService } from '../../core/revenge.service';
import { BOARD_THEMES, PIECE_SETS, ThemeMode, applyThemeMode, clearCrazyStyles, clearVisualizationHide, parseShareViewParams } from './board-theme.util';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { applyUci } from './puzzle-move.util';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { LongSolveService } from './long-solve.service';
import { of } from 'rxjs';

type PuzzleState = 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE' | 'THINKING' | 'PLAYING' | 'SOLVED' | 'FAILED' | 'ERROR';


@Component({
  selector: 'app-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatProgressSpinnerModule, MatMenuModule, MatDialogModule, TranslateModule, PuzzleBoardComponent,
    PuzzleRatingCardComponent, PuzzleStatusCardComponent, ChallengeFriendsComponent
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

  // elapsedSeconds / Stoppuhr / start-/stopTimer / formatTime: jetzt in BasePuzzleSolver (geteilt).

  private attemptRecorded = false;
  private nextPuzzle: PuzzleDto | null = null;
  /** Monotone Epoche je Ladevorgang. Eine ältere, langsamer auflösende Puzzle-Anfrage
   *  (schnelle Navigation: Auto-Advance + „Weiter" + Prefetch) darf ein neueres Puzzle
   *  nicht überschreiben → veraltete Antworten werden anhand der Epoche verworfen. */
  private loadEpoch = 0;
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
  /** Warteschlange der noch offenen Revenge-Puzzle-Ids des Freundes (ohne das aktuelle). */
  private revengeQueue: number[] = [];
  /** Revanche-Runde durch → Glückwunsch-/Feuerwerk-Card statt nächstem Puzzle. */
  revengeComplete = false;
  revengeFriendName = '';
  revengeSolvedCount = 0;
  revengeTotalCount = 0;
  /** Indizes für die Feuerwerk-Funken im Template. */
  readonly fireworkDots = Array.from({ length: 28 }, (_, i) => i);
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
    private http: HttpClient,
    private longSolve: LongSolveService
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

  protected override handleSolved(alternative: boolean): void {
    this.state = 'SOLVED';
    this.stopTimer();
    this.updateBoard();
    this.lastSolvedPuzzleId = this.puzzle?.id ?? null;
    this.lastSolvedFen = this.puzzle?.fen ?? null;
    this.lastSolvedMoves = this.puzzle?.moves ?? '';
    this.lastSolvedOrientation = this.orientation;
    this.enterSolutionReview();
    // Auffällig lange Lösezeit (Tab lag vermutlich offen) → nachfragen, bevor gewertet wird; der
    // Dialog ist modal und blockiert „Weiter" dahinter. Aufzeichnen + Auto-Advance erst danach.
    this.longSolve.resolve(this.elapsedSeconds).subscribe(seconds => {
      this.recordAttempt(true, seconds);
      // Bei alternativer (eigener) Lösung NICHT automatisch weiterspringen — wie im Endless-Modus:
      // der Spieler entscheidet selbst (Weiter / Originallösung zeigen).
      if (alternative) return;
      this.startSolvedCountdown(() => this.loadNext());
    });
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
    this.dialog.open(SharePuzzleDialogComponent, {
      data: {
        url, previousUrl,
        puzzleId: this.puzzle.id,
        previousPuzzleId: this.previousPuzzleId ?? undefined,
        source: 'standard',
        canChallenge: this.isLoggedIn,
      },
      width: '400px',
      maxWidth: '95vw',
    });
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
    // Optionale Anzeige-Overrides aus dem (geteilten) Link (`?crazy=1`, `?visualmode=0–4`),
    // bevor das Puzzle aufgebaut wird. Transient — verändert keine gespeicherten Einstellungen.
    const ov = parseShareViewParams(this.route.snapshot.queryParamMap);
    if (ov.themeMode) this.themeMode = ov.themeMode;
    if (ov.visualization != null) this.visualizationMode = ov.visualization;

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

    // Als Revanche an einem gescheiterten Puzzle eines Freundes geöffnet → den Freund informieren
    // und im Revenge-Modus bleiben (offene Puzzles des Freundes nacheinander durchspielen).
    const revengeParam = this.route.snapshot.queryParamMap.get('revengeUserId');
    if (revengeParam) {
      this.revengeUserId = Number(revengeParam) || null;
      if (this.revengeUserId) this.loadRevengeQueue(this.revengeUserId);
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
    const epoch = ++this.loadEpoch;
    this.state = 'LOADING';
    this.offlineNoCache = false;
    this.offlinePoolExhausted = false;
    this.attemptRecorded = false;
    this.revengeNotified = false;
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
          if (epoch !== this.loadEpoch) return;   // veraltete Antwort eines früheren Ladevorgangs
          // Bisher angezeigtes Puzzle als „vorheriges" merken (für Teilen-Dialog).
          if (this.puzzle && this.puzzle.id !== puzzle.id) this.previousPuzzleId = this.puzzle.id;
          this.puzzle = puzzle;
          this.lastShownPuzzle = puzzle;
          this.setupPuzzle(puzzle);
          this.prefetchNext();
          this.prefetchOfflinePool();
        },
        error: () => {
          if (epoch !== this.loadEpoch) return;
          this.state = 'ERROR';
          this.puzzle = null;
        }
      });
  }

  /** Spielt das zuletzt geladene Puzzle nochmal (Fallback wenn der Offline-Pool aufgebraucht ist). */
  replayLastPuzzle(): void {
    if (!this.lastShownPuzzle) return;
    ++this.loadEpoch;   // evtl. noch laufenden loadNext entwerten
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
    const epoch = this.loadEpoch;
    const r = this.ratingRange();
    this.puzzleService.getRandom(r.min, r.max, undefined, this.excludeSolved, this.worstThemesParam)
      .subscribe({ next: p => { if (epoch === this.loadEpoch) this.nextPuzzle = p; }, error: () => {} });
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

  /** On-the-fly klassifizierter erster Löserzug (Schach/Schlag/ruhig) — Basis der gestuften Tipps. */
  private firstMoveHint: FirstMoveHint | null = null;

  /**
   * On-the-fly-Tipps für Standard-Puzzles (kein vorberechneter Speicher): Stufe 1 = Check-Capture-
   * Threat-Hinweis je nach Zugtyp, Stufe 2 = welche Figur zieht, Stufe 3 = der Zug (SAN).
   */
  override get availableHints(): string[] {
    const h = this.firstMoveHint;
    if (!h) return [];
    const t = (k: string, p?: object) => this.translate.instant(k, p) as string;
    const tier1 = h.type === 'check' ? t('puzzles.hints.t1Check')
      : h.type === 'capture' ? t('puzzles.hints.t1Capture')
      : t('puzzles.hints.t1Quiet');
    const PIECE: Record<string, string> = { p: 'pawn', n: 'knight', b: 'bishop', r: 'rook', q: 'queen', k: 'king' };
    const piece = t('puzzles.hints.pieces.' + (PIECE[h.pieceType] ?? 'piece'));
    return [tier1, t('puzzles.hints.t2Piece', { piece }), t('puzzles.hints.t3Move', { move: h.san })];
  }

  flagSaving = false;

  /** „Dumme Tipps" markieren/aufheben (jeder eingeloggte User; nur möglich, wenn schon ein Tipp aufgedeckt war). */
  toggleHintsFlag(): void {
    if (!this.puzzle || this.flagSaving) return;
    const next = !this.puzzle.hintsFlagged;
    this.flagSaving = true;
    this.puzzleService.flagPuzzleHints(this.puzzle.id, next).subscribe({
      next: () => {
        if (this.puzzle) this.puzzle.hintsFlagged = next;
        this.flagSaving = false;
        this.snackbar.success(this.translate.instant(next ? 'puzzles.hints.flagSaved' : 'puzzles.hints.flagCleared'), { duration: 2000 });
      },
      error: () => {
        this.flagSaving = false;
        this.snackbar.warn(this.translate.instant('puzzles.hints.flagError'), { duration: 3000 });
      }
    });
  }

  private setupPuzzle(puzzle: PuzzleDto): void {
    this.reviewMode = false;
    this.reviewIndex = 0;
    this.hintLevel = 0;
    this.firstMoveHint = classifyStandardFirstMove(puzzle.fen, puzzle.moves);
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

  /** Nach alternativem (eigenem) Mattweg die vom Puzzle vorgesehene Lösung von vorne durchspielen. */
  showOriginalSolution(): void { this.stopCountdown(); this.playSolutionFromStart(); }

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


  /** `seconds` = zu wertende Lösezeit; default = gemessene Zeit (Fehlversuche), beim Lösen ggf. wegen
   *  überlanger Zeit gekappt (siehe {@link LongSolveService}). */
  private recordAttempt(solved: boolean, seconds: number = this.elapsedSeconds): void {
    if (!this.puzzle || this.attemptRecorded) return;
    this.attemptRecorded = true;
    const log = this.moveLog.length > 0 ? JSON.stringify(this.moveLog) : undefined;
    const id = this.puzzle.id;
    const url = this.isLoggedIn ? `/api/puzzles/${id}/attempt` : `/api/puzzles/${id}/attempt/anonymous`;
    const body: Record<string, unknown> = {
      solved, timeSpentSeconds: seconds, moveLog: log ?? null,
      visualizationLevel: this.visualizationMode,
      evalShown: this.evalShown, vizShowCount: this.vizShowCount,
      hintsUsed: this.hintLevel,
      screenWidth: window.innerWidth, screenHeight: window.innerHeight,
    };
    if (!this.isLoggedIn) body['sessionId'] = this.puzzleService.ensureSessionId();
    if (!navigator.onLine) {
      // Offline gelöst → für späteres Hochladen vormerken (Stats aktualisieren sich beim Sync).
      this.offlineQueue.enqueue('POST', url, body);
      return;
    }
    if (this.isLoggedIn) {
      this.puzzleService.recordAttempt(id, solved, seconds, log, this.visualizationMode, this.evalShown, this.vizShowCount, this.hintLevel).subscribe({
        next: res => {
          if (res.eloChange != null) this.lastEloChange = res.eloChange;
          this.puzzleService.getStats(this.visualizationMode).subscribe(s => this.stats = s);
        },
        error: () => this.offlineQueue.enqueue('POST', url, body),
      });
      this.resolveChallengeIfNeeded(solved, seconds);
      this.notifyRevengeIfNeeded(solved);
    } else {
      this.puzzleService.recordAnonymousAttempt(id, solved, seconds, log, this.visualizationMode, this.evalShown, this.vizShowCount, this.hintLevel).subscribe({
        next: () => this.puzzleService.getAnonymousStats().subscribe(s => this.stats = s),
        error: () => this.offlineQueue.enqueue('POST', url, body),
      });
    }
  }

  /** Meldet das Ergebnis genau einmal an eine offene Challenge zurück (fire-and-forget). */
  private resolveChallengeIfNeeded(solved: boolean, seconds: number = this.elapsedSeconds): void {
    if (this.challengeId == null || this.challengeResolved) return;
    this.challengeResolved = true;
    this.challengeService.resolve(this.challengeId, solved, seconds).subscribe({ next: () => {}, error: () => {} });
  }

  /** Informiert genau einmal den Freund, dessen gescheitertes Puzzle gerade gerächt wurde (fire-and-forget). */
  private notifyRevengeIfNeeded(solved: boolean): void {
    if (this.revengeUserId == null || this.revengeNotified || !this.puzzle) return;
    this.revengeNotified = true;
    if (solved) this.revengeSolvedCount++;
    this.revengeService.recordResult(this.revengeUserId, this.puzzle.id, solved).subscribe({ next: () => {}, error: () => {} });
  }

  /** Offene Revenge-Puzzles des Freundes laden und als Warteschlange für die Revanche-Runde merken. */
  private loadRevengeQueue(friendUserId: number): void {
    this.http.get<{ displayName: string | null; username: string; puzzles: { puzzleId: number; solvedByViewer: boolean }[] }>(
      `/api/friends/${friendUserId}/revenge`
    ).subscribe({
      next: d => {
        this.revengeFriendName = d.displayName || d.username;
        const openIds = d.puzzles.filter(p => !p.solvedByViewer).map(p => p.puzzleId);
        this.revengeTotalCount = openIds.length;
        // Das aktuell geladene Puzzle wird zuerst gespielt → aus der Restschlange nehmen.
        this.revengeQueue = openIds.filter(id => id !== this.routePuzzleId && id !== this.puzzle?.id);
      },
      error: () => {}
    });
  }

  /** „Nächstes": im Revenge-Modus das nächste offene Puzzle des Freundes, sonst normales Zufallspuzzle. */
  onNext(): void {
    if (this.revengeUserId != null) { this.advanceRevenge(); return; }
    this.loadNext();
  }

  /** Nächstes Puzzle der Revanche-Runde laden; ist die Schlange leer → Glückwunsch/Feuerwerk. */
  private advanceRevenge(): void {
    const nextId = this.revengeQueue.shift();
    if (nextId == null) {
      this.stopTimer();
      this.stopCountdown();
      this.revengeComplete = true;
      return;
    }
    this.routePuzzleId = nextId;
    this.loadNext();
  }

  /** Von der Feuerwerk-Card zurück zur Revanche-Liste des Freundes (zeigt jetzt die erledigten). */
  goToRevengeList(): void {
    if (this.revengeUserId != null) this.router.navigate(['/friends', this.revengeUserId, 'revenge']);
  }

  /** Freundesliste fürs „An Freund schicken"-Menü faul laden (nur einmal). */
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

  protected override refreshEvalIfShown(): void {
    if (this.showEval) this.refreshEval();
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
