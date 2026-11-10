import { WoodpeckerPlan } from './woodpecker.service';

const DAY_MS = 86_400_000;

/** Kennzahlen eines vorigen Zyklus zum Vergleich (nur mit mindestens einem Versuch). */
export interface WoodpeckerPrevStats {
  number: number;
  /** Quote in Prozentpunkten (0–100), für `| pct`. */
  ratePct: number;
  avgSeconds: number;
}

/**
 * Statistik + Hochrechnung des laufenden Zyklus (Wunsch 2026-11-10: „wie viel gelöst, wie viel mit Hint,
 * Hochrechnung“). Alles aus `plan.cycles` — der Server liefert je Zyklus nur Summen (erster Versuch je Aufgabe).
 *
 * „Gelöst mit Tipp“ bleibt GELÖST (zählt in `solved` und in der Quote), wird aber getrennt ausgewiesen:
 * `solvedClean` = gelöst ohne Tipp.
 */
export interface WoodpeckerCycleStats {
  cycle: number;
  attempted: number;
  solved: number;
  solvedClean: number;
  /** Versuche mit mindestens einem Tipp (gelöst oder nicht). */
  hinted: number;
  /** solved / attempted in Prozentpunkten; `null` ohne Versuch. */
  ratePct: number | null;
  /** Ø Lösezeit je bearbeiteter Aufgabe (Sekunden, gerundet); `null` ohne Versuch. */
  avgSeconds: number | null;
  /** Ab Zyklus 2: Satz − bearbeitet; in Zyklus 1 `null` (der Satz wächst noch). */
  open: number | null;
  /** Ab Zyklus 2: offen × Ø (reine Lösezeit, Sekunden); `null` ohne Ø oder in Zyklus 1. */
  remainingSeconds: number | null;
  /** Begonnene Kalendertage seit Zyklusbeginn (lokal, inkl. heute, ≥ 1). */
  daysElapsed: number;
  /** Bisheriges Tempo: bearbeitet / daysElapsed. */
  perDay: number;
  /** Ganze Resttage bis zur Frist (wie im Kopf „noch N Tage“); 0 = Frist erreicht. */
  daysLeft: number;
  overdue: boolean;
  /** Ab Zyklus 2: nötige Aufgaben je Tag bis zur Frist (aufgerundet); `null` in Zyklus 1, wenn nichts offen ist
   *  oder die Frist schon vorbei ist. */
  neededPerDay: number | null;
  /** Ab Zyklus 2: voraussichtliches Ende beim bisherigen Tempo (ISO); `null` ohne Tempo oder nichts offen. */
  etaIso: string | null;
  /** Ab Zyklus 2: reicht das Tempo bis zur Frist? `null` ohne Tempo bzw. nichts offen. */
  onTrack: boolean | null;
  /** Zyklus 1: bei diesem Tempo ≈ so viele Aufgaben im Satz bis zur Frist (≤ alle Aufgaben der Kurse);
   *  `null` ohne Versuch oder ab Zyklus 2. */
  projectedSet: number | null;
  deadlineIso: string;
  prev: WoodpeckerPrevStats | null;
}

/** Lokale Mitternacht des Zeitpunkts (ms). */
function localDayStart(t: number): number {
  const d = new Date(t);
  return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
}

/** Begonnene lokale Kalendertage von `from` bis `now`, inklusive beider Tage (≥ 1). Über Math.round, damit ein
 *  Sommerzeitwechsel (23/25-h-Tag) nicht mitzählt. */
export function calendarDaysSince(fromMs: number, nowMs: number): number {
  if (nowMs <= fromMs) return 1;
  return Math.round((localDayStart(nowMs) - localDayStart(fromMs)) / DAY_MS) + 1;
}

/**
 * Statistik des LAUFENDEN (letzten) Zyklus; `null` ohne Plan/Zyklus. Rein (Zeit über `now`), getestet in
 * `woodpecker-stats.util.spec.ts`.
 *
 * Formeln:
 * - Quote = gelöst / bearbeitet; Ø = Σ Zeit / bearbeitet.
 * - Tempo = bearbeitet / begonnene Kalendertage seit Zyklusbeginn (heute zählt als ganzer Tag — „gerade begonnen“
 *   mit 5 Aufgaben = 5/Tag statt einer Hochrechnung aus Minuten).
 * - ab Zyklus 2: offen = Satz − bearbeitet; Restzeit = offen × Ø; nötig/Tag = ⌈offen / Resttage⌉ (Resttage wie
 *   `daysLeft`, aufgerundet); voraussichtlich fertig = jetzt + offen / Tempo Tage, im Plan, wenn ≤ Frist.
 * - Zyklus 1: ≈ Satz bis zur Frist = bearbeitet + Tempo × verbleibende Tage (anteilig), höchstens alle Aufgaben.
 */
export function woodpeckerCycleStats(plan: WoodpeckerPlan | null | undefined, now: number = Date.now()): WoodpeckerCycleStats | null {
  const c = plan?.cycles.length ? plan.cycles[plan.cycles.length - 1] : null;
  if (!plan || !c) return null;
  const start = new Date(c.startedAt).getTime();
  const deadline = new Date(c.deadline).getTime();
  const attempted = Math.max(0, c.attempted);
  const solved = Math.max(0, c.solved);
  const hinted = Math.max(0, c.hinted ?? 0);
  const solvedWithHint = Math.max(0, c.solvedWithHint ?? 0);
  const ratePct = attempted ? (100 * solved) / attempted : null;
  const avgSeconds = attempted ? Math.round(c.timeSeconds / attempted) : null;
  const daysElapsed = calendarDaysSince(start, now);
  const perDay = attempted / daysElapsed;
  const msLeft = deadline - now;
  const daysLeft = Math.max(0, Math.ceil(msLeft / DAY_MS));
  const overdue = msLeft <= 0;

  let open: number | null = null;
  let remainingSeconds: number | null = null;
  let neededPerDay: number | null = null;
  let etaIso: string | null = null;
  let onTrack: boolean | null = null;
  let projectedSet: number | null = null;

  if (c.number > 1) {
    open = Math.max(0, plan.setSize - attempted);
    remainingSeconds = avgSeconds != null ? open * avgSeconds : null;
    if (open > 0 && daysLeft > 0) neededPerDay = Math.ceil(open / daysLeft);
    if (open > 0 && perDay > 0) {
      const eta = now + (open / perDay) * DAY_MS;
      etaIso = new Date(eta).toISOString();
      onTrack = eta <= deadline;
    }
  } else if (attempted > 0) {
    const rest = Math.max(0, msLeft / DAY_MS);
    projectedSet = Math.min(Math.max(plan.availableCount, attempted), Math.round(attempted + perDay * rest));
  }

  const p = plan.cycles.length > 1 ? plan.cycles[plan.cycles.length - 2] : null;
  const prev = p && p.attempted > 0
    ? { number: p.number, ratePct: (100 * p.solved) / p.attempted, avgSeconds: Math.round(p.timeSeconds / p.attempted) }
    : null;

  return {
    cycle: c.number, attempted, solved, solvedClean: Math.max(0, solved - solvedWithHint), hinted, ratePct, avgSeconds,
    open, remainingSeconds, daysElapsed, perDay, daysLeft, overdue, neededPerDay, etaIso, onTrack, projectedSet,
    deadlineIso: c.deadline, prev,
  };
}
