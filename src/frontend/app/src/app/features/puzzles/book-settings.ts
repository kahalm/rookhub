import { MatDialog } from '@angular/material/dialog';
import { PreferencesService } from '../../core/preferences.service';
import { ThemeMode, applyThemeMode } from './board-theme.util';
import { BookPuzzleDto } from './puzzle.service';
import { PuzzleSettingsDialogComponent, PuzzleSettingsDialogData, PuzzleSettingsDialogResult } from './puzzle-settings-dialog.component';

/** Was die Einstellungen (Brett, Figuren, Visualisierung, Engine-Tiefe) vom Buch-Löser brauchen. */
export interface BookSettingsHost {
  readonly prefs: PreferencesService;
  readonly dialog: MatDialog;
  readonly puzzle: BookPuzzleDto | null;
  /** Läuft gerade ein Lösungsversuch (Lös-Automat offen)? */
  readonly isSolving: boolean;
  /** Erzwingt `?anarchy=max` en passant unabhängig von der Einstellung? */
  readonly anarchyForcedByUrl: boolean;
  readonly solveModes: { followLevel(level: number): void };
  boardTheme: string;
  pieceSet: string;
  stockfishDepth: number;
  themeMode: ThemeMode;
  visualizationMode: number;
  vizArrowEnabled: boolean;
  enPassantForced: boolean;
  setupPuzzle(p: BookPuzzleDto): void;
  /** Gegnerpfeil der Visualisierung entfernen. */
  clearVizOpponentArrow(): void;
  /** Zugziele des laufenden Versuchs nachziehen. */
  updateBoard(): void;
}

/**
 * Einstellungen des Buch-Lösers (Zerlegung Z18): Startwerte aus den Präferenzen, Einstellungsdialog und das Speichern
 * je Wert. Eine geänderte Visualisierungsstufe setzt den laufenden Versuch neu auf, Brett/Figuren nicht.
 */
export class BookSettingsController {
  constructor(private readonly h: BookSettingsHost) {}

  load(): void {
    const { h } = this;
    h.boardTheme = h.prefs.boardTheme;
    h.pieceSet = h.prefs.pieceSet;
    h.themeMode = h.prefs.themeMode;
    h.stockfishDepth = h.prefs.bookStockfishDepth;
    h.visualizationMode = h.prefs.visualization;
    h.vizArrowEnabled = h.prefs.vizArrow;
  }

  /** Brett/Figuren zum Aufsetzen eines Puzzles aus dem Themenmodus ableiten (Crazy würfelt, Anarchy erzwingt e.p.). */
  applyThemeForSetup(): void {
    const { h } = this;
    const applied = applyThemeMode(h.themeMode, h.prefs.boardTheme, h.prefs.pieceSet);
    h.boardTheme = applied.boardTheme;
    h.pieceSet = applied.pieceSet;
    h.enPassantForced = h.anarchyForcedByUrl || (h.themeMode === 'crazy' && h.prefs.enPassantForced);
  }

  setVisualizationLevel(level: number): void {
    const { h } = this;
    h.visualizationMode = level;
    h.prefs.setVisualization(level);
    h.solveModes.followLevel(level);   // gemerkte Spielweise mitziehen
    // Modus-Wechsel = laufendes Puzzle neu starten; nach Gelöst/Gescheitert gilt die Stufe ab dem
    // nächsten Puzzle (ein Neuaufsetzen spränge aus der Durchsicht zurück ins Setup).
    if (h.puzzle && h.isSolving) h.setupPuzzle(h.puzzle);
  }

  setVizArrowEnabled(val: boolean): void {
    this.h.vizArrowEnabled = val;
    if (!val) this.h.clearVizOpponentArrow();
    this.h.prefs.setVizArrow(val);
  }

  saveConfig(): void {
    this.h.prefs.setBookStockfishDepth(this.h.stockfishDepth);
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
    const { h } = this;
    h.themeMode = mode;
    h.prefs.setThemeMode(mode);
    const applied = applyThemeMode(mode, h.prefs.boardTheme, h.prefs.pieceSet);
    h.boardTheme = applied.boardTheme;
    h.pieceSet = applied.pieceSet;
  }

  openDialog(): void {
    const { h } = this;
    const ref = h.dialog.open(PuzzleSettingsDialogComponent, {
      data: {
        mode: 'book',
        boardTheme: h.prefs.boardTheme,
        pieceSet: h.prefs.pieceSet,
        themeMode: h.themeMode,
        visualizationMode: h.visualizationMode,
        vizArrowEnabled: h.vizArrowEnabled,
        stockfishDepth: h.stockfishDepth,
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
        h.prefs.setBookStockfishDepth(h.stockfishDepth);
      }
      if (result.offPathWarnMoves !== undefined) h.prefs.setOffPathWarnMoves(result.offPathWarnMoves);
      if (result.enPassantForced !== undefined) {
        h.prefs.setEnPassantForced(result.enPassantForced);
        // Der Link-Zwang (?anarchy=max) gilt unabhängig von der Einstellung (wie in applyThemeForSetup).
        h.enPassantForced = h.anarchyForcedByUrl || (h.themeMode === 'crazy' && result.enPassantForced);
        if (h.isSolving) h.updateBoard();   // Zugziele des laufenden Versuchs nachziehen
      }
    });
  }
}
