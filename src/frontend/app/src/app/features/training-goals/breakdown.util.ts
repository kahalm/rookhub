// Reine Perioden-/Aufschlüsselungs-Mathematik der Trainingsziele-Seite. In eine eigene Util gezogen,
// damit sowohl TrainingGoalsComponent als auch die ausgegliederten Karten (PeriodBreakdownCardComponent)
// sie importieren können, OHNE einen Modul-Zyklus Parent ↔ Kind zu erzeugen. Alles frei von Angular/DOM.

import { TrackerDay, SourceBreakdown, ThemeBreakdown } from './training-goals.service';

/** Eine Zeile einer Aufschlüsselung (Quelle/Thema): i18n-Label + Sekunden + Balkenanteil. */
export interface BreakRow { label: string; seconds: number; pct: number; }

/** Periode der umschaltbaren Aufschlüsselung. */
export type BreakdownPeriod = 'day' | 'week' | 'month' | 'year' | 'all';
export const BREAKDOWN_PERIODS: BreakdownPeriod[] = ['day', 'week', 'month', 'year', 'all'];

/** Lokales yyyy-MM-dd eines Date. */
export function ymd(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/** yyyy-MM-dd → lokales Date (Mitternacht). */
export function parseYmd(s: string): Date {
  const [y, m, d] = s.split('-').map(Number);
  return new Date(y, m - 1, d);
}

/** [start,end] (inkl., yyyy-MM-dd) der Periode, die `anchor` enthält. 'all' = firstDate…today. */
export function periodBounds(
  period: BreakdownPeriod, anchor: string, firstDate: string, today: string,
): { start: string; end: string } {
  if (period === 'all') return { start: firstDate || today, end: today };
  const d = parseYmd(anchor);
  if (period === 'day') return { start: anchor, end: anchor };
  if (period === 'week') {
    const dow = (d.getDay() + 6) % 7;                  // 0 = Montag
    const s = new Date(d); s.setDate(d.getDate() - dow);
    const e = new Date(s); e.setDate(s.getDate() + 6);
    return { start: ymd(s), end: ymd(e) };
  }
  if (period === 'month') {
    return { start: ymd(new Date(d.getFullYear(), d.getMonth(), 1)), end: ymd(new Date(d.getFullYear(), d.getMonth() + 1, 0)) };
  }
  return { start: ymd(new Date(d.getFullYear(), 0, 1)), end: ymd(new Date(d.getFullYear(), 11, 31)) }; // year
}

/** Anker um eine Periode verschieben (dir −1 = zurück, +1 = vor). Monats-/Jahresschritte normalisieren
 * auf den Periodenanfang, damit kein Tag-Überlauf (z.B. 31.03. − 1 Monat) eine Periode überspringt. */
export function shiftAnchor(period: BreakdownPeriod, anchor: string, dir: number): string {
  const d = parseYmd(anchor);
  if (period === 'day') { d.setDate(d.getDate() + dir); return ymd(d); }
  if (period === 'week') { d.setDate(d.getDate() + dir * 7); return ymd(d); }
  if (period === 'month') return ymd(new Date(d.getFullYear(), d.getMonth() + dir, 1));
  if (period === 'year') return ymd(new Date(d.getFullYear() + dir, 0, 1));
  return anchor; // 'all' kennt kein Durchschalten
}

/** Summiert bySource+byTheme über alle Tage im [start,end]-Fenster (yyyy-MM-dd lexikografisch = chronologisch). */
export function sumBreakdown(days: TrackerDay[], start: string, end: string): { bySource: SourceBreakdown; byTheme: ThemeBreakdown } {
  const bySource: SourceBreakdown = { randomPuzzleSeconds: 0, courseBookSeconds: 0, chessableSeconds: 0 };
  const byTheme: ThemeBreakdown = { openingSeconds: 0, middlegameSeconds: 0, endgameSeconds: 0, tacticsSeconds: 0, otherSeconds: 0 };
  for (const day of days) {
    if (day.date < start || day.date > end) continue;
    bySource.randomPuzzleSeconds += day.bySource.randomPuzzleSeconds;
    bySource.courseBookSeconds += day.bySource.courseBookSeconds;
    bySource.chessableSeconds += day.bySource.chessableSeconds;
    byTheme.openingSeconds += day.byTheme.openingSeconds;
    byTheme.middlegameSeconds += day.byTheme.middlegameSeconds;
    byTheme.endgameSeconds += day.byTheme.endgameSeconds;
    byTheme.tacticsSeconds += day.byTheme.tacticsSeconds;
    byTheme.otherSeconds += day.byTheme.otherSeconds;
  }
  return { bySource, byTheme };
}

/** Wandelt eine Aufschlüsselung in Anzeige-Zeilen (nur Töpfe mit Zeit, Anteil am Topf-Total). */
export function breakdownRows(buckets: Record<string, number>, keys: { key: string; label: string }[]): BreakRow[] {
  const total = keys.reduce((sum, k) => sum + (buckets[k.key] ?? 0), 0);
  return keys
    .map(k => ({ label: k.label, seconds: buckets[k.key] ?? 0, pct: total > 0 ? Math.round((100 * (buckets[k.key] ?? 0)) / total) : 0 }))
    .filter(r => r.seconds > 0);
}
