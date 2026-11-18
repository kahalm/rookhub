import { todayYmd, WoodpeckerPlan, WoodpeckerStats } from './woodpecker.service';

const DAY_MS = 86_400_000;

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
 *
 * Der Vorzyklus (Quote, Ø) steht seit 2026-11-18 nicht mehr hier: die Übersicht ({@link woodpeckerOverview}) vergleicht
 * auf denselben Aufgaben, die Verlaufstabelle zeigt die ganzen Zyklen.
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

  return {
    cycle: c.number, attempted, solved, solvedClean: Math.max(0, solved - solvedWithHint), hinted, ratePct, avgSeconds,
    open, remainingSeconds, daysElapsed, perDay, daysLeft, overdue, neededPerDay, etaIso, onTrack, projectedSet,
    deadlineIso: c.deadline,
  };
}

/** Ein früherer Zyklus in der Übersicht: Zeit und Quote auf DENSELBEN Aufgaben wie jetzt. */
export interface WoodpeckerOverviewEarlier {
  cycle: number;
  /** Gemeinsame Aufgaben (jetzt bearbeitet UND damals mit Versuch). */
  count: number;
  secondsThen: number;
  /** Quote damals in Prozentpunkten; `null` ohne gemeinsame Aufgabe. */
  ratePctThen: number | null;
  /** Nur wenn die Schnittmenge KLEINER ist als die jetzt bearbeiteten Aufgaben: die Jetzt-Werte auf genau diesen
   *  Aufgaben — sonst stünde die Gesamtzeit von jetzt gegen die Zeit einer Teilmenge. Sonst `null`. */
  partial: { secondsNow: number; ratePctNow: number | null } | null;
}

/**
 * Übersicht ganz oben im Statistik-Fenster (Wunsch 2026-11-18): bisherige Zeit und Quote des laufenden Zyklus, daneben
 * dieselben Aufgaben in JEDEM früheren Zyklus, dazu „nötig je Tag“ und „heute“.
 */
export interface WoodpeckerOverview {
  cycle: number;
  attempted: number;
  /** Bisherige Lösezeit im laufenden Zyklus (Sekunden). */
  secondsNow: number;
  /** Bisherige Quote in Prozentpunkten; `null` ohne Versuch. */
  ratePctNow: number | null;
  /** Frühere Zyklen mit mindestens einer gemeinsamen Aufgabe, jüngster zuerst. */
  earlier: WoodpeckerOverviewEarlier[];
  /** Heute (lokaler Kalendertag) im laufenden Zyklus bearbeitete Aufgaben. */
  today: number;
  /** Ab Zyklus 2: offene Aufgaben des Satzes; in Zyklus 1 `null` (der Satz wächst noch, es gibt kein Soll). */
  open: number | null;
  /** Ganze Resttage bis zur Frist (aufgerundet wie „noch N Tage“); 0 = Frist erreicht. */
  daysLeft: number;
  overdue: boolean;
  /** Ab Zyklus 2: ⌈offen / Resttage⌉; `null` in Zyklus 1, ohne offene Aufgabe oder nach der Frist. */
  neededPerDay: number | null;
  /** Bisheriges Tempo (bearbeitet / begonnene Kalendertage) — in Zyklus 1 statt einer Soll-Zahl gezeigt. */
  perDay: number;
}

/**
 * Der Plan mit den Zahlen des laufenden Zyklus aus `/stats` — damit Übersicht, Kennzahlzeilen und Abschnitte im
 * Fenster denselben Serverstand zeigen (der Plan im Löser kann eine Meldung hinterher sein). Frist, Beginn und
 * `availableCount` gibt es nur im Plan; passt `/stats` nicht zum Plan (anderer Zyklus), bleibt der Plan, wie er ist.
 */
export function planWithStats(plan: WoodpeckerPlan, stats: WoodpeckerStats | null | undefined): WoodpeckerPlan {
  const cur = stats?.cycles.find(c => c.number === stats.currentCycle);
  const last = plan.cycles[plan.cycles.length - 1];
  if (!stats || !cur || !last || last.number !== cur.number || stats.planId !== plan.id) return plan;
  return {
    ...plan,
    setSize: plan.currentCycle > 1 ? stats.setSize : plan.setSize,
    cycles: plan.cycles.map(c => (c === last
      ? { ...c, attempted: cur.attempted, solved: cur.solved, timeSeconds: cur.timeSeconds, hinted: cur.hinted,
          solvedWithHint: Math.max(0, cur.solved - cur.solvedClean) }
      : c)),
  };
}

/**
 * Übersicht aus `/stats` (Zeit, Quote, dieselben Aufgaben je früherem Zyklus, heute) und dem Plan (Frist). Rein
 * (Zeit über `now`), getestet in `woodpecker-stats.util.spec.ts`. `null`, solange `/stats` fehlt oder nicht zum
 * Plan passt.
 *
 * - „heute“ = Eintrag von `days[]` mit dem lokalen Datum von `now` (der Server bildet die Tage mit dem
 *   `utcOffsetMinutes` dieses Geräts); gibt es keinen, ist heute noch nichts bearbeitet → 0.
 * - „nötig je Tag“ = ⌈offene Aufgaben / Resttage⌉ (Vorgabe des Nutzers), siehe {@link woodpeckerCycleStats}.
 */
export function woodpeckerOverview(
  stats: WoodpeckerStats | null | undefined, plan: WoodpeckerPlan | null | undefined, now: number = Date.now(),
): WoodpeckerOverview | null {
  if (!stats || !plan) return null;
  const cur = stats.cycles.find(c => c.number === stats.currentCycle);
  const s = woodpeckerCycleStats(planWithStats(plan, stats), now);
  if (!cur || !s || s.cycle !== cur.number) return null;
  const earlier = stats.cycles
    .filter(c => c.number < stats.currentCycle && (c.sameAsNow?.count ?? 0) > 0)
    .sort((a, b) => b.number - a.number)
    .map(c => {
      const o = c.sameAsNow!;
      return {
        cycle: c.number, count: o.count, secondsThen: o.secondsThen, ratePctThen: o.ratePctThen,
        partial: o.count < cur.attempted ? { secondsNow: o.secondsNow, ratePctNow: o.ratePctNow } : null,
      };
    });
  const todayKey = todayYmd(new Date(now));
  return {
    cycle: cur.number, attempted: cur.attempted, secondsNow: cur.timeSeconds, ratePctNow: s.ratePct, earlier,
    today: stats.days.find(d => d.date === todayKey)?.attempted ?? 0,
    open: s.open, daysLeft: s.daysLeft, overdue: s.overdue, neededPerDay: s.neededPerDay, perDay: s.perDay,
  };
}
