import { Chess } from 'chess.js';
import { Color, Key } from 'chessground/types';
import { applyUci, tryFreeMove, calcDests, formatSanList, formatSanListHtml } from './puzzle-move.util';
import { applyVisualizationHide, CrazyPieceMode, ThemeMode } from './board-theme.util';
import { OpponentArrowMode, opponentArrowModeFor, VizController } from './viz-controller';
import { previewPosition } from './solver-setup.util';

/** Zustände, die der Lös-Automat selbst setzt; die Endzustände (SOLVED/FAILED/…) bringt jede Komponente als
 *  Typ-Parameter mit ({@link BasePuzzleSolver}). */
export type SolverCoreState = 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE' | 'THINKING' | 'PLAYING';

/**
 * Unterste Schicht des Lös-Automaten ({@link BasePuzzleSolver}): Brett-Präsentation (FEN, Zugziele, Schach,
 * Blickrichtung), der chess.js-Zustand samt Lösungszügen und der Visualisierungs-/Blindfold-Modus.
 * Tipps/Eval/Stoppuhr liegen in {@link SolverAssistBase}, Durchsicht/Countdown in {@link SolverReviewBase},
 * die Zug-Bewertung in {@link BasePuzzleSolver}.
 */
export abstract class SolverBoardBase<S extends string = never> {
  // ---- Brett-Präsentation (von Templates gelesen) ----
  boardFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  /**
   * Tatsächlicher chess.js-Zustand (im Viz-Modus weicht boardFen ab — dort bleibt das Brett
   * auf frozenFen). Das Brett-Component nutzt das fürs Erkennen von Promotion-Zügen und
   * für die Legalitäts-Prüfung im Viz-Klick-Modus.
   */
  actualFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  orientation: Color = 'white';
  turnColor: Color = 'white';
  dests: Map<Key, Key[]> = new Map();
  /** Anarchy-Modus (`?anarchy=max`): erzwingt En passant, wenn verfügbar (schränkt `dests` ein). */
  enPassantForced = false;
  /** Crazy-Figuren-Modus: 'piece' = je Figur eigenes Set (Default), 'square' = das Feld bestimmt
   *  den Stil (`?anarchy=max+1`). Wird ans `app-puzzle-board` durchgereicht. */
  crazyPieceMode: CrazyPieceMode = 'piece';
  /** Brett-Themen-Modus (fixed/crazy/…), aus den Prefs bzw. Share-View-Params gesetzt. Steuert u.a.
   *  {@link opponentArrowMode} (crazy → bleibender Gegnerzug-Pfeil). Geteilt über alle Modi. */
  themeMode: ThemeMode = 'fixed';
  lastMove?: [Key, Key];
  isCheck = false;

  /** Gemeinsamer Zustand; Endzustände (SOLVED/FAILED/...) setzt die Komponente (Typ-Parameter `S`). */
  state: SolverCoreState | S = 'LOADING';
  /** Die Komponente wartet auf die Spielweise-Wahl (blockierender Dialog vor dem ersten Puzzle eines
   *  Bereichs): die Aufgabe steht schon auf dem Brett ({@link previewSetup}), aber ohne Uhr und Setup-Zug,
   *  und die Status-Karte bittet um die Wahl statt „lädt…" zu zeigen. */
  awaitingSolveMode = false;

  /** Anarchy per URL erzwungen (`?anarchy=max`): e.p.-Zwang gilt dann unabhängig von der Einstellung.
   *  Ohne URL-Zwang folgt der e.p.-Zwang im Crazy-Brett der Einstellung `prefs.enPassantForced`. */
  anarchyForcedByUrl = false;

  // ---- Lösungszüge + chess.js-Zustand ----
  protected chess = new Chess();
  protected solutionMoves: string[] = [];
  /** Index des nächsten erwarteten Zugs in `solutionMoves` (von Hilfsklassen der Modi gelesen, nur die Basis schreibt). */
  moveIndex = 0;
  /** Vorspiel-Halbzug (siehe {@link setupSolver}); gelesen wie {@link moveIndex}. */
  startPly = 0;

  // ---- Visualisierungs-/Blindfold-Modus (0 = aus, 1-4 = aktiv) ----
  visualizationMode = 0;
  vizMoves: string[] = [];
  protected frozenFen = '';
  protected vizStartWhite = true;
  protected vizStartNum = 1;
  /** Verdeck-Countdown, „Anzeigen"-Knopf und Gegnerzug-Pfeil (verdeckt wird mit der Stufe beim Ablauf). */
  protected readonly viz = new VizController(() => applyVisualizationHide(this.visualizationMode));
  /** Level 2-4: Figuren versteckt (nach Countdown). */
  get vizPiecesHidden(): boolean { return this.viz.piecesHidden; }
  /** Show-Button aktiv (deckt Figuren/Steine auf — Toggle, läuft NICHT automatisch ab). */
  get vizShowPressed(): boolean { return this.viz.showPressed; }
  /** Wie oft der Show-Button in diesem Versuch zum Aufdecken gedrückt wurde. */
  get vizShowCount(): number { return this.viz.showCount; }
  /** Countdown-Sekunden bis Figuren verschwinden (Level 2-4). */
  get vizCountdownSeconds(): number { return this.viz.countdownSeconds; }
  /** Letzter Gegnerzug als Pfeil (Viz-Modus 1-4: immer nur der zuletzt gespielte Gegnerzug). */
  get vizOpponentLastMove(): [Key, Key] | undefined { return this.viz.opponentLastMove; }
  /** Eval wurde in diesem Versuch mindestens einmal eingeblendet. */
  evalShown = false;
  /** Pfeil-Anzeige ein/aus (Nutzer-Einstellung, aus Prefs geladen). */
  vizArrowEnabled = true;

  // ===== abgeleitete Helfer =====
  /** Läuft gerade ein Lösungsversuch (Lös-Automat offen)? */
  get isSolving(): boolean {
    return this.state === 'AWAITING_USER_MOVE' || this.state === 'THINKING' || this.state === 'PLAYING';
  }

  /** SAN-Zugliste (Visualisierungs-Modus) mit korrekten Zugnummern ab der Trainingsstellung. */
  get vizMoveText(): string {
    return formatSanList(this.vizMoves, this.vizStartWhite, this.vizStartNum);
  }

  /** Wie `vizMoveText`, aber Gegnerzüge in `<strong>` (für die [innerHTML]-Bindung der Solver-Templates). */
  get vizMoveHtml(): string {
    return formatSanListHtml(this.vizMoves, this.vizStartWhite, this.vizStartNum);
  }

  // ===== Setup =====
  /**
   * Zeigt die Stellung, die {@link setupSolver} gleich aufbaut (Vorspiel bis `startPly`, Blickrichtung des
   * Lösers) — OHNE etwas zu starten: kein Setup-Zug, keine Uhr, keine Zugziele, `state` bleibt. Für die Zeit
   * hinter dem Spielweise-Dialog ({@link awaitingSolveMode}); setupSolver setzt danach alles neu auf.
   */
  protected previewSetup(fen: string, movesStr: string, startPly = 0): void {
    const p = previewPosition(fen, movesStr, startPly);
    if (p) {
      this.orientation = p.orientation;
      this.boardFen = this.actualFen = p.fen;
      this.turnColor = p.turnColor;
    } else {
      this.boardFen = this.actualFen = fen;   // chess.js lehnt die FEN ab: wenigstens die Stellung zeigen
    }
    this.lastMove = undefined;
    this.isCheck = false;
    this.dests = new Map();
  }

  // ===== Brett / Züge =====
  protected playMove(uci: string): void {
    const mv = applyUci(this.chess, uci);
    this.lastMove = [uci.substring(0, 2) as Key, uci.substring(2, 4) as Key];
    if (this.visualizationMode && mv && this.isSolving) this.vizMoves.push(mv.san);
  }

  protected playFreeMove(orig: Key, dest: Key, promotion?: string): boolean {
    const mv = tryFreeMove(this.chess, orig, dest, promotion);
    if (!mv) return false;
    this.lastMove = [orig, dest];
    if (this.visualizationMode && this.isSolving) this.vizMoves.push(mv.san);
    return true;
  }

  /** Brett-Präsentation (FEN, Zugziele, Schach) aus dem Lös-Zustand neu ableiten. */
  updateBoard(): void {
    this.actualFen = this.chess.fen();
    // Visualisierung: Brett auf der eingefrorenen Startstellung halten, solange gelöst wird;
    // am Ende (Endzustand der Komponente) das echte Brett aufdecken.
    if (this.visualizationMode && (this.isSolving || this.state === 'SETUP')) {
      // Level 1 („Blindspiel"): Beim Drücken von „Anzeigen" kurz die TATSÄCHLICHE aktuelle
      // Stellung zeigen (sonst bleibt das Brett auf der eingefrorenen Startstellung). Ab Level 2
      // deckt stattdessen CSS die Figuren auf — das Brett bleibt dort immer eingefroren.
      if (this.visualizationMode === 1 && this.vizShowPressed) {
        this.boardFen = this.actualFen;
        this.turnColor = this.chess.turn() === 'w' ? 'white' : 'black';
        this.isCheck = this.chess.isCheck();
      } else {
        this.boardFen = this.frozenFen || this.actualFen;
        this.turnColor = this.orientation;
        this.isCheck = false;
      }
      this.dests = new Map();
      return;
    }
    // Endzustand → Figuren wieder einblenden
    if (this.vizPiecesHidden) this.endVisualizationHide();
    this.boardFen = this.actualFen;
    this.turnColor = this.chess.turn() === 'w' ? 'white' : 'black';
    this.isCheck = this.chess.isCheck();
    const interactive = (this.state === 'AWAITING_USER_MOVE' || this.state === 'PLAYING') && this.turnColor === this.orientation;
    this.dests = interactive ? calcDests(this.chess, this.enPassantForced) : new Map();
  }

  // ===== Visualization Level 2-4: Countdown + Hide (VizController) =====
  /** Toggle: deckt die Figuren auf bzw. blendet sie wieder aus. Läuft bewusst NICHT automatisch
   *  ab — der Spieler entscheidet selbst, wann wieder verdeckt wird. */
  onVizShow(): void {
    this.viz.toggleShow();   // nur das Aufdecken zählt
    // Level 1: Brett auf die tatsächliche Stellung umstellen bzw. wieder einfrieren
    // (Level 2–4 decken über die CSS-Klasse `.viz-hidden` auf, kein Board-Wechsel nötig).
    if (this.visualizationMode === 1) this.updateBoard();
  }

  protected markEvalShown(): void {
    this.evalShown = true;
  }

  protected endVisualizationHide(): void {
    this.viz.endHide();
  }

  /** Wann der Gegnerzug-Pfeil gezeigt wird und wie lange (siehe {@link opponentArrowModeFor}). */
  protected get opponentArrowMode(): OpponentArrowMode {
    return opponentArrowModeFor(this.themeMode, this.visualizationMode);
  }

  /** Zeigt den letzten Gegnerzug als Pfeil — je nach {@link opponentArrowMode} zeitlich begrenzt
   *  (Viz, respektiert die Pfeil-Einstellung) oder bleibend bis zum nächsten User-Zug (Crazy). */
  protected showVizOpponentArrow(): void {
    this.viz.showOpponentArrow(this.opponentArrowMode, this.vizArrowEnabled, this.lastMove);
  }

  /** Gegnerzug-Pfeil sofort entfernen. */
  clearVizOpponentArrow(): void {
    this.viz.clearOpponentArrow();
  }
}
