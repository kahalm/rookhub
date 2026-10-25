import { Component, OnDestroy, OnInit, HostListener, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { SnackbarService } from '../../core/snackbar.service';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { BoardFsActionsComponent } from './board-fs-actions.component';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { PuzzleActionBarComponent } from './puzzle-action-bar.component';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleSettingsDialogComponent, PuzzleSettingsDialogData, PuzzleSettingsDialogResult } from './puzzle-settings-dialog.component';
import { PuzzleStatusCardComponent } from './puzzle-status-card.component';
import { ChallengeFriendsComponent } from './challenge-friends.component';
import { EndlessResultComponent } from './endless-result.component';
import { EndlessConfigComponent } from './endless-config.component';
import { EndlessHelpDialogComponent, EndlessHelpPhases } from './endless-help-dialog.component';
import { PuzzleService, PuzzleDto, PuzzleRatingRange } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { EndlessConfig, EndlessSession } from './endless-storage.service';
import { chainRatingAt, ENDLESS_RATING_WINDOW, CHAIN_T1_INDEX, CHAIN_T2_INDEX, CHAIN_FLAT_STEP, FIRST_RUN_ANCHOR1_INDEX, FIRST_RUN_ANCHOR1_RATING, FIRST_RUN_ANCHOR2_INDEX, FIRST_RUN_ANCHOR2_RATING } from './endless-prefetch.util';
import { EndlessFasttrackState } from './endless-fasttrack-state';
import { EndlessPuzzleAttempt, EndlessSessionRecorder } from './endless-session-recorder';
import { EndlessRunStore } from './endless-run.store';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { FavoritesService } from '../../core/favorites.service';
import { loadLastSolved, saveLastSolved } from './last-solved-store';
import { WorksheetService } from '../worksheets/worksheet.service';
import { taskItemFromPuzzle } from '../worksheets/worksheet-items.util';
import { FavoriteTracker } from './favorite-tracker';
import { AuthService } from '../../core/auth.service';
import { PreferencesService } from '../../core/preferences.service';
import { BOARD_THEMES, PIECE_SETS, ThemeMode, applyThemeMode, parseShareViewParams } from './board-theme.util';
import { buildStagedHints } from './puzzle-hints.util';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { epForcedHintTexts, offPathHintTexts, offPathWarningText } from './solver-texts';
import { PuzzleThemePipe } from './puzzle-theme.pipe';
import { LongSolveService } from './long-solve.service';
import { SolveMode, SolveModeService } from '../../core/solve-mode.service';
import { isBoardHotkey } from '../../shared/keyboard.util';

/** Bereichs-Schlüssel der Spielweise (SolveModeService) für den Endless-Modus. */
const SOLVE_SCOPE = 'endless';

/** Endzustände dieses Lösers (die übrigen setzt {@link BasePuzzleSolver}). */
export type EndlessEndState = 'CONFIG' | 'SOLVED' | 'FAILED' | 'GAME_OVER' | 'WON';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-endless-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    IconLabelDirective, TranslatePipe, PuzzleBoardComponent, BoardFsActionsComponent,
    PuzzleActionBarComponent, PuzzleStatusCardComponent, ChallengeFriendsComponent, PuzzleThemePipe,
    EndlessConfigComponent, EndlessResultComponent,
  ],
  providers: [EndlessRunStore],
  templateUrl: './endless-puzzle.component.html',
  styleUrls: ['./endless-puzzle.component.scss'],
})
export class EndlessPuzzleComponent extends BasePuzzleSolver<EndlessEndState> implements OnDestroy, OnInit {
  // Zustände (state, String aus BasePuzzleSolver): CONFIG, LOADING, SETUP, AWAITING_USER_MOVE (erster Zug, keine
  // Knöpfe), THINKING (Gegner antwortet, Brett gesperrt), PLAYING (Löser am Zug), SOLVED, FAILED, GAME_OVER, WON.
  get screen(): 'config' | 'play' | 'gameover' | 'won' | 'loading' {
    // History-Detail wird geladen → Spinner statt leerem Brett (kein gültiges Puzzle vorhanden).
    if (this.run.historyView() && this.state === 'LOADING') return 'loading';
    if (this.state === 'WON') return 'won';
    if (this.state === 'GAME_OVER') return 'gameover';
    if (this.state === 'CONFIG') return 'config';
    return 'play';
  }

  /** Konfiguration des Laufs (zwei-Wege an Start-Bildschirm und Tiefen-Regler) — liegt im {@link EndlessRunStore}. */
  get config(): EndlessConfig { return this.run.config; }

  /** Für das Config-Template: „schwächste Themen"-Option nur eingeloggt anbieten. */
  get isLoggedIn(): boolean { return this.authService.isLoggedIn; }

  /** Aktive Themen-Filter für die Sichtbar-Anzeige (Config + Run). Leer = kein Filter. */
  get activeFilterThemes(): string[] { return this.run.profile.activeFilterThemes(); }

  /** True, wenn das gerade GELÖSTE Puzzle dieses Thema trägt — fürs Einfärben des passenden Chips. */
  themeMatched(theme: string): boolean {
    return this.state === 'SOLVED' && !!this.puzzle?.themes
      && this.puzzle.themes.split(/\s+/).includes(theme);
  }

  /** Toggle „5 schwächste Themen trainieren": schwächste Themen laden und in die Editbox schreiben (sichtbar). */
  onWorstTagsToggle(enabled: boolean): void {
    this.run.profile.toggleWorstTags(enabled, () => this.saveConfig());
  }

  /** Feineinstellungen des Start-Bildschirms aufgeklappt (Stockfish-Tiefe, Schwellen, Kurvenvorschau; zwei-Wege an
   *  EndlessConfigComponent). Standardmäßig zu: vor dem Start zählen Start-Rating und Themen, alles andere ist Nachjustierung. */
  showAdvanced = false;

  /** Alle verfügbaren Themen (vom Backend, alphabetisch) — Optionen des Autocomplete-Dropdowns im Start-Bildschirm. */
  allThemes: string[] = [];

  /** Lädt die verfügbaren Themen einmalig (best effort; Freitext bleibt auch ohne Liste möglich). */
  private loadAllThemes(): void {
    if (this.allThemes.length) return;
    this.puzzleService.getAllThemes().subscribe({
      next: t => this.allThemes = t,
      error: () => {},   // Liste optional — manuelles Tippen funktioniert weiterhin
    });
  }

  // Lauf-Kennzahlen für Template und Kind-Bildschirme (Quelle: EndlessRunStore).
  get lives(): number { return this.run.lives(); }
  get level(): number { return this.run.level(); }
  get solved(): number { return this.run.solved(); }
  get maxRatingReached(): number { return this.run.maxRatingReached(); }
  get isNewHighscore(): boolean { return this.run.isNewHighscore(); }
  get highscore(): number { return this.run.profile.highscore(); }
  get sessionSeconds(): number { return this.run.sessionSeconds(); }
  get sessionHistory(): EndlessSession[] { return this.run.profile.sessionHistory(); }
  get currentSessionMistakes(): number[] { return this.run.mistakes; }
  get currentSessionPuzzles(): EndlessPuzzleAttempt[] { return this.run.puzzles; }
  get activeGameState(): any { return this.run.activeGameState(); }
  get lastSessionId(): number | null { return this.run.lastSessionId(); }
  get lastSessionArchived(): boolean { return this.run.lastSessionArchived(); }
  get archiving(): boolean { return this.run.archiving(); }
  /** Game-Over-Ansicht zeigt einen abgeschlossenen Lauf aus der History (?session=ID). */
  get historyView(): boolean { return this.run.historyView(); }
  get puzzleRange(): PuzzleRatingRange { return this.run.profile.puzzleRange; }
  get fasttrack(): EndlessFasttrackState { return this.run.profile.fasttrack; }

  // Board theme
  boardTheme = 'brown';
  readonly boardThemes = BOARD_THEMES;

  pieceSet = 'cburnett';
  readonly pieceSets = PIECE_SETS;

  /** `?start=1`/`?autostart=1`: Endless-Lauf direkt starten (sobald die Rating-Range geladen ist),
   *  ohne den Config-Screen. Wird in ngOnInit aus der URL gesetzt, ausgelöst in maybeAutoStart(). */
  private autoStartRequested = false;

  /** ID des zuvor gespielten Puzzles (für „vorheriges Puzzle teilen"). */
  private previousPuzzleId: number | null = null;

  // Board (Brett/Viz/Lös-State in BasePuzzleSolver)
  puzzle: PuzzleDto | null = null;
  reviewingWrongPuzzle = false;
  gaveUp = false;
  // elapsedSeconds + formatTime: aus BasePuzzleSolver geerbt (Endless setzt elapsedSeconds aus der
  // eigenen Puzzle-Stoppuhr und nutzt start/stopTimer der Basis NICHT).

  // Zuletzt gelöstes Puzzle — für „Letztes Puzzle analysieren" (bleibt auch nach dem
  // Auto-Advance auf das nächste Puzzle erhalten, da der SOLVED-Status nur kurz sichtbar ist).
  lastSolvedPuzzleId: number | null = null;
  private lastSolvedFen: string | null = null;
  private lastSolvedMoves = '';
  private lastSolvedOrientation: 'white' | 'black' = 'white';
  /** Themen des letzten Puzzles — Vorschlagsquelle für die Themen eines Aufgabenblatts. */
  private lastSolvedThemes = '';
  /** „Geliebtes Puzzle"-Zustand (Herz) für aktuelles + zuletzt gelöstes Puzzle. */
  readonly favoriteTracker: FavoriteTracker;

  /** Gewählte Spielweise für den Bereich „Endless" (Training/Einfach). `null` = es wurde bewusst
   *  nicht gefragt (fester Ansichts-Link ?visualmode=) — dann bleibt es bei der Stufe aus den
   *  Einstellungen bzw. aus der URL, und das ⋮-Menü zeigt keinen Umschalter. */
  solveModeChoice: SolveMode | null = null;
  /** True, wenn in diesem Aufruf nicht nach der Spielweise gefragt werden darf (siehe oben). */
  private solveModeAskSuppressed = false;

  constructor(
    private puzzleService: PuzzleService,
    stockfish: StockfishService,
    public authService: AuthService,
    private prefs: PreferencesService,
    public router: Router,
    private route: ActivatedRoute,
    private dialog: MatDialog,
    private translate: TranslateService,
    private snackbar: SnackbarService,
    private offlineQueue: OfflineQueueService,
    private longSolve: LongSolveService,
    private favorites: FavoritesService,
    private solveMode: SolveModeService,
    private worksheets: WorksheetService,
    /** Lauf-Zustand und -Übergänge (je Seite, siehe `providers`). */
    readonly run: EndlessRunStore,
  ) {
    super(stockfish);
    this.run.attach({
      setState: s => { this.state = s; },
      loadCurrent: () => this.loadCurrent(),
      notice: (key, duration) => this.snackbar.info(this.translate.instant(key), { action: 'common.ok', duration }),
      tick: seconds => { this.elapsedSeconds = seconds; },
    });
    this.favoriteTracker = new FavoriteTracker(
      this.favorites, 'standard',
      () => this.puzzle?.id, () => this.lastSolvedPuzzleId, () => this.isLoggedIn,
    );
    this.state = 'CONFIG';
    this.loadSettingsOpen();
    // Load board theme from preferences service
    this.boardTheme = this.prefs.boardTheme;
    this.pieceSet = this.prefs.pieceSet;
    this.themeMode = this.prefs.themeMode;
    this.visualizationMode = this.prefs.visualization;
    this.vizArrowEnabled = this.prefs.vizArrow;
    // 1. Load from localStorage immediately (no latency); 2. Server-Stand (async) einmischen
    this.run.loadLocal();
    this.run.syncFromServer(() => this.state === 'CONFIG');
    this.run.loadRatingRange(() => this.maybeAutoStart());   // ?start=1: jetzt (Range geladen) direkt loslegen
    this.stockfish.init().catch(() => {});
  }

  ngOnInit(): void {
    // Verfügbare Themen fürs Auswahl-Dropdown laden (unabhängig vom Einstiegs-Modus).
    this.loadAllThemes();

    // „Letztes Puzzle" (analysieren / ♥ / teilen) über Navigation zu /analysis hinweg wiederherstellen —
    // ohne Persistenz wird `lastSolvedPuzzleId` beim Component-Destroy null und die Knöpfe verschwinden.
    const restored = loadLastSolved('endless');
    if (restored) {
      this.lastSolvedPuzzleId = restored.id;
      this.lastSolvedFen = restored.fen;
      this.lastSolvedMoves = restored.moves;
      this.lastSolvedOrientation = restored.orientation;
      this.lastSolvedThemes = restored.themes ?? '';
      this.favoriteTracker.refresh();
    }

    // History-Detail (?session=ID): abgeschlossenen Lauf wie den Game-Over-Screen anzeigen.
    // Stats + Puzzle-Review werden aus dem persistierten Lauf rekonstruiert.
    const sessionParam = this.route.snapshot.queryParamMap.get('session');
    if (sessionParam) {
      const id = parseInt(sessionParam, 10);
      if (id > 0) { this.loadHistorySession(id); return; }
    }

    // Rückkehr aus dem Analysemodus nach verlorenstem letzten Herz (?gameover=1):
    // Gameover-Snapshot aus sessionStorage wiederherstellen und Zusammenfassungs-Screen zeigen.
    if (this.route.snapshot.queryParamMap.get('gameover') === '1') {
      const s = EndlessSessionRecorder.takeGameOverSnapshot();
      // lastSessionId könnte inzwischen asynchron eingetroffen sein (recordSession-Subscribe); der Run wurde vor dem
      // Absprung in die Analyse bereits via run.finish() aufgezeichnet → nicht erneut posten.
      this.run.restoreGameOver(s, EndlessSessionRecorder.takePendingSessionId());
      this.state = 'GAME_OVER';
      return;
    }
    // Direkt-Start-/Ansichts-Parameter (Deep-Link, z. B. geteilter Endless-Link):
    //  ?themes=fork,pin (oder ?tags=…) → Themenfilter vorbelegen (Komma/Leerzeichen, ODER-Filter)
    //  ?elo=1500                       → Start-Elo
    //  ?anarchy=max | max+1            → Crazy-Brett + erzwungenes en passant (max+1: Feld bestimmt Figurenstil)
    //  ?start=1 / ?autostart=1         → Lauf sofort starten (sobald Rating-Range geladen), ohne Config-Screen
    const qp = this.route.snapshot.queryParamMap;
    const overrides: Partial<EndlessConfig> = {};
    const themesParam = qp.get('themes') ?? qp.get('tags');
    if (themesParam) {
      const themes = themesParam.split(/[,\s]+/).map(t => t.trim()).filter(Boolean);
      if (themes.length) {
        overrides.worstTags = false;
        overrides.themes = [...new Set(themes)].join(' ');
      }
    }
    const eloParam = parseInt(qp.get('elo') ?? '', 10);
    if (Number.isFinite(eloParam) && eloParam > 0) overrides.startElo = eloParam;
    this.run.profile.applyUrlOverrides(overrides);

    const ov = parseShareViewParams(qp);
    if (ov.themeMode) this.themeMode = ov.themeMode;
    if (ov.visualization != null) this.visualizationMode = ov.visualization;
    this.anarchyForcedByUrl = !!ov.enPassantForced;   // Anarchy per URL: e.p. immer forciert (sonst folgt es der Einstellung)
    if (ov.crazyPieceMode) this.crazyPieceMode = ov.crazyPieceMode;  // ?anarchy=max+1 → Feld bestimmt Stil
    // Feste Ansicht aus dem Link (?visualmode=) → nicht nach der Spielweise fragen: wer einen Link
    // mit vorgegebener Ansicht öffnet, hat die Wahl schon getroffen.
    this.solveModeAskSuppressed = ov.visualization != null;

    this.autoStartRequested = qp.get('start') === '1' || qp.get('autostart') === '1';

    // Rückkehr aus dem Analysemodus (?resume=1): laufenden Run direkt fortsetzen statt in der
    // Übersicht zu landen. Bei normalem Einstieg (kein resume-Param) bleibt der Resume-Banner,
    // damit man Fortsetzen/Archivieren wählen kann. Der Konstruktor hat 0-Leben-Zombies bereits
    // bereinigt → activeGameState != null bedeutet: noch Leben übrig.
    if (this.route.snapshot.queryParamMap.get('resume') === '1' && this.run.activeGameState() && this.state === 'CONFIG') {
      this.resumeGame();
    }
  }

  /** Startet den Lauf, wenn per ?start=1 angefordert und wir noch im Config-Screen sind (idempotent). */
  private maybeAutoStart(): void {
    if (this.autoStartRequested && this.state === 'CONFIG') {
      this.autoStartRequested = false;
      this.startGame();
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.run.saveLiveIfRunning();   // finaler Live-Zeitstand (der 1-s-Tick hängt bis zu 1 s hinterher)
    this.run.stopSessionTimer();
    this.releaseSolver();
    this.run.rescueUnrecordedRun();
  }

  /** Tab/Fenster wird geschlossen oder in den Hintergrund verschoben. Best-effort-Variante des
   *  Sicherheitsnetzes für den Fall, dass ngOnDestroy nicht (rechtzeitig) läuft. */
  @HostListener('window:pagehide')
  onPageHide(): void {
    this.run.saveLiveIfRunning();
    this.run.rescueUnrecordedRun();
  }

  // --- Config ---

  get currentMinRating(): number { return this.run.currentMinRating(); }
  get currentMaxRating(): number { return this.run.currentMinRating() + ENDLESS_RATING_WINDOW; }
  get currentRating(): number { return this.run.currentMinRating(); }

  get currentPhaseLabel(): string {
    if (this.isFirstRun) {
      // Erster Lauf: Phasengrenzen folgen der steilen Erst-Lauf-Kurve (Anker 15/30),
      // step = durchschnittlicher Rating-Zuwachs je Puzzle in dieser Phase.
      const s = this.config.startElo, i = this.run.chainIndex();
      if (i < FIRST_RUN_ANCHOR1_INDEX)
        return this.translate.instant('endless.game.phaseLabel', { phase: 1, step: Math.round((FIRST_RUN_ANCHOR1_RATING - s) / FIRST_RUN_ANCHOR1_INDEX) });
      if (i < FIRST_RUN_ANCHOR2_INDEX)
        return this.translate.instant('endless.game.phaseLabel', { phase: 2, step: Math.round((FIRST_RUN_ANCHOR2_RATING - FIRST_RUN_ANCHOR1_RATING) / (FIRST_RUN_ANCHOR2_INDEX - FIRST_RUN_ANCHOR1_INDEX)) });
      return this.translate.instant('endless.game.phaseLabel', { phase: 3, step: CHAIN_FLAT_STEP });
    }
    // Adaptive Kurve: Phasengrenzen und Schritt aus derselben Kurve, die die Kette erzeugt
    // (chainRatingAt) — step = Anstieg zum nächsten Puzzle. Früher fest „/5" bzw. „20" aus einem
    // älteren linearen Modell, während die Kette bis Puzzle 10/25 log-förmig und danach um 15 stieg.
    const i = this.run.chainIndex(), s = this.config.startElo, t1 = this.fasttrack.avgFirst, t2 = this.fasttrack.avgSecond;
    const step = chainRatingAt(i + 1, s, t1, t2) - chainRatingAt(i, s, t1, t2);
    const phase = i < CHAIN_T1_INDEX ? 1 : i < CHAIN_T2_INDEX ? 2 : 3;
    return this.translate.instant('endless.game.phaseLabel', { phase, step });
  }

  /** Phasengrenzen der Hilfe (1-basierte Puzzle-Nummern) aus denselben Konstanten wie die Kette. */
  readonly phaseHelp: EndlessHelpPhases = {
    p1End: CHAIN_T1_INDEX, p2Start: CHAIN_T1_INDEX + 1, p2End: CHAIN_T2_INDEX,
    p3Start: CHAIN_T2_INDEX + 1, flatStep: CHAIN_FLAT_STEP,
  };

  /**
   * Erster Lauf des Users (keine abgeschlossene Session in der Historie) → bewusst steile,
   * schnell tödliche Kurve (2000 nach 15, 3000 nach 30 Puzzles), bis genug Daten für die
   * adaptive Kurve vorliegen.
   */
  get isFirstRun(): boolean { return this.run.profile.isFirstRun(); }

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
    // Stufe direkt gewählt → gemerkte Spielweise mitziehen, sonst widersprechen sich beide.
    this.solveModeChoice = this.solveMode.modeForLevel(level);
    this.solveMode.set(SOLVE_SCOPE, this.solveModeChoice);
    if (this.puzzle && this.isSolving) this.setupPuzzle(this.puzzle);  // laufendes Puzzle neu starten
  }

  // ===== Spielweise (Training/Einfach) =====

  /** Fragt beim ERSTEN Lauf nach der Spielweise; danach gilt die gemerkte Wahl ohne Dialog
   *  (SolveModeService). Ein Endless-Lauf ist eine Einheit → gefragt wird beim Start/Fortsetzen
   *  eines Laufs, nicht bei jedem Puzzle darin. Steht das erste Puzzle schon, wird es mit der
   *  frisch gewählten Spielweise neu aufgesetzt. */
  private askSolveMode(): void {
    if (this.solveModeAskSuppressed) return;
    this.solveMode.ensure(SOLVE_SCOPE, { scopeLabel: this.translate.instant('solveMode.scope.endless') })
      .subscribe(mode => {
        this.solveModeChoice = mode;
        this.applySolveModeLevel();
        if (this.puzzle && this.isSolving) this.setupPuzzle(this.puzzle);
      });
  }

  /** Visualisierungsstufe zur gemerkten Spielweise setzen. Wird bei jedem Puzzle-Start
   *  aufgerufen, damit ein Umschalten ab dem NÄCHSTEN Puzzle greift. Ohne gemerkte Wahl
   *  (nicht gefragt) bleibt die Stufe unangetastet. */
  private applySolveModeLevel(): void {
    if (!this.solveModeChoice) return;
    this.visualizationMode = this.solveMode.levelFor(this.solveModeChoice);
  }

  /** Spielweise umschalten (⋮-Menü). Wirkt ab dem NÄCHSTEN Puzzle — ein laufender Versuch darf
   *  nicht mitten im Rechnen die Regeln wechseln. */
  toggleSolveMode(): void {
    const neu: SolveMode = this.solveModeChoice === 'training' ? 'easy' : 'training';
    this.solveModeChoice = neu;
    this.solveMode.set(SOLVE_SCOPE, neu);
    this.snackbar.info(
      this.translate.instant(neu === 'easy' ? 'solveMode.switchedEasy' : 'solveMode.switchedTraining'),
      { duration: 3000 });
  }

  setVizArrowEnabled(val: boolean): void {
    this.vizArrowEnabled = val;
    if (!val) this.clearVizOpponentArrow();
    this.prefs.setVizArrow(val);
  }

  /** Hilfe zu Endless als Dialog: Rolle, Fokusfang, Esc und die Rückgabe des Fokus an den Hilfe-Knopf
   *  bringt MatDialog mit; der Titel (mat-dialog-title) benennt den Dialog. */
  openHelp(): void {
    this.dialog.open(EndlessHelpDialogComponent, { data: this.phaseHelp, width: '600px', maxWidth: '95vw' });
  }

  openSettingsDialog(): void {
    const ref = this.dialog.open(PuzzleSettingsDialogComponent, {
      data: {
        mode: 'endless',
        boardTheme: this.prefs.boardTheme,
        pieceSet: this.prefs.pieceSet,
        themeMode: this.themeMode,
        visualizationMode: this.visualizationMode,
        vizArrowEnabled: this.vizArrowEnabled,
        offPathWarnMoves: this.prefs.offPathWarnMoves,
        enPassantForced: this.prefs.enPassantForced,
      } as PuzzleSettingsDialogData,
      width: '360px',
      maxWidth: '95vw',
    });
    ref.afterClosed().subscribe((result: PuzzleSettingsDialogResult | null) => {
      if (!result) return;
      this.setBoardTheme(result.boardTheme);
      this.setPieceSet(result.pieceSet);
      this.setThemeMode(result.themeMode);
      // Nur eine GEÄNDERTE Stufe setzt neu auf — Brett/Figuren speichern darf den Versuch nicht neu starten.
      if (result.visualizationMode !== this.visualizationMode) this.setVisualizationLevel(result.visualizationMode);
      this.setVizArrowEnabled(result.vizArrowEnabled);
      if (result.offPathWarnMoves !== undefined) this.prefs.setOffPathWarnMoves(result.offPathWarnMoves);
      if (result.enPassantForced !== undefined) {
        this.prefs.setEnPassantForced(result.enPassantForced);
        // Der Link-Zwang (?anarchy=max) gilt unabhängig von der Einstellung (wie in onSetupStart).
        this.enPassantForced = this.anarchyForcedByUrl || (this.themeMode === 'crazy' && result.enPassantForced);
        if (this.isSolving) this.updateBoard();   // Zugziele des laufenden Versuchs nachziehen
      }
    });
  }

  // ===== Hooks für BasePuzzleSolver =====
  protected override get depth(): number { return this.config.stockfishDepth; }

  protected override onSetupStart(): void {
    const applied = applyThemeMode(this.themeMode, this.prefs.boardTheme, this.prefs.pieceSet);
    this.boardTheme = applied.boardTheme;
    this.pieceSet = applied.pieceSet;
    this.enPassantForced = this.anarchyForcedByUrl || (this.themeMode === 'crazy' && this.prefs.enPassantForced);
  }

  protected override get offPathWarnThreshold(): number { return this.prefs.offPathWarnMoves; }
  protected override onOffPathWarning(): void {
    this.snackbar.info(offPathWarningText(this.t, this.startWasBalanced(), this.playerEvalDisplay()),
      { action: 'common.ok', duration: 7000 });
  }
  private readonly t = (k: string, p?: object) => this.translate.instant(k, p) as string;
  protected override get epForcedHints(): string[] { return epForcedHintTexts(this.t); }
  protected override get offPathHints(): string[] { return offPathHintTexts(this.t, this.offPathUserMoveNumber); }

  protected override onSolvingBegins(): void {
    // Gewertete Dauer = nur aktive Tab-Zeit; nach einem Resume DESSELBEN Puzzles (Refresh mitten
    // im Lösen) wird die persistierte Zwischenzeit fortgesetzt statt bei 0 zu starten.
    this.elapsedSeconds = this.run.timer.beginPuzzle();
  }

  protected override handleSolved(alternative: boolean): void { this.puzzleSolved(alternative); }
  protected override handleFailed(): void { this.loseLife(); }

  // --- Game lifecycle ---

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

  startGame(): void {
    ++this.run.runGeneration;   // laufenden Hintergrund-Prefetch für diesen Run entwerten
    this.askSolveMode();        // Spielweise gilt für den ganzen LAUF, nicht je Puzzle
    this.run.profile.clampConfig();
    this.saveConfig();
    this.previousPuzzleId = null;
    this.run.start();
  }

  resumeGame(): void {
    if (!this.run.activeGameState()) return;
    ++this.run.runGeneration;   // laufenden Hintergrund-Prefetch entwerten
    this.askSolveMode();        // fortgesetzter Lauf ist derselbe Bereich → nach der ersten Wahl stumm
    this.run.resume();
  }

  archiveAndStartNew(): void {
    if (!this.run.activeGameState()) { this.startGame(); return; }
    this.run.archiveOpenRun(() => this.startGame());
  }

  archiveLastSession(): void { this.run.archiveLastSession(); }

  playAgain(): void {
    // Beendeten Run vollständig verwerfen (in-memory + Speicher), damit der Config-Screen
    // ihn nicht erneut zum Fortsetzen anbietet — sonst „Resume → Aufgeben → Nochmal →
    // wieder fortsetzbar"-Schleife (nur der Storage war genullt, der in-memory-Snapshot nicht).
    this.run.discardFinishedRun();
  }

  backToPuzzles(): void { this.router.navigate(['/puzzles']); }

  backToHistory(): void { this.router.navigate(['/puzzles/endless/history']); }

  openPuzzle(id: number): void {
    this.router.navigate(['/puzzles', id]);
  }

  /** Lädt einen abgeschlossenen Lauf aus der History und zeigt ihn im Game-Over-Layout an
   * (gleiche Detail-Ansicht wie direkt nach Spielende). */
  private loadHistorySession(id: number): void {
    this.run.loadHistorySession(id, () => this.router.navigate(['/puzzles/endless/history']));
  }

  // --- Loading (Gauntlet: vordefinierte Kette) ---

  /** Lädt das aktuelle Ketten-Puzzle; am Kettenende wird verlängert/gewonnen (EndlessRunStore). */
  private loadCurrent(): void {
    this.applySolveModeLevel();   // ein Umschalten der Spielweise greift ab diesem Puzzle
    this.state = 'LOADING';
    this.alternativeSolve = false;
    this.showEval = false;
    this.initialEval = '';
    this.currentEval = '';

    const next = this.run.currentPuzzle();
    if (!next) { this.run.handleChainEnd(); return; }
    this.onPuzzleLoaded(next);
  }

  private onPuzzleLoaded(puzzle: PuzzleDto): void {
    this.run.enterPuzzle(puzzle);   // Leben-Sperre lösen, Rating übernehmen
    // Bisher angezeigtes Puzzle als „vorheriges" merken (für Teilen-Dialog).
    if (this.puzzle && this.puzzle.id !== puzzle.id) this.previousPuzzleId = this.puzzle.id;
    this.puzzle = puzzle;
    this.setupPuzzle(puzzle);
  }

  // --- Puzzle setup ---

  /** On-the-fly klassifizierter erster Löserzug (Schach/Schlag/ruhig) — Basis der gestuften Tipps (wie Standard-Modus). */
  /**
   * On-the-fly-Tipps für die Standard-Puzzles des Endless-Modus (identisch zum Standard-Solver) — zum
   * AKTUELL erwarteten Zug (jeder Zug, nicht nur der erste): Stufe 1 = Check-Capture-Threat-Hinweis je
   * Zugtyp, Stufe 2 = welche Figur zieht, Stufe 3 = der Zug (SAN).
   */
  override get availableHints(): string[] {
    return buildStagedHints(this.currentMoveHint, (k, p) => this.translate.instant(k, p) as string);
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
    this.reviewingWrongPuzzle = false;
    this.gaveUp = false;
    this.reviewMode = false;
    this.hintLevel = 0;
    // Tipps werden pro erwartetem Zug on-the-fly aus der aktuellen Stellung erzeugt (currentMoveHint).
    // Lös-Automat (Setup, Zug-Handling, Stockfish, Viz) aus BasePuzzleSolver.
    this.setupSolver(puzzle.fen, puzzle.moves, 0);
  }

  private puzzleSolved(alternative: boolean): void {
    this.alternativeSolve = alternative;
    this.state = 'SOLVED';
    this.run.markSolved(this.puzzle);   // Serie + Lauf-Protokoll
    // Pro-Puzzle-Lösezeit JETZT festhalten (vor einer evtl. Nachfrage, die Zeit kosten würde).
    const perPuzzleSeconds = this.run.timer.puzzleSeconds();
    if (this.puzzle) {
      // Für „Letztes Puzzle analysieren" merken (überlebt den Auto-Advance).
      this.lastSolvedPuzzleId = this.puzzle.id;
      this.lastSolvedFen = this.puzzle.fen;
      this.lastSolvedMoves = this.puzzle.moves;
      this.lastSolvedOrientation = this.orientation;
      this.lastSolvedThemes = this.puzzle.themes ?? '';
      saveLastSolved('endless', {
        id: this.puzzle.id, fen: this.puzzle.fen,
        moves: this.puzzle.moves, orientation: this.orientation,
        themes: this.puzzle.themes ?? '',
      });
    }
    this.favoriteTracker.refresh();
    this.run.syncActiveGame(this.run.chainIndex() + 1);   // Ergebnis steht → Fortsetzen beim NÄCHSTEN Puzzle
    this.updateBoard();
    this.enterSolutionReview();

    // Auffällig lange Lösezeit (Tab lag vermutlich offen) → nachfragen, bevor die Zeit gewertet wird;
    // der Dialog ist modal und blockiert „Weiter" dahinter. Aufzeichnen + Auto-Advance erst danach.
    this.longSolve.resolve(perPuzzleSeconds).subscribe(seconds => {
      if (this.destroyed) return;   // Seite verlassen (Dialog schließt bei Navigation)
      this.recordAttempt(true, seconds);

      // Bei alternativer Lösung NICHT auto-advancen — der Spieler wählt Weiter / Lösung zeigen.
      if (alternative) return;
      // Bei 0 Herzen (gelöstes Retry am tödlichen Puzzle) gibt es kein nächstes Puzzle mehr →
      // kein Countdown; „Weiter" führt über continueAfterSolve ins Game Over.
      if (this.run.lives() <= 0) return;
      // Kurzer, sichtbarer Countdown bis zum nächsten Puzzle — per „Weiter"-Klick überspringbar.
      this.startSolvedCountdown(() => this.continueAfterSolve());
    });
  }

  continueAfterSolve(): void {
    this.reviewMode = false;
    this.stopCountdown();
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);  // pending Auto-Advance verwerfen
    // 0 Herzen = Lauf vorbei. Das tödliche Puzzle darf per Retry nochmal gespielt werden,
    // aber ein gelöstes Retry belebt den Run NICHT wieder (kein Weiterspielen mit 0 Herzen).
    if (this.run.lives() <= 0) {
      this.run.finish('GAME_OVER');
      return;
    }
    this.run.advance();
  }

  continueAfterWrong(): void {
    this.reviewMode = false;
    this.reviewingWrongPuzzle = false;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    if (this.run.lives() <= 0) {
      this.run.finish('GAME_OVER');
    } else {
      // Gauntlet: ein Fehler kostet ein Leben UND rückt zum nächsten (höheren) Ketten-Puzzle.
      this.run.advance();
    }
  }

  /** Aktuelles Puzzle (z.B. nach dem Aufgeben) im Analysemodus öffnen. */
  analyzeCurrentPuzzle(): void {
    if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; }
    if (!this.puzzle) return;

    if ((this.state === 'FAILED' || this.reviewingWrongPuzzle) && this.run.lives() <= 0) {
      // Letztes Herz verloren → Run ist vorbei. Session jetzt aufzeichnen, Snapshot sichern
      // und nach der Analyse direkt zum Zusammenfassungs-Screen zurückkehren (gameover=1).
      this.run.finish('GAME_OVER');
      EndlessSessionRecorder.saveGameOverSnapshot(this.run.gameOverSnapshot());
      this.router.navigate(['/analysis'], {
        queryParams: {
          fen: this.puzzle.fen,
          moves: this.puzzle.moves.split(' ').filter(m => m).join(','),
          orientation: this.orientation,
          from: '/puzzles/endless?gameover=1',
        },
      });
      return;
    }

    // Kein Vorrücken hier: hat das Puzzle ein Ergebnis (gelöst ODER gescheitert), steht der
    // gespeicherte Zeiger schon auf dem nächsten (puzzleSolved/loseLife/resetPuzzle), und resume=1
    // lädt nicht dasselbe Puzzle erneut. Früher rückte nur dieser Weg vor — und nur für FAILED; ein
    // gelöstes Puzzle kam nach der Analyse ein zweites Mal (doppelt gezählt), und der hier im
    // Speicher vorgerückte Index nahm die Lösezeit des alten Puzzles ins nächste mit.
    this.router.navigate(['/analysis'], {
      queryParams: {
        fen: this.puzzle.fen,
        moves: this.puzzle.moves.split(' ').filter(m => m).join(','),
        orientation: this.orientation,
        from: '/puzzles/endless?resume=1',   // Rückkehr setzt den laufenden Run fort (siehe ngOnInit)
      },
    });
  }

  /**
   * Das zuletzt gelöste Puzzle aufs AUFGABENBLATT — dieselbe Auswahl wie „♥ letztes Puzzle".
   * Geschickt wird die AUFGABEN-Stellung (der Gegnerzug ist eingespielt), nicht die rohe FEN.
   */
  sendLastToWorksheet(target: number | null): void {
    if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; }
    if (!this.lastSolvedFen) return;
    const item = taskItemFromPuzzle(
      {
        fen: this.lastSolvedFen, moves: this.lastSolvedMoves,
        orientation: this.lastSolvedOrientation, themes: this.lastSolvedThemes,
      },
      'Standard', { sourceId: this.lastSolvedPuzzleId },
    );
    this.worksheets.sendAndNotify(target, item ? [item] : []);
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

  override get reviewTotal(): number {
    return this.puzzle ? this.puzzle.moves.split(' ').filter(m => m).length : 0;
  }

  override reviewNext(): void { this.stopCountdown(); if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; } this.reviewGoTo(this.reviewIndex + 1); }
  override reviewPrev(): void { this.stopCountdown(); if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; } this.reviewGoTo(this.reviewIndex - 1); }

  /** Nach alternativem (eigenem) Mattweg die vom Puzzle vorgesehene Lösung von vorne durchspielen. */
  override showOriginalSolution(): void { this.stopCountdown(); if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; } this.playSolutionFromStart(); }

  protected override reviewGoTo(index: number): void {
    if (!this.puzzle) return;
    // Gemeinsamer Review-Aufbau der Basisklasse (mit FEN-Guard) statt eigener Kopie.
    this.reviewGoToCore(this.puzzle.fen, this.puzzle.moves.split(' ').filter(m => m), index);
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    if (this.state !== 'SOLVED' && this.state !== 'FAILED') return;
    if (!isBoardHotkey(e)) return;
    this.reviewArrowKey(e);
  }

  private loseLife(): void {
    if (this.run.registerLoss(this.puzzle)) {   // Fehler-Rating, Sitzungs-Eintrag, Leben −1 (einmal je Puzzle)
      this.recordAttempt(false);
      this.run.saveAfterLoss();   // Fortsetzen beim NÄCHSTEN Puzzle bzw. bei 0 Leben kein Zombie-Spielstand
    }
    this.state = 'FAILED';
    this.favoriteTracker.refresh();
    this.updateBoard();
    this.showFailureOrHoldMate();
  }

  // --- Buttons ---

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
    if (this.run.registerLoss(this.puzzle)) {
      // Buchführung IDENTISCH zum Fehlzug (loseLife): Fehler-Rating, Sitzungs-Eintrag,
      // Versuchs-Meldung und — der wichtigste Teil — das Sichern des laufenden Stands.
      // Vorher zeichnete NUR der 0-Leben-Fall auf: ein „Zurücksetzen" kostete ein Leben, das nie
      // beim Server ankam, also gab ein simples Neuladen es zurück (die Strafe war beliebig
      // wiederholbar). Ohne die Sitzungs-Einträge stimmte außerdem die Statistik des Laufs nicht,
      // und die fehlenden Fehler-Ratings verschoben die automatisch berechnete Schwelle.
      this.recordAttempt(false);
      if (this.run.lives() <= 0) {
        // 0 Lives = Run ist vorbei. Active-State (mit jetzt veralteten Werten) auf
        // dem Server loeschen, damit kein "Unfinished run | 0 lives"-Zombie zurueckbleibt.
        this.run.clearActiveGame();
        this.state = 'FAILED';
        this.updateBoard();
        this.enterSolutionReview();
        return;
      }
      this.run.syncActiveGame(this.run.chainIndex() + 1);   // Fortsetzen beim NÄCHSTEN Puzzle
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
   *  bereits eines gekostet. Auch beim letzten verlorenen Leben erlaubt: man darf das Puzzle,
   *  an dem der Run scheiterte, nochmal probieren (Lösen → Sudden-Death weiter, sonst „Weiter"
   *  führt ins Game Over). */
  retry(): void {
    if (!this.puzzle) return;
    this.currentEval = '';
    this.initialEval = '';
    this.showEval = false;
    this.setupPuzzle(this.puzzle);
  }

  onThresholdChange(): void {
    this.fasttrack.applyOverrides(this.config);
  }

  resetThreshold(which: number): void {
    this.fasttrack.reset(which === 1 ? 1 : 2, this.config);
  }

  /** `seconds` = zu wertende Lösezeit; default = gemessene Zeit (Fehlversuche), beim Lösen ggf. wegen
   *  überlanger Zeit gekappt (siehe {@link LongSolveService}). */
  private recordAttempt(solved: boolean, seconds?: number): void {
    if (!this.puzzle) return;
    const timeSpent = seconds ?? this.run.timer.puzzleSeconds();
    const log = this.moveLog.length > 0 ? JSON.stringify(this.moveLog) : undefined;
    const id = this.puzzle.id;
    const loggedIn = this.authService.isLoggedIn;
    const url = loggedIn ? `/api/puzzles/${id}/attempt` : `/api/puzzles/${id}/attempt/anonymous`;
    const body: Record<string, unknown> = {
      solved, timeSpentSeconds: timeSpent, moveLog: log ?? null, visualizationLevel: this.visualizationMode,
      evalShown: this.evalShown, vizShowCount: this.vizShowCount,
      hintsUsed: this.maxHintLevel,
      screenWidth: window.innerWidth, screenHeight: window.innerHeight,
    };
    if (!loggedIn) body['sessionId'] = this.puzzleService.ensureSessionId();
    // Offline gelöste Endless-Puzzles nicht verlieren → vormerken (Sync bei Reconnect).
    if (!navigator.onLine) { this.offlineQueue.enqueue('POST', url, body); return; }
    const obs = loggedIn
      ? this.puzzleService.recordAttempt(id, solved, timeSpent, log, this.visualizationMode, this.evalShown, this.vizShowCount, this.maxHintLevel)
      : this.puzzleService.recordAnonymousAttempt(id, solved, timeSpent, log, this.visualizationMode, this.evalShown, this.vizShowCount, this.maxHintLevel);
    obs.subscribe({ error: () => this.offlineQueue.enqueue('POST', url, body) });
  }

  // --- localStorage ---

  protected saveConfig(): void {
    // Während eines Laufs (Tiefen-Regler im Spielbildschirm) den zuletzt gesicherten Spielstand
    // mitschicken: null heißt für den Server „kein offener Lauf" (siehe EndlessRunStore.saveConfig).
    this.run.saveConfig(this.screen === 'play' && !this.run.historyView() && this.run.lives() > 0);
  }
}
