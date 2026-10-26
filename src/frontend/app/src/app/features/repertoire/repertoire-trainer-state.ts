import { Key } from 'chessground/types';
import { whiteToMove } from '../../shared/chess/fen-side';
import type { ParsedGame } from '../../shared/pgn-viewer/pgn-parser';
import type { LineReviewRequest } from './repertoire-training.service';
import type { TrainColor } from '../../shared/repertoire/repertoire-color.util';
import { RepertoireGraph } from './repertoire-tree.util';
import {
  LineMoveToken, commentParagraphs, lineChapterTitle, lineLabel, lineMoveTokens, plyComment, plyPrettyLabel,
} from './repertoire-trainer-line-view.util';

export type TrainerPhase = 'LOADING' | 'EMPTY' | 'PLAYING' | 'FEEDBACK' | 'DONE' | 'LINE_DONE' | 'LEARN_SHOW' | 'COMMENT';
export type TrainerOutcome = 'correct' | 'tolerated' | 'wrong';
export type TrainerMode = 'quiz' | 'learn';
export interface BoardMove { orig: Key; dest: Key; promotion?: string }

// Wie lange das „Correct/Tolerated"-Feedback nach einem Zug stehen bleibt, bevor automatisch
// weitergerückt wird (der User kann per Klick/Leertaste/Enter überspringen). correct: 1,5 s → 600 →
// 300 ms (der grüne Haken blitzt kurz, dann kommt zügig der Gegnerzug). Geduldet bleibt länger
// stehen (der Alternativzug soll wirken).
export const ADVANCE_MS: Record<TrainerOutcome, number> = { correct: 300, tolerated: 1500, wrong: 0 };
export const OPP_MOVE_DELAY_MS = 250;   // kurze Pause vor jedem automatischen Gegnerzug
export const LEARN_SHOW_MS = 1000;      // Zug im Learn-Modus ohne Kommentar so lange zeigen, dann zurücknehmen
export const LEARN_GAP_MS = 400;        // Learn: Pause zwischen Gegnerzug und dem Zeigen des eigenen Zugs (fühlte sich beim Wiederholen zu lang an, halbiert von 800)
// Learn-Modus: wie oft eine Linie durchgespielt werden muss, bevor sie in den Übungspool wandert
// (1× geführt lernen + 2× „durchklicken").
export const LEARN_REPEATS = 3;

const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

/** Uhr der Sitzung — im Trainer die des Browsers, in Specs eine von Hand weitergestellte. */
export interface TrainerClock {
  setTimeout(fn: () => void, ms: number): unknown;
  clearTimeout(handle: unknown): void;
}

export const browserClock: TrainerClock = {
  setTimeout: (fn, ms) => setTimeout(fn, ms),
  clearTimeout: h => clearTimeout(h as ReturnType<typeof setTimeout>),
};

/** Was die Sitzung nach außen meldet (die Komponente verdrahtet SR-Abgleich, Bewertungsvergleich, Change Detection). */
export interface TrainerSessionHooks {
  /** Anzeige geändert (OnPush: markForCheck). */
  changed(): void;
  /** Trainingsfarbe einer Linie (aus ihrem Kapitel). */
  colorOf(line: ParsedGame): TrainColor;
  /** Lern-Modus: Linie oft genug durchgespielt → in den Pool. */
  lineLearned(lineKey: string): void;
  /** Abfragen: Urteil über die Linie (fehlerfrei oder nicht). Die Fälligkeit kommt über {@link TrainerSession.showNextRepeat} zurück. */
  lineReviewed(req: LineReviewRequest): void;
  /** Fehlzug: Bewertungsvergleich Spielerzug ↔ Repertoirezug starten. */
  wrongMove(startFen: string, fenAfterPlayer: string, expectedSan: string, color: TrainColor): void;
  /** Neuversuch nach dem Fehlzug: laufenden Vergleich verwerfen. */
  retryAfterWrong(): void;
}

/**
 * Zustand und Anzeige der Trainer-Sitzung ({@link TrainerSession}): Phase, Warteschlange, Brett, Zähler und Feedback,
 * die daraus abgeleiteten Anzeige-Werte fürs Template und die Timer der laufenden Linie. Die Abläufe (Linie
 * durchspielen, urteilen, Lern-Modus) stehen in der Unterklasse.
 */
export abstract class TrainerSessionState {
  phase: TrainerPhase = 'LOADING';
  /** 'quiz' = fällige Pool-Linien abfragen; 'learn' = neue Linien durchspielen → in Pool. */
  mode: TrainerMode = 'quiz';
  /** Trainingsfarbe der AKTUELL laufenden Linie (Brett-Orientierung / „X am Zug" / Eval-Vorzeichen). */
  color: TrainColor = 'w';
  queue: ParsedGame[] = [];
  qIndex = 0;
  /** Repertoire-Graph für die geduldeten Alternativen je Stellung. */
  graph: RepertoireGraph | null = null;

  fen = START_FEN;
  dests = new Map<Key, Key[]>();
  lastMove?: [Key, Key];
  currentPly = 0;
  startFen = '';
  correct = 0;
  wrong = 0;
  currentStreak = 0;   // fortlaufende ✓/geduldet-Zug-Serie; echter Fehler setzt zurück (Mausrutscher nicht).
  bestStreak = 0;      // Session-Rekord.
  sessionUserMoves = 0;   // Zähler für Progress-Balken (grob geschätzt)

  outcome: TrainerOutcome = 'correct';
  expectedDisplay = '';
  wrongRevealed = false;
  /** Ergebnis der AKTUELLEN Linie: true, sobald ein NICHT als Mausrutscher verziehener Fehler passiert ist. */
  lineHadWrong = false;
  /** Ein offener Fehler an der aktuellen Stellung, der noch als Mausrutscher verziehen werden kann. */
  pendingWrong = false;
  /** LINE_DONE (Abfragen): wann die gerade beendete Linie erneut fällig wird (ISO); null solange offen bzw. Lernen. */
  nextRepeatAt: string | null = null;
  /** true, während der Gegnerzug automatisch gespielt wird — erlaubt einen Premove. */
  oppMoving = false;
  /** Learn-Modus: Kommentar des gerade gezeigten Zugs (hält die Anzeige, bis der User weitertippt). */
  learnComment = '';
  /** COMMENT-Phase: Kommentar eines gerade gespielten Gegnerzugs, hält bis der User „Weiter" bestätigt. */
  holdComment = '';
  /** Learn-Modus: wie oft die AKTUELLE Linie schon komplett durchgespielt wurde (bis {@link LEARN_REPEATS}). */
  learnPass = 0;
  /** LINE_DONE: true = beim „Weiter" wird DIESELBE Linie erneut gestartet (Learn-Wiederholung). */
  pendingRepeat = false;

  advanceTimer: unknown = null;
  protected oppTimer: unknown = null;
  protected learnTimer: unknown = null;

  constructor(protected readonly hooks: TrainerSessionHooks, protected readonly clock: TrainerClock = browserClock) {}

  // ===== Anzeige =====

  get isPlayable(): boolean {
    if (this.phase === 'PLAYING') return true;
    // Nach falschem Zug UND vor „Lösung zeigen": weiter spielbar (sofortiger Retry).
    return this.phase === 'FEEDBACK' && this.outcome === 'wrong' && !this.wrongRevealed;
  }

  get progressPct(): number {
    if (this.sessionUserMoves === 0) return 0;
    return Math.round(((this.correct + this.wrong) / this.sessionUserMoves) * 100);
  }

  get currentLine(): ParsedGame | undefined { return this.queue[this.qIndex]; }

  get currentLineLabel(): string { return lineLabel(this.currentLine); }

  get currentLineChapter(): string { return lineChapterTitle(this.currentLine); }

  /** Züge der aktuellen Linie fürs Template. Im Abfragen-Modus, solange der User seinen Zug noch SUCHT (PLAYING),
   *  erscheint der aktuelle Halbzug NICHT — sonst stünde die Lösung in der Zugliste. */
  get movesInLine(): LineMoveToken[] {
    const revealCurrent = this.mode === 'learn' || this.phase !== 'PLAYING';
    return lineMoveTokens(this.currentLine, this.currentPly, revealCurrent);
  }

  /** PGN-Kommentar zum AKTUELL zu spielenden Halbzug (Lern-Modus). */
  get currentComment(): string { return plyComment(this.currentLine, this.currentPly); }

  get currentCommentParagraphs(): string[] { return commentParagraphs(this.currentComment); }

  /** Menschenlesbares Label des aktuellen Halbzugs, z. B. „3. exd5" oder „2… d6". */
  get currentMovePrettyLabel(): string { return plyPrettyLabel(this.currentLine, this.currentPly); }

  get userColorName(): 'white' | 'black' { return this.color === 'w' ? 'white' : 'black'; }

  get boardTurn(): 'white' | 'black' { return whiteToMove(this.fen) ? 'white' : 'black'; }

  /** Premove erlauben, solange der GEGNER am Zug ist — während des automatischen Gegnerzugs und im kurzen
   *  Feedback nach einem richtigen/geduldeten Zug. Nur im Abfragen-Modus. */
  get premovable(): boolean {
    if (this.mode !== 'quiz') return false;
    if (this.boardTurn === this.userColorName) return false;
    if (this.oppMoving) return true;
    return this.phase === 'FEEDBACK' && this.outcome !== 'wrong';
  }

  // ===== Timer =====

  /** Alle Timer der laufenden Linie wegräumen (Weiter, Gegnerzug, Lern-Anzeige). */
  clearTimers(): void {
    this.clearAdvance(); this.clearOppTimer(); this.clearLearn();
  }

  protected clearAdvance(): void {
    if (this.advanceTimer !== null) { this.clock.clearTimeout(this.advanceTimer); this.advanceTimer = null; }
  }

  protected clearOppTimer(): void {
    if (this.oppTimer !== null) { this.clock.clearTimeout(this.oppTimer); this.oppTimer = null; }
  }

  protected clearLearn(): void {
    if (this.learnTimer !== null) { this.clock.clearTimeout(this.learnTimer); this.learnTimer = null; }
  }
}
