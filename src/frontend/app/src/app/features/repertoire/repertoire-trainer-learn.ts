import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { applyUci, tryLoadFen } from '../../shared/chess/puzzle-move.util';
import { ExpectedMove, judgeMove, resolveExpectedUci } from '../../shared/chess/line-solver';
import { destsAt } from './repertoire-trainer.util';
import { BoardMove, LEARN_SHOW_MS, TrainerSessionState } from './repertoire-trainer-state';

/**
 * Lern-Modus der Trainer-Sitzung ({@link TrainerSession}): der erwartete Zug wird vorgezeigt (mit Kommentar bis zum
 * Weitertippen, sonst {@link LEARN_SHOW_MS}), dann zurückgenommen und vom Nutzer selbst gespielt; Kommentare von
 * Gegnerzügen halten an (`COMMENT`). Den Weitergang der Linie liefert die Unterklasse
 * ({@link advanceToUserMove}/{@link finishLine}).
 */
export abstract class TrainerLearnMode extends TrainerSessionState {
  /** Das Brett der laufenden Linie — die Sitzung weist es je Linie (und bei „Lösung zeigen") neu zu. */
  protected chess = new Chess();

  /** Spielt Gegnerzüge nacheinander, bis der User dran ist — oder die Linie zu Ende ist. */
  protected abstract advanceToUserMove(): void;
  /** Linie zu Ende gespielt (Lern-Wiederholung bzw. Bewertung). */
  protected abstract finishLine(): void;

  // ===== Lern-Modus =====

  /** Kommentar eines gerade gespielten Gegnerzugs dauerhaft anzeigen (COMMENT), bis der User „Weiter" bestätigt. */
  protected enterCommentHold(comment: string): void {
    this.clearLearn();
    this.clearOppTimer();
    this.holdComment = comment;
    this.dests = new Map();
    this.phase = 'COMMENT';
    this.hooks.changed();
  }

  /** „Weiter" aus der COMMENT-Phase. */
  continueFromComment(): void {
    if (this.phase !== 'COMMENT') return;
    this.holdComment = '';
    this.advanceToUserMove();
  }

  /** Wiederholungs-Durchläufe: den erwarteten Zug NICHT vorzeigen, das Brett ist sofort spielbar. */
  protected enterLearnPlay(): void {
    this.clearLearn();
    this.fen = this.startFen;
    this.lastMove = undefined;
    this.dests = destsAt(this.startFen);
    this.learnComment = '';
    this.phase = 'PLAYING';
    this.hooks.changed();
  }

  /** Zeigt den erwarteten Zug (view-only). Ohne Kommentar nach {@link LEARN_SHOW_MS} zurücknehmen; MIT Kommentar
   *  stehen lassen, bis der User weitertippt. */
  protected enterLearnShow(): void {
    this.clearLearn();
    const line = this.queue[this.qIndex];
    const expected = line?.moves[this.currentPly];
    if (!expected) { this.finishLine(); return; }
    let fenAfter = this.startFen;
    try {
      const c = new Chess(this.startFen);
      const mv = c.move(expected.san);
      if (mv) { fenAfter = c.fen(); this.lastMove = [mv.from as Key, mv.to as Key]; }
    } catch { /* nicht spielbar → nur Text */ }
    this.fen = fenAfter;
    this.dests = new Map();
    this.expectedDisplay = expected.san;
    // Kommentar nur beim ERSTEN Durchlauf zeigen; die Wiederholungen laufen ohne Kommentar-Halt durch.
    this.learnComment = this.learnPass === 0 ? (line.comments?.[this.currentPly] || '').trim() : '';
    this.phase = 'LEARN_SHOW';
    this.hooks.changed();
    if (!this.learnComment) {
      this.learnTimer = this.clock.setTimeout(() => { this.learnTimer = null; this.learnRetract(); }, LEARN_SHOW_MS);
    }
  }

  /** Gezeigten Zug zurücknehmen → der User spielt ihn selbst. */
  protected learnRetract(): void {
    this.clearLearn();
    this.fen = this.startFen;
    this.lastMove = undefined;
    this.dests = destsAt(this.startFen);
    this.learnComment = '';
    this.phase = 'PLAYING';
    this.hooks.changed();
  }

  /** Learn-Zug: nur der gezeigte (erwartete) Zug führt weiter; falsch → Zug erneut zeigen. Keine Alternativen. */
  protected onLearnMove(ev: BoardMove): void {
    const line = this.queue[this.qIndex];
    if (!line || this.phase !== 'PLAYING' || this.currentPly >= line.moves.length) return;
    const board = tryLoadFen(this.startFen);
    if (!board) return;
    const expected: ExpectedMove = { san: line.moves[this.currentPly].san };
    const verdict = judgeMove(board, expected, [], ev.orig, ev.dest, ev.promotion);
    if (verdict === 'illegal' || verdict === 'not-your-turn') return;
    if (verdict !== 'correct') { this.enterLearnShow(); return; }   // nicht der Zug → nochmal vormachen
    const uci = resolveExpectedUci(board, expected);
    if (uci) { try { applyUci(this.chess, uci); } catch { /* Brett steht woanders */ } }
    this.fen = this.chess.fen();
    this.lastMove = [ev.orig, ev.dest];
    this.currentPly++;
    this.advanceToUserMove();
  }
}
