import { GuessHistoryMove } from './guess.service';

/** Eine Zeile der Zugliste: Weiss und Schwarz nebeneinander, je mit Index und Kommentar-Marke. */
export interface HistoryRow {
  no: number;
  w: string; wIdx: number; wNoted: boolean; wSkipped: string | null;
  b: string | null; bIdx: number; bNoted: boolean; bSkipped: string | null;
}

/** Der Verlauf als Zeilen „Nr. · Weiss · Schwarz" — je Halbzug der Index fuers Anklicken. */
export function guessHistoryRows(history: readonly GuessHistoryMove[]): HistoryRow[] {
  const rows: HistoryRow[] = [];
  for (let i = 0; i < history.length; i++) {
    const m = history[i];
    const noted = !!m.comment;
    const skipped = m.skipped ?? null;
    if (m.white) {
      rows.push({ no: m.moveNumber, w: m.san, wIdx: i, wNoted: noted, wSkipped: skipped,
                  b: null, bIdx: -1, bNoted: false, bSkipped: null });
    } else if (rows.length && rows[rows.length - 1].b === null) {
      rows[rows.length - 1].b = m.san;
      rows[rows.length - 1].bIdx = i;
      rows[rows.length - 1].bNoted = noted;
      rows[rows.length - 1].bSkipped = skipped;
    } else {
      rows.push({ no: m.moveNumber, w: '…', wIdx: -1, wNoted: false, wSkipped: null,
                  b: m.san, bIdx: i, bNoted: noted, bSkipped: skipped });
    }
  }
  return rows;
}

/** Warum der angesehene Zug nicht abgefragt wurde (`notScorable`, `pending` …) — sonst `null`. */
export function skipReason(history: readonly GuessHistoryMove[], index: number | null): string | null {
  if (index === null || index < 0) return null;
  return history[index]?.skipped || null;
}
