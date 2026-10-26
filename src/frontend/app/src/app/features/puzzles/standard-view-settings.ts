import { MatDialog } from '@angular/material/dialog';
import { TranslateService } from '@ngx-translate/core';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { SolveMode, SolveModeService } from '../../core/solve-mode.service';
import { ThemeMode, applyThemeMode } from './board-theme.util';
import { DIFFICULTY_OFFSET } from './puzzle-window.util';
import { PuzzleDto, PuzzleService, PuzzleStatsDto } from './puzzle.service';
import { PuzzleSettingsDialogComponent, PuzzleSettingsDialogData, PuzzleSettingsDialogResult } from './puzzle-settings-dialog.component';

/** Bereichs-Schlüssel der Spielweise (SolveModeService) für die Standard-Puzzles. */
const SOLVE_SCOPE = 'puzzles';

/** Schwierigkeit der Standard-Puzzles (Offset des Rating-Fensters, siehe `DIFFICULTY_OFFSET`). */
export type PuzzleDifficulty = 'sehr_leicht' | 'leicht' | 'normal' | 'schwer' | 'sehr_schwer';

/** Was Ansicht, Filter-Einstellungen und Spielweise vom Standard-Löser brauchen. */
export interface StandardViewHost {
  readonly prefs: PreferencesService;
  readonly dialog: MatDialog;
  readonly solveMode: SolveModeService;
  readonly snackbar: SnackbarService;
  readonly translate: TranslateService;
  readonly puzzleService: PuzzleService;
  readonly puzzle: PuzzleDto | null;
  /** Läuft gerade ein Lösungsversuch (Lös-Automat offen)? */
  readonly isSolving: boolean;
  readonly isLoggedIn: boolean;
  /** Erzwingt `?anarchy=max` en passant unabhängig von der Einstellung? */
  readonly anarchyForcedByUrl: boolean;
  boardTheme: string;
  pieceSet: string;
  themeMode: ThemeMode;
  visualizationMode: number;
  vizArrowEnabled: boolean;
  enPassantForced: boolean;
  stockfishDepth: number;
  difficulty: PuzzleDifficulty;
  excludeSolved: boolean;
  worstTagsEnabled: boolean;
  stats: PuzzleStatsDto | null;
  awaitingSolveMode: boolean;
  setupPuzzle(p: PuzzleDto): void;
  clearVizOpponentArrow(): void;
  updateBoard(): void;
  /** Puzzle-Filter geändert (Vorab/Pool verwerfen; `loadNow` = gleich ein neues Puzzle). */
  onPuzzleFilterChange(loadNow: boolean): void;
}

/**
 * Ansicht, Filter-Einstellungen und Spielweise des Standard-Lösers: Brett/Figuren/Thema, Visualisierung,
 * Gegnerpfeil, Einstellungs-Dialog (samt Schwierigkeit, „Gelöste ausschließen", schwächste Themen) und die
 * Spielweise (Training/Einfach) im Bereich „puzzles" — Muster wie `EndlessViewSettings`.
 */
export class StandardViewSettings {
  /** Gewählte Spielweise für den Bereich „Puzzles" (Training/Einfach). `null` = es wurde bewusst
   *  nicht gefragt (fester Ansichts-Link, geteiltes Einzel-Puzzle, Challenge/Revanche) — dann
   *  bleibt es bei der Stufe aus den Einstellungen bzw. aus der URL, und das ⋮-Menü zeigt keinen
   *  Umschalter. */
  solveModeChoice: SolveMode | null = null;
  /** True, wenn in diesem Aufruf nicht nach der Spielweise gefragt werden darf (siehe oben). */
  askSuppressed = false;

  constructor(private readonly h: StandardViewHost) {}

  /** Gemerkte Einstellungen übernehmen (beim Erzeugen der Komponente). */
  loadConfig(): void {
    const h = this.h;
    h.boardTheme = h.prefs.boardTheme;
    h.pieceSet = h.prefs.pieceSet;
    h.themeMode = h.prefs.themeMode;
    h.stockfishDepth = h.prefs.stockfishDepth;
    h.visualizationMode = h.prefs.visualization;
    h.vizArrowEnabled = h.prefs.vizArrow;
    const d = h.prefs.puzzleDifficulty;
    if (d && d in DIFFICULTY_OFFSET) h.difficulty = d as PuzzleDifficulty;
    h.worstTagsEnabled = h.prefs.puzzleWorstTags;
    h.excludeSolved = h.prefs.puzzleExcludeSolved;
  }

  /** Vor jedem Puzzle-Setup: Thema auf Brett/Figuren anwenden, e.p.-Zwang (Link-Zwang ?anarchy=max gilt immer). */
  applyLook(): void {
    const applied = applyThemeMode(this.h.themeMode, this.h.prefs.boardTheme, this.h.prefs.pieceSet);
    this.h.boardTheme = applied.boardTheme;
    this.h.pieceSet = applied.pieceSet;
    // e.p.-Zwang: per URL erzwungen ODER (Crazy-Brett + Einstellung an).
    this.h.enPassantForced = this.h.anarchyForcedByUrl || (this.h.themeMode === 'crazy' && this.h.prefs.enPassantForced);
  }

  setBoardTheme(theme: string): void {
    this.h.boardTheme = theme;
    this.h.prefs.setBoardTheme(theme);
  }

  setPieceSet(set: string): void {
    this.h.pieceSet = set;
    this.h.prefs.setPieceSet(set);
  }

  setThemeMode(mode: ThemeMode): void {
    this.h.themeMode = mode;
    this.h.prefs.setThemeMode(mode);
    const applied = applyThemeMode(mode, this.h.prefs.boardTheme, this.h.prefs.pieceSet);
    this.h.boardTheme = applied.boardTheme;
    this.h.pieceSet = applied.pieceSet;
  }

  setVisualizationLevel(level: number): void {
    const h = this.h;
    h.visualizationMode = level;
    h.prefs.setVisualization(level);
    // Stufe direkt gewählt → gemerkte Spielweise mitziehen, sonst widersprechen sich beide.
    this.solveModeChoice = h.solveMode.modeForLevel(level);
    h.solveMode.set(SOLVE_SCOPE, this.solveModeChoice);
    if (h.isLoggedIn) {
      h.puzzleService.getStats(level).subscribe(s => h.stats = s);
    }
    // Modus-Wechsel = laufendes Puzzle neu starten; nach Gelöst/Gescheitert gilt die Stufe ab dem
    // nächsten Puzzle (ein Neuaufsetzen spränge aus der Durchsicht zurück ins Setup).
    if (h.puzzle && h.isSolving) h.setupPuzzle(h.puzzle);
  }

  setVizArrowEnabled(val: boolean): void {
    this.h.vizArrowEnabled = val;
    if (!val) this.h.clearVizOpponentArrow();
    this.h.prefs.setVizArrow(val);
  }

  openDialog(): void {
    const h = this.h;
    const ref = h.dialog.open(PuzzleSettingsDialogComponent, {
      data: {
        mode: 'standard',
        boardTheme: h.prefs.boardTheme,
        pieceSet: h.prefs.pieceSet,
        themeMode: h.themeMode,
        visualizationMode: h.visualizationMode,
        vizArrowEnabled: h.vizArrowEnabled,
        stockfishDepth: h.stockfishDepth,
        difficulty: h.difficulty,
        excludeSolved: h.excludeSolved,
        worstTags: h.worstTagsEnabled,
        isLoggedIn: h.isLoggedIn,
        puzzleElo: h.stats?.puzzleElo,
        offPathWarnMoves: h.prefs.offPathWarnMoves,
        enPassantForced: h.prefs.enPassantForced,
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
      if (result.visualizationMode !== h.visualizationMode) this.setVisualizationLevel(result.visualizationMode);
      this.setVizArrowEnabled(result.vizArrowEnabled);
      if (result.stockfishDepth !== undefined) {
        h.stockfishDepth = result.stockfishDepth;
        h.prefs.setStockfishDepth(h.stockfishDepth);
      }
      this.applyFilterResult(result);
      if (result.offPathWarnMoves !== undefined) h.prefs.setOffPathWarnMoves(result.offPathWarnMoves);
      if (result.enPassantForced !== undefined) {
        h.prefs.setEnPassantForced(result.enPassantForced);
        // Der Link-Zwang (?anarchy=max) gilt unabhängig von der Einstellung (wie in applyLook).
        h.enPassantForced = h.anarchyForcedByUrl || (h.themeMode === 'crazy' && result.enPassantForced);
        if (h.isSolving) h.updateBoard();   // Zugziele des laufenden Versuchs nachziehen
      }
    });
  }

  /** Puzzle-Filter: erst ALLE neuen Werte setzen und merken, dann EIN gemeinsamer Neuaufbau — sonst fragt
   *  ein früherer Block (z. B. Schwierigkeit) Puzzle/Offline-Pool noch mit dem alten Wert eines späteren an. */
  private applyFilterResult(result: PuzzleSettingsDialogResult): void {
    const h = this.h;
    const difficultyChanged = result.difficulty !== undefined && result.difficulty !== h.difficulty;
    const excludeSolvedChanged = result.excludeSolved !== undefined && result.excludeSolved !== h.excludeSolved;
    const worstTagsChanged = result.worstTags !== undefined && result.worstTags !== h.worstTagsEnabled;
    if (difficultyChanged) {
      h.difficulty = result.difficulty as PuzzleDifficulty;
      h.prefs.setPuzzleDifficulty(h.difficulty);
    }
    if (excludeSolvedChanged) {
      h.excludeSolved = !!result.excludeSolved;
      h.prefs.setPuzzleExcludeSolved(h.excludeSolved);
    }
    if (worstTagsChanged) {
      h.worstTagsEnabled = !!result.worstTags;
      h.prefs.setPuzzleWorstTags(h.worstTagsEnabled);
    }
    // Neue Schwierigkeit/Themen laden sofort ein neues Puzzle; „Gelöste ausschließen" allein lässt das laufende stehen.
    if (difficultyChanged || excludeSolvedChanged || worstTagsChanged) {
      h.onPuzzleFilterChange(difficultyChanged || worstTagsChanged);
    }
  }

  // ===== Spielweise (Training/Einfach) =====

  /** Fragt beim ersten Einstieg in den Bereich nach der Spielweise; danach kommt die gemerkte
   *  Wahl ohne Dialog (SolveModeService). Das erste Puzzle steht evtl. schon → mit der frisch
   *  gewählten Spielweise neu aufsetzen (wie im Wochenpost). */
  askSolveMode(): void {
    if (this.askSuppressed) return;
    const h = this.h;
    // Bis zur Wahl wird ein ankommendes Puzzle nur GEZEIGT, nicht aufgesetzt (loadNext) — sonst liefe die
    // Uhr hinter dem Dialog und der Gegnerzug wäre schon gespielt. Mit gemerkter Wahl antwortet ensure
    // synchron, das Flag fällt also sofort wieder.
    h.awaitingSolveMode = true;
    h.solveMode.ensure(SOLVE_SCOPE, { scopeLabel: h.translate.instant('solveMode.scope.puzzles') })
      .subscribe(mode => {
        h.awaitingSolveMode = false;
        const vorher = h.visualizationMode;
        this.solveModeChoice = mode;
        this.applySolveModeLevel();
        // Elo/Statistik hängen an der Visualisierungsstufe (wie in setVisualizationLevel) →
        // nur nachladen, wenn die Wahl die Stufe tatsächlich verschoben hat.
        if (h.isLoggedIn && h.visualizationMode !== vorher) {
          h.puzzleService.getStats(h.visualizationMode)
            .subscribe({ next: s => h.stats = s, error: () => {} });
        }
        if (h.puzzle) h.setupPuzzle(h.puzzle);
      });
  }

  /** Visualisierungsstufe zur gemerkten Spielweise setzen. Wird bei jedem Puzzle-Start
   *  aufgerufen, damit ein Umschalten ab dem NÄCHSTEN Puzzle greift. Ohne gemerkte Wahl
   *  (nicht gefragt) bleibt die Stufe unangetastet. */
  applySolveModeLevel(): void {
    if (!this.solveModeChoice) return;
    this.h.visualizationMode = this.h.solveMode.levelFor(this.solveModeChoice);
  }

  /** Spielweise umschalten (⋮-Menü). Wirkt ab dem NÄCHSTEN Puzzle — ein laufender Versuch darf
   *  nicht mitten im Rechnen die Regeln wechseln. */
  toggleSolveMode(): void {
    const neu: SolveMode = this.solveModeChoice === 'training' ? 'easy' : 'training';
    this.solveModeChoice = neu;
    this.h.solveMode.set(SOLVE_SCOPE, neu);
    this.h.snackbar.info(
      this.h.translate.instant(neu === 'easy' ? 'solveMode.switchedEasy' : 'solveMode.switchedTraining'),
      { duration: 3000 });
  }
}
