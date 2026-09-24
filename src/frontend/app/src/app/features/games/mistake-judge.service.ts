import { Injectable, inject } from '@angular/core';
import { Chess } from 'chess.js';
import { StockfishService } from '../puzzles/stockfish.service';
import { fenAfterUci } from '../../shared/pgn-viewer/board-moves.util';
import { EvalScore } from './game-review.util';
import { Mistake, isEquivalentAfter } from './mistakes.util';

/**
 * Prüft im Fehler-Trainer einen Zug, den die Partie-Analyse NICHT unter ihren Kandidaten führt, mit der
 * Browser-Engine (Stockfish WASM, dieselbe wie im Puzzle-Solver). Gefragt wird nur, wenn schon der
 * schwächste Kandidat gleichwertig war (`Mistake.checkUnlisted`) — sonst ist jeder andere Zug sicher
 * schlechter, und es wäre Rechenzeit für eine bekannte Antwort.
 *
 * Gerechnet werden ZWEI Stellungen in derselben Tiefe: nach dem Bestzug der Analyse und nach dem eigenen
 * Zug. Die Server-Analyse lief tiefer (20–30), der Browser-Kern schafft das nicht in vertretbarer Zeit —
 * verglichen wird deshalb Browser gegen Browser.
 */
@Injectable({ providedIn: 'root' })
export class MistakeJudgeService {
  /** Tiefe beider Suchen — die Vorgabe des Puzzle-Solvers; die Suche selbst bricht nach 10 s ab. */
  static readonly DEPTH = 16;

  private readonly stockfish = inject(StockfishService);

  /** `true` gleichwertig · `false` schlechter · `null` nicht zu prüfen (Engine-Fehler, unlesbare Stellung). */
  async judge(m: Mistake, fenAfterUser: string): Promise<boolean | null> {
    const fenAfterBest = fenAfterUci(m.fenBefore, m.bestUci);
    if (!fenAfterBest || !fenAfterUser) return null;
    try {
      const best = await this.scoreOf(fenAfterBest);
      const user = await this.scoreOf(fenAfterUser);
      if (!best || !user) return null;
      return isEquivalentAfter({ score: best, fen: fenAfterBest }, { score: user, fen: fenAfterUser }, m.white);
    } catch {
      return null;
    }
  }

  /** Bewertung einer Stellung in Weiß-Sicht. Ist die Partie dort vorbei, gibt es keinen Zug zu suchen —
   *  die Engine meldete „No move found"; Matt und Remis stehen ohnehin fest. */
  private async scoreOf(fen: string): Promise<EvalScore | null> {
    let chess: Chess;
    try { chess = new Chess(fen); } catch { return null; }
    if (chess.isCheckmate()) return { mate: 0 };
    if (chess.isDraw()) return { cp: 0 };
    const r = await this.stockfish.getBestMove(fen, MistakeJudgeService.DEPTH);
    return r.score ? { cp: r.score.cp ?? null, mate: r.score.mate ?? null } : null;
  }
}
