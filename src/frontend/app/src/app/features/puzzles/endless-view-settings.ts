import { MatDialog } from '@angular/material/dialog';
import { TranslateService } from '@ngx-translate/core';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { SolveMode, SolveModeService } from '../../core/solve-mode.service';
import { ThemeMode, applyThemeMode } from './board-theme.util';
import { PuzzleDto } from './puzzle.service';
import { PuzzleSettingsDialogComponent, PuzzleSettingsDialogData, PuzzleSettingsDialogResult } from './puzzle-settings-dialog.component';

/** Bereichs-Schlüssel der Spielweise (SolveModeService) für den Endless-Modus. */
const SOLVE_SCOPE = 'endless';

/** Was Ansicht und Spielweise vom Endlos-Löser brauchen. */
export interface EndlessViewHost {
  readonly prefs: PreferencesService;
  readonly dialog: MatDialog;
  readonly solveMode: SolveModeService;
  readonly snackbar: SnackbarService;
  readonly translate: TranslateService;
  readonly puzzle: PuzzleDto | null;
  /** Läuft gerade ein Lösungsversuch (Lös-Automat offen)? */
  readonly isSolving: boolean;
  /** Erzwingt `?anarchy=max` en passant unabhängig von der Einstellung? */
  readonly anarchyForcedByUrl: boolean;
  boardTheme: string;
  pieceSet: string;
  themeMode: ThemeMode;
  visualizationMode: number;
  vizArrowEnabled: boolean;
  enPassantForced: boolean;
  setupPuzzle(p: PuzzleDto): void;
  clearVizOpponentArrow(): void;
  updateBoard(): void;
}

/**
 * Ansicht und Spielweise des Endlos-Lösers (Zerlegung A4): Brett/Figuren/Thema, Visualisierung, Gegnerpfeil,
 * Einstellungs-Dialog und die Spielweise (Training/Einfach) im Bereich „endless" — Muster wie `BookSettingsController`.
 */
export class EndlessViewSettings {
  /** Gewählte Spielweise für den Bereich „Endless" (Training/Einfach). `null` = es wurde bewusst
   *  nicht gefragt (fester Ansichts-Link ?visualmode=) — dann bleibt es bei der Stufe aus den
   *  Einstellungen bzw. aus der URL, und das ⋮-Menü zeigt keinen Umschalter. */
  solveModeChoice: SolveMode | null = null;
  /** True, wenn in diesem Aufruf nicht nach der Spielweise gefragt werden darf (siehe oben). */
  askSuppressed = false;

  constructor(private readonly h: EndlessViewHost) {}

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
    this.h.visualizationMode = level;
    this.h.prefs.setVisualization(level);
    // Stufe direkt gewählt → gemerkte Spielweise mitziehen, sonst widersprechen sich beide.
    this.solveModeChoice = this.h.solveMode.modeForLevel(level);
    this.h.solveMode.set(SOLVE_SCOPE, this.solveModeChoice);
    if (this.h.puzzle && this.h.isSolving) this.h.setupPuzzle(this.h.puzzle);  // laufendes Puzzle neu starten
  }

  setVizArrowEnabled(val: boolean): void {
    this.h.vizArrowEnabled = val;
    if (!val) this.h.clearVizOpponentArrow();
    this.h.prefs.setVizArrow(val);
  }

  /** Vor jedem Puzzle-Setup: Thema auf Brett/Figuren anwenden, e.p.-Zwang (Link-Zwang ?anarchy=max gilt immer). */
  applyLook(): void {
    const applied = applyThemeMode(this.h.themeMode, this.h.prefs.boardTheme, this.h.prefs.pieceSet);
    this.h.boardTheme = applied.boardTheme;
    this.h.pieceSet = applied.pieceSet;
    this.h.enPassantForced = this.h.anarchyForcedByUrl || (this.h.themeMode === 'crazy' && this.h.prefs.enPassantForced);
  }

  openDialog(): void {
    const ref = this.h.dialog.open(PuzzleSettingsDialogComponent, {
      data: {
        mode: 'endless',
        boardTheme: this.h.prefs.boardTheme,
        pieceSet: this.h.prefs.pieceSet,
        themeMode: this.h.themeMode,
        visualizationMode: this.h.visualizationMode,
        vizArrowEnabled: this.h.vizArrowEnabled,
        offPathWarnMoves: this.h.prefs.offPathWarnMoves,
        enPassantForced: this.h.prefs.enPassantForced,
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
      if (result.visualizationMode !== this.h.visualizationMode) this.setVisualizationLevel(result.visualizationMode);
      this.setVizArrowEnabled(result.vizArrowEnabled);
      if (result.offPathWarnMoves !== undefined) this.h.prefs.setOffPathWarnMoves(result.offPathWarnMoves);
      if (result.enPassantForced !== undefined) {
        this.h.prefs.setEnPassantForced(result.enPassantForced);
        // Der Link-Zwang (?anarchy=max) gilt unabhängig von der Einstellung (wie in applyLook).
        this.h.enPassantForced = this.h.anarchyForcedByUrl || (this.h.themeMode === 'crazy' && result.enPassantForced);
        if (this.h.isSolving) this.h.updateBoard();   // Zugziele des laufenden Versuchs nachziehen
      }
    });
  }

  // ===== Spielweise (Training/Einfach) =====

  /** Fragt beim ERSTEN Lauf nach der Spielweise; danach gilt die gemerkte Wahl ohne Dialog
   *  (SolveModeService). Ein Endless-Lauf ist eine Einheit → gefragt wird beim Start/Fortsetzen
   *  eines Laufs, nicht bei jedem Puzzle darin. Steht das erste Puzzle schon, wird es mit der
   *  frisch gewählten Spielweise neu aufgesetzt. */
  askSolveMode(): void {
    if (this.askSuppressed) return;
    this.h.solveMode.ensure(SOLVE_SCOPE, { scopeLabel: this.h.translate.instant('solveMode.scope.endless') })
      .subscribe(mode => {
        this.solveModeChoice = mode;
        this.applySolveModeLevel();
        if (this.h.puzzle && this.h.isSolving) this.h.setupPuzzle(this.h.puzzle);
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
