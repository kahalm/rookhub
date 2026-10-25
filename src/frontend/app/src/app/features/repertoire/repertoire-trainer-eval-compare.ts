import { Chess } from 'chess.js';
import { parseWhiteEval } from './repertoire-eval.util';
import type { TrainColor } from '../../shared/repertoire/repertoire-color.util';

/** Woher die Bewertungen kommen (im Trainer: `StockfishService`). */
export interface EvalSource {
  getEval(fen: string, depth: number): Promise<string>;
}

export type EvalMateNote = 'missed' | 'allowed';

/** Suchtiefe des Vergleichs Spielerzug ↔ Repertoirezug. */
export const EVAL_COMPARE_DEPTH = 14;

/**
 * Vergleich zweier Weiß-Bewertungen aus Sicht der Trainingsfarbe: Unterschied in Bauern (negativ = der Spielerzug ist
 * schlechter) und ob dabei ein Matt verpasst bzw. erlaubt wurde. Rein, ohne Engine.
 */
export function compareEvals(sPlayer: string, sRep: string, color: TrainColor): { deltaPawns: number; mateNote: EvalMateNote | null } {
  const ep = parseWhiteEval(sPlayer);
  const er = parseWhiteEval(sRep);
  const sign = color === 'w' ? 1 : -1;
  const playerWasInWin = er.mateFor && (er.mateFor === color);
  const playerNowLosing = ep.mateFor && (ep.mateFor !== color);
  let mateNote: EvalMateNote | null = null;
  if (playerNowLosing) mateNote = 'allowed';
  else if (playerWasInWin && !ep.mateFor) mateNote = 'missed';
  return { deltaPawns: (ep.cp * sign - er.cp * sign) / 100, mateNote };
}

/**
 * Der Bewertungsvergleich nach einem Fehlzug im Repertoire-Trainer (Zerlegung Z21): rechnet die Stellung nach dem
 * Spielerzug und nach dem Repertoirezug und hält das Ergebnis für die Anzeige. Ein neuer Vergleich oder {@link reset}
 * macht ein noch ausstehendes Ergebnis ungültig (Epoche). Ohne Angular; `changed` meldet jede Änderung der Anzeige.
 */
export class EvalCompare {
  loading = false;
  deltaPawns: number | null = null;
  mateNote: EvalMateNote | null = null;
  private epoch = 0;

  constructor(private readonly source: EvalSource, private readonly changed: () => void) {}

  /** Anzeige leeren und einen laufenden Vergleich verwerfen (Neuversuch nach dem Fehlzug). */
  reset(): void {
    this.loading = false; this.deltaPawns = null; this.mateNote = null; this.epoch++;
  }

  /** Betrag des Unterschieds ohne überflüssige Nullen („0.5“, „1.25“); leer ohne Ergebnis. */
  get deltaAbsDisplay(): string {
    if (this.deltaPawns === null) return '';
    return Math.abs(this.deltaPawns).toFixed(2).replace(/\.?0+$/, '');
  }

  /** Vergleich starten: `startFen` + `expectedSan` ergibt die Stellung nach dem Repertoirezug. */
  start(startFen: string, fenAfterPlayer: string, expectedSan: string, color: TrainColor): void {
    const epoch = ++this.epoch;
    this.loading = true;
    this.deltaPawns = null;
    this.mateNote = null;

    let fenAfterRep = '';
    try {
      const c = new Chess(startFen);
      c.move(expectedSan);
      fenAfterRep = c.fen();
    } catch { this.loading = false; this.changed(); return; }

    Promise.all([
      this.source.getEval(fenAfterPlayer, EVAL_COMPARE_DEPTH).catch(() => ''),
      this.source.getEval(fenAfterRep, EVAL_COMPARE_DEPTH).catch(() => ''),
    ]).then(([sPlayer, sRep]) => {
      if (epoch !== this.epoch) return;
      this.loading = false;
      if (!sPlayer || !sRep) { this.changed(); return; }
      const r = compareEvals(sPlayer, sRep, color);
      this.mateNote = r.mateNote;
      this.deltaPawns = r.deltaPawns;
      this.changed();
    }).catch(() => {
      if (epoch !== this.epoch) return;
      this.loading = false;
      this.changed();
    });
  }
}
