import { Chess, Square } from 'chess.js';
import { Color, Key } from 'chessground/types';
import { StockfishService } from './stockfish.service';
import { applyUci, tryFreeMove, calcDests, formatSanList } from './puzzle-move.util';

export interface MoveLogEntry { i: number; uci: string; exp: string; ms: number; ok: boolean; }

/**
 * Gemeinsamer Lös-Automat für alle 3 Puzzle-Modi (Normal, Endless, Buch).
 * Kapselt chess.js-State, den Zustandsautomaten (SETUP→AWAITING→THINKING→PLAYING),
 * Lösungsvergleich, Stockfish-Übernahme bei Fehlzug, Mouseslip, Brett-Präsentation und
 * den Visualisierungs-/Blindfold-Modus. Vorher in jeder Komponente ~400 Zeilen dupliziert.
 *
 * Modus-Unterschiede laufen über Hooks: {@link handleSolved}/{@link handleFailed} (Pflicht),
 * {@link depth}, {@link stockfishErrorContinues}, {@link onSetupStart}, {@link onSolvingBegins}.
 * Die konkrete Komponente setzt am Ende ihren eigenen `state` (SOLVED/CORRECT/WRONG/...).
 */
export abstract class BasePuzzleSolver {
  // ---- Brett-Präsentation (von Templates gelesen) ----
  boardFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  orientation: Color = 'white';
  turnColor: Color = 'white';
  dests: Map<Key, Key[]> = new Map();
  lastMove?: [Key, Key];
  isCheck = false;

  /** Gemeinsamer Zustand; Endzustände (SOLVED/FAILED/CORRECT/WRONG/...) setzt die Komponente. */
  state = 'LOADING';

  onSolutionPath = true;
  alternativeSolve = false;
  mouseslipUsed = false;
  currentEval = '';

  // ---- Visualisierungs-/Blindfold-Modus ----
  visualizationMode = false;
  vizMoves: string[] = [];
  protected frozenFen = '';
  protected vizStartWhite = true;
  protected vizStartNum = 1;

  // ---- intern ----
  protected chess = new Chess();
  protected solutionMoves: string[] = [];
  protected moveIndex = 0;
  protected startPly = 0;
  protected aborted = false;
  protected autoAdvanceTimer?: ReturnType<typeof setTimeout>;
  protected moveLog: MoveLogEntry[] = [];
  protected moveStartTime = 0;

  constructor(protected stockfish: StockfishService) {}

  // ===== Hooks (von den Komponenten überschrieben) =====
  /** Puzzle gelöst — Komponente setzt Endzustand + Mode-Logik (Elo/Leben/Review/…). */
  protected abstract handleSolved(alternative: boolean): void;
  /** Puzzle verloren — Komponente setzt Endzustand + Mode-Logik. */
  protected abstract handleFailed(): void;
  /** Stockfish-Suchtiefe (Komponente liefert ihre Einstellung). */
  protected get depth(): number { return 16; }
  /** Bei Stockfish-Fehler weiterspielen (true) oder als verloren werten (false, z.B. Buch). */
  protected get stockfishErrorContinues(): boolean { return true; }
  /** Am Anfang von setupSolver (z.B. Theme-Modus anwenden). */
  protected onSetupStart(): void {}
  /** Sobald gelöst werden kann (Trainingsstellung erreicht) — z.B. Timer/initialFen. */
  protected onSolvingBegins(): void {}

  // ===== abgeleitete Helfer =====
  protected get isSolving(): boolean {
    return this.state === 'AWAITING_USER_MOVE' || this.state === 'THINKING' || this.state === 'PLAYING';
  }

  /** SAN-Zugliste (Visualisierungs-Modus) mit korrekten Zugnummern ab der Trainingsstellung. */
  get vizMoveText(): string {
    return formatSanList(this.vizMoves, this.vizStartWhite, this.vizStartNum);
  }

  // ===== Setup =====
  /**
   * Puzzle aufsetzen. `startPly`: -1 = FEN ist Trainingsstellung (lösen ab moves[0]);
   * 0 = klassisch (moves[0] Setup, lösen ab moves[1]); k = Vorspiel bis moves[k].
   */
  protected setupSolver(fen: string, movesStr: string, startPly = 0): void {
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.solutionMoves = movesStr.split(' ');
    this.startPly = startPly;
    if (this.startPly > this.solutionMoves.length - 2) this.startPly = 0;
    if (this.startPly < -1) this.startPly = -1;
    this.moveIndex = 0;
    this.chess = new Chess(fen);
    this.onSolutionPath = true;
    this.aborted = false;
    this.mouseslipUsed = false;
    this.alternativeSolve = false;
    this.moveLog = [];
    this.frozenFen = fen;
    this.vizMoves = [];
    this.onSetupStart();

    for (let i = 0; i < this.startPly; i++) applyUci(this.chess, this.solutionMoves[i]);
    this.lastMove = undefined;

    if (this.startPly < 0) {
      // FEN ist bereits die Trainingsstellung → sofort lösen, kein Setup-Zug.
      this.moveIndex = 0;
      this.orientation = this.chess.turn() === 'w' ? 'white' : 'black';
      this.beginSolving();
      this.state = 'AWAITING_USER_MOVE';
      this.onSolvingBegins();
      this.updateBoard();
      return;
    }

    const setupMove = this.solutionMoves[this.startPly];
    const piece = this.chess.get(setupMove.substring(0, 2) as Square);
    this.orientation = piece?.color === 'w' ? 'black' : 'white';
    this.updateBoard();
    this.state = 'SETUP';

    this.autoAdvanceTimer = setTimeout(() => {
      if (this.state !== 'SETUP') return;
      this.playMove(this.solutionMoves[this.startPly]);
      this.moveIndex = this.startPly + 1;
      this.beginSolving();
      this.state = 'AWAITING_USER_MOVE';
      this.onSolvingBegins();
      this.moveStartTime = Date.now();
      this.updateBoard();
    }, 600);
  }

  protected beginSolving(): void {
    this.frozenFen = this.chess.fen();
    const f = this.frozenFen.split(' ');
    this.vizStartWhite = f[1] !== 'b';
    this.vizStartNum = parseInt(f[5], 10) || 1;
    this.vizMoves = [];
  }

  // ===== Zug-Handling =====
  onMoveMade(event: { orig: Key; dest: Key; promotion?: string }): void {
    if (this.state === 'PLAYING') { this.handleOffPathMove(event); return; }
    if (this.state !== 'AWAITING_USER_MOVE') return;

    if (this.onSolutionPath) {
      const expectedUci = this.solutionMoves[this.moveIndex];
      const userUci = event.orig + event.dest + (event.promotion || '');
      const thinkMs = Date.now() - this.moveStartTime;

      if (userUci === expectedUci.substring(0, userUci.length)) {
        this.moveLog.push({ i: this.moveIndex, uci: expectedUci, exp: expectedUci, ms: thinkMs, ok: true });
        this.playMove(expectedUci);
        this.moveIndex++;
        this.advanceAfterCorrectMove();
      } else {
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

  protected handleOffPathMove(event: { orig: Key; dest: Key; promotion?: string }): void {
    if (!this.playFreeMove(event.orig, event.dest, event.promotion)) return;
    if (this.chess.isGameOver()) { this.handleGameOver(); return; }
    this.opponentRespond();
  }

  protected advanceAfterCorrectMove(): void {
    if (this.moveIndex >= this.solutionMoves.length) { this.solvedInternal(false); return; }
    this.state = 'THINKING';
    this.updateBoard();
    this.autoAdvanceTimer = setTimeout(() => {
      if (this.aborted) return;
      this.playMove(this.solutionMoves[this.moveIndex]);
      this.moveIndex++;
      this.updateBoard();
      if (this.moveIndex >= this.solutionMoves.length) { this.solvedInternal(false); return; }
      this.state = 'PLAYING';
      this.moveStartTime = Date.now();
      this.updateBoard();
    }, 400);
  }

  protected async opponentRespond(): Promise<void> {
    this.state = 'THINKING';
    this.updateBoard();
    try {
      const result = await this.stockfish.getBestMove(this.chess.fen(), this.depth);
      if (this.aborted) return;
      this.currentEval = result.eval;
      this.playMove(result.move);
      this.updateBoard();
      if (this.chess.isGameOver()) { this.handleGameOver(); return; }
      this.autoAdvanceTimer = setTimeout(() => {
        if (this.aborted) return;
        this.state = 'PLAYING';
        this.moveStartTime = Date.now();
        this.updateBoard();
      }, 400);
    } catch {
      if (this.aborted) return;
      if (this.stockfishErrorContinues) {
        this.state = 'PLAYING';
        this.moveStartTime = Date.now();
        this.updateBoard();
      } else {
        this.handleFailed();
      }
    }
  }

  protected handleGameOver(): void {
    if (this.chess.isCheckmate()) {
      const loserColor = this.chess.turn();
      const userColor = this.orientation === 'white' ? 'w' : 'b';
      if (loserColor !== userColor) { this.solvedInternal(true); return; }
    }
    this.handleFailed();
  }

  private solvedInternal(alternative: boolean): void {
    this.alternativeSolve = alternative;
    this.handleSolved(alternative);
  }

  mouseslip(): void {
    if (this.mouseslipUsed || this.onSolutionPath) return;
    this.mouseslipUsed = true;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    if (this.state === 'PLAYING') { this.chess.undo(); this.chess.undo(); }
    else { this.chess.undo(); }
    this.aborted = false;
    this.state = 'PLAYING';
    this.updateBoard();
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

  protected updateBoard(): void {
    // Visualisierung: Brett auf der eingefrorenen Startstellung halten, solange gelöst wird;
    // am Ende (Endzustand der Komponente) das echte Brett aufdecken.
    if (this.visualizationMode && (this.isSolving || this.state === 'SETUP')) {
      this.boardFen = this.frozenFen || this.chess.fen();
      this.turnColor = this.orientation;
      this.isCheck = false;
      this.dests = new Map();
      return;
    }
    this.boardFen = this.chess.fen();
    this.turnColor = this.chess.turn() === 'w' ? 'white' : 'black';
    this.isCheck = this.chess.isCheck();
    const interactive = (this.state === 'AWAITING_USER_MOVE' || this.state === 'PLAYING') && this.turnColor === this.orientation;
    this.dests = interactive ? calcDests(this.chess) : new Map();
  }

  /** Aufräumen (Timer) — Komponente ruft dies in ngOnDestroy/reset. */
  protected abortSolver(): void {
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
  }
}
