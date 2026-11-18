import { WoodpeckerCycle, WoodpeckerOverlap, WoodpeckerPlan, WoodpeckerStats, WoodpeckerStatsCycle } from './woodpecker.service';
import { calendarDaysSince, planWithStats, woodpeckerCycleStats, woodpeckerOverview } from './woodpecker-stats.util';

const DAY = 86_400_000;
/** Lokale Zeitpunkte, damit die Kalendertage in jeder Zeitzone gleich zählen. */
const at = (d: number, h = 12) => new Date(2026, 10, d, h, 0, 0).getTime();

function cycle(over: Partial<WoodpeckerCycle> & { start: number; days: number }): WoodpeckerCycle {
  const { start, days, ...rest } = over;
  return {
    number: 1, targetDays: days, startedAt: new Date(start).toISOString(), deadline: new Date(start + days * DAY).toISOString(),
    finishedAt: null, attempted: 0, solved: 0, timeSeconds: 0, hinted: 0, solvedWithHint: 0, ...rest,
  };
}

function plan(cycles: WoodpeckerCycle[], over: Partial<WoodpeckerPlan> = {}): WoodpeckerPlan {
  return {
    id: 1, name: 'WP', createdAt: '', completedAt: null, books: [], setSize: 100, availableCount: 500,
    currentCycle: cycles.length, maxCycles: 7, status: 'solving', nextCycleAt: null, cycles, ...over,
  };
}

describe('woodpeckerCycleStats (Statistik + Hochrechnung, 0.956.0)', () => {
  it('ohne Plan oder Zyklus: null', () => {
    expect(woodpeckerCycleStats(null)).toBeNull();
    expect(woodpeckerCycleStats(plan([]))).toBeNull();
  });

  it('calendarDaysSince zählt begonnene lokale Kalendertage inkl. heute', () => {
    expect(calendarDaysSince(at(1, 23), at(1, 23) + 60_000)).toBe(1);
    expect(calendarDaysSince(at(1, 23), at(2, 1))).toBe(2);      // kurz nach Mitternacht = zweiter Tag
    expect(calendarDaysSince(at(1), at(8))).toBe(8);
    expect(calendarDaysSince(at(5), at(1))).toBe(1);             // Uhr vor dem Beginn: nie < 1
  });

  it('Zyklus 2, im Plan: Quote, Ø, offen, Restzeit, nötig/Tag, Tempo und voraussichtliches Ende', () => {
    // 14 Tage, Beginn am 1., heute der 5. (5 Kalendertage) — 50 von 100 bearbeitet, 40 gelöst, 10 davon mit Tipp.
    const c2 = cycle({ number: 2, start: at(1, 8), days: 14, attempted: 50, solved: 40, timeSeconds: 3000, hinted: 12, solvedWithHint: 10 });
    const s = woodpeckerCycleStats(plan([cycle({ start: at(1) - 40 * DAY, days: 28, attempted: 100, solved: 70, timeSeconds: 9000 }), c2]), at(5))!;
    expect(s.cycle).toBe(2);
    expect(s.ratePct).toBe(80);
    expect(s.solvedClean).toBe(30);       // gelöst mit Tipp bleibt gelöst, nur nicht „sauber“
    expect(s.hinted).toBe(12);
    expect(s.avgSeconds).toBe(60);
    expect(s.open).toBe(50);
    expect(s.remainingSeconds).toBe(3000);
    expect(s.daysElapsed).toBe(5);
    expect(s.perDay).toBe(10);
    expect(s.daysLeft).toBe(10);          // Frist am 15. um 8 Uhr, jetzt der 5. um 12 Uhr → 9,8 → 10
    expect(s.neededPerDay).toBe(5);
    expect(new Date(s.etaIso!).getTime()).toBe(at(10));   // 50 offen / 10 pro Tag = 5 Tage
    expect(s.onTrack).toBeTrue();
    expect(s.projectedSet).toBeNull();
  });

  it('Zyklus 2, zu langsam: Ende nach der Frist', () => {
    const c2 = cycle({ number: 2, start: at(1), days: 7, attempted: 10, solved: 10, timeSeconds: 600 });
    const s = woodpeckerCycleStats(plan([cycle({ start: at(1) - 30 * DAY, days: 28 }), c2]), at(5))!;
    expect(s.perDay).toBe(2);
    expect(s.neededPerDay).toBe(30);      // 90 offen, 3 Tage
    expect(s.onTrack).toBeFalse();
  });

  it('Zyklus 2 gerade begonnen, 0 Versuche: offen = Satz, nötig/Tag, aber kein Tempo, kein Ende, kein Ø', () => {
    const now = at(3);
    const c2 = cycle({ number: 2, start: now - 60_000, days: 14 });
    const s = woodpeckerCycleStats(plan([cycle({ start: at(1) - 30 * DAY, days: 28, attempted: 100, solved: 50, timeSeconds: 100 }), c2]), now)!;
    expect(s.ratePct).toBeNull();
    expect(s.avgSeconds).toBeNull();
    expect(s.open).toBe(100);
    expect(s.remainingSeconds).toBeNull();
    expect(s.neededPerDay).toBe(8);       // ⌈100 / 14⌉
    expect(s.perDay).toBe(0);
    expect(s.etaIso).toBeNull();
    expect(s.onTrack).toBeNull();
    expect(s.daysElapsed).toBe(1);
  });

  it('Zyklus 2 gerade begonnen mit 5 Aufgaben: heute zählt als ganzer Tag (5/Tag, keine Minuten-Hochrechnung)', () => {
    const now = at(3, 12);
    const c2 = cycle({ number: 2, start: now - 10 * 60_000, days: 14, attempted: 5, solved: 5, timeSeconds: 300 });
    const s = woodpeckerCycleStats(plan([cycle({ start: at(1) - 30 * DAY, days: 28 }), c2]), now)!;
    expect(s.perDay).toBe(5);
    expect(s.onTrack).toBeFalse();        // 95 offen / 5 pro Tag = 19 Tage > 14
  });

  it('Frist überschritten: überfällig, nötig/Tag entfällt, Ende nach der Frist', () => {
    const c2 = cycle({ number: 2, start: at(1), days: 2, attempted: 20, solved: 18, timeSeconds: 400 });
    const s = woodpeckerCycleStats(plan([cycle({ start: at(1) - 30 * DAY, days: 28 }), c2]), at(5))!;
    expect(s.overdue).toBeTrue();
    expect(s.daysLeft).toBe(0);
    expect(s.neededPerDay).toBeNull();
    expect(s.onTrack).toBeFalse();
    expect(s.open).toBe(80);
  });

  it('Zyklus 2 alles bearbeitet: nichts offen, keine Hochrechnung', () => {
    const c2 = cycle({ number: 2, start: at(1), days: 14, attempted: 100, solved: 90, timeSeconds: 1000 });
    const s = woodpeckerCycleStats(plan([cycle({ start: at(1) - 30 * DAY, days: 28 }), c2]), at(4))!;
    expect(s.open).toBe(0);
    expect(s.neededPerDay).toBeNull();
    expect(s.etaIso).toBeNull();
    expect(s.remainingSeconds).toBe(0);
  });

  it('Zyklus 1: kein offen/Ende, stattdessen ≈ Satz bis zur Frist aus dem Tempo', () => {
    // Beginn am 1. um 12 Uhr, heute der 4. um 12 Uhr: 4 Kalendertage, 40 Aufgaben → 10/Tag, noch 25 Tage.
    const s = woodpeckerCycleStats(plan([cycle({ start: at(1), days: 28, attempted: 40, solved: 30, timeSeconds: 2400 })]), at(4))!;
    expect(s.open).toBeNull();
    expect(s.neededPerDay).toBeNull();
    expect(s.etaIso).toBeNull();
    expect(s.remainingSeconds).toBeNull();
    expect(s.perDay).toBe(10);
    expect(s.projectedSet).toBe(290);
  });

  it('Zyklus 1: Hochrechnung höchstens alle Aufgaben der Kurse; ohne Versuch keine; nach der Frist = bearbeitet', () => {
    const c = cycle({ start: at(1), days: 28, attempted: 40, solved: 30, timeSeconds: 2400 });
    expect(woodpeckerCycleStats(plan([c], { availableCount: 120 }), at(4))!.projectedSet).toBe(120);
    expect(woodpeckerCycleStats(plan([cycle({ start: at(1), days: 28 })]), at(4))!.projectedSet).toBeNull();
    expect(woodpeckerCycleStats(plan([c]), at(1) + 30 * DAY)!.projectedSet).toBe(40);
  });

  it('ältere Server ohne hinted/solvedWithHint: 0 mit Tipp, alles gelöste ist sauber', () => {
    const c: any = cycle({ start: at(1), days: 28, attempted: 4, solved: 3, timeSeconds: 40 });
    delete c.hinted; delete c.solvedWithHint;
    const s = woodpeckerCycleStats(plan([c]), at(2))!;
    expect(s.hinted).toBe(0);
    expect(s.solvedClean).toBe(3);
  });
});

// Wunsch 2026-11-18: ganz oben bisherige Zeit/Quote gegen dieselben Aufgaben der früheren Zyklen, nötig je Tag, heute.
describe('woodpeckerOverview (Übersicht im Statistik-Fenster)', () => {
  const overlap = (over: Partial<WoodpeckerOverlap>): WoodpeckerOverlap => ({
    cycle: 1, count: 0, secondsNow: 0, secondsThen: 0, solvedNow: 0, solvedThen: 0, hintedNow: 0, hintedThen: 0,
    ratePctNow: null, ratePctThen: null, timeRatio: null, speedFactor: null, ...over,
  });
  const row = (over: Partial<WoodpeckerStatsCycle>): WoodpeckerStatsCycle => ({
    number: 1, targetDays: 28, startedAt: '', finishedAt: null, attempted: 0, solved: 0, solvedClean: 0, hinted: 0,
    timeSeconds: 0, ratePct: null, avgSeconds: null, daysUsed: 0, hintsTrackedCount: 0, sameAsNow: null, ...over,
  });
  const stats = (cycles: WoodpeckerStatsCycle[], over: Partial<WoodpeckerStats> = {}): WoodpeckerStats => ({
    planId: 1, currentCycle: cycles.length, setSize: 100, cycles, changes: null, forecast: null, days: [],
    hintsTrackedSince: '2026-11-10T17:34:00Z',
    hints: { tracked: 0, untracked: 0, solvedNoHint: 0, solvedHint1: 0, solvedHint2: 0, solvedHint3: 0, missedNoHint: 0, missedWithHint: 0 },
    hintChanges: null, timeBuckets: { fast: 0, medium: 0, slow: 0 }, slowest: [], alwaysMissedCount: 0, alwaysMissed: [],
    ...over,
  });
  /** Drei Zyklen, laufender = 3: Beginn am 1., 7 Tage, heute der 5. um 12 Uhr. */
  const plan3 = () => plan([
    cycle({ start: at(1) - 60 * DAY, days: 28, attempted: 100, solved: 70, timeSeconds: 9000 }),
    cycle({ number: 2, start: at(1) - 20 * DAY, days: 14, attempted: 100, solved: 85, timeSeconds: 6000 }),
    cycle({ number: 3, start: at(1, 8), days: 7, attempted: 39, solved: 29, timeSeconds: 1900 }),   // Plan eine Meldung hinterher
  ]);

  it('ohne /stats oder Plan: null', () => {
    expect(woodpeckerOverview(null, plan3())).toBeNull();
    expect(woodpeckerOverview(stats([row({})]), null)).toBeNull();
  });

  it('Zyklus 3: Zeit und Quote jetzt, dieselben Aufgaben je früherem Zyklus (jüngster zuerst), nötig je Tag = ⌈offen / Resttage⌉, heute', () => {
    const s = stats([
      row({ number: 1, sameAsNow: overlap({ cycle: 1, count: 40, secondsNow: 2000, secondsThen: 5200, ratePctNow: 75, ratePctThen: 65 }) }),
      row({ number: 2, sameAsNow: overlap({ cycle: 2, count: 40, secondsNow: 2000, secondsThen: 3100, ratePctNow: 75, ratePctThen: 80 }) }),
      row({ number: 3, attempted: 40, solved: 30, solvedClean: 28, hinted: 3, timeSeconds: 2000, ratePct: 75, avgSeconds: 50 }),
    ], { days: [{ date: '2026-11-04', attempted: 28, solved: 20, timeSeconds: 1400 }, { date: '2026-11-05', attempted: 12, solved: 10, timeSeconds: 600 }] });
    const o = woodpeckerOverview(s, plan3(), at(5))!;
    expect(o.cycle).toBe(3);
    expect(o.attempted).toBe(40);            // aus /stats, nicht aus dem (älteren) Plan
    expect(o.secondsNow).toBe(2000);
    expect(o.ratePctNow).toBe(75);
    expect(o.earlier.map(e => e.cycle)).toEqual([2, 1]);
    expect(o.earlier[0]).toEqual({ cycle: 2, count: 40, secondsThen: 3100, ratePctThen: 80, partial: null });
    expect(o.earlier[1].secondsThen).toBe(5200);
    expect(o.open).toBe(60);                 // 100 − 40
    expect(o.daysLeft).toBe(3);              // Frist am 8. um 8 Uhr, jetzt der 5. um 12 Uhr → 2,8 → 3
    expect(o.neededPerDay).toBe(20);         // ⌈60 / 3⌉
    expect(o.today).toBe(12);
    expect(o.overdue).toBeFalse();
  });

  it('Schnittmenge kleiner als jetzt bearbeitet: die Jetzt-Werte auf genau diesen Aufgaben stehen dabei', () => {
    const s = stats([
      row({ number: 1, sameAsNow: overlap({ cycle: 1, count: 40, secondsNow: 2000, secondsThen: 5200, ratePctNow: 75, ratePctThen: 65 }) }),
      row({ number: 2, sameAsNow: overlap({ cycle: 2, count: 25, secondsNow: 1100, secondsThen: 1800, ratePctNow: 72, ratePctThen: 88 }) }),
      row({ number: 3, attempted: 40, solved: 30, solvedClean: 30, timeSeconds: 2000, ratePct: 75, avgSeconds: 50 }),
    ]);
    const o = woodpeckerOverview(s, plan3(), at(5))!;
    expect(o.earlier[0]).toEqual({ cycle: 2, count: 25, secondsThen: 1800, ratePctThen: 88, partial: { secondsNow: 1100, ratePctNow: 72 } });
    expect(o.earlier[1].partial).toBeNull();
  });

  it('heute noch nichts (letzter Eintrag ist gestern): 0; Zyklen ohne gemeinsame Aufgabe fehlen', () => {
    const s = stats([
      row({ number: 1, sameAsNow: overlap({ cycle: 1, count: 0 }) }),
      row({ number: 2, sameAsNow: null }),
      row({ number: 3, attempted: 28, solved: 20, solvedClean: 20, timeSeconds: 1400, ratePct: 71.4, avgSeconds: 50 }),
    ], { days: [{ date: '2026-11-04', attempted: 28, solved: 20, timeSeconds: 1400 }] });
    const o = woodpeckerOverview(s, plan3(), at(5))!;
    expect(o.today).toBe(0);
    expect(o.earlier).toEqual([]);
  });

  it('Zyklus 1: kein Soll (offen/nötig = null), dafür Tempo und heute', () => {
    const p = plan([cycle({ start: at(1), days: 28, attempted: 40, solved: 30, timeSeconds: 2400 })]);
    const s = stats([row({ attempted: 40, solved: 30, solvedClean: 30, timeSeconds: 2400, ratePct: 75, avgSeconds: 60 })],
      { days: [{ date: '2026-11-04', attempted: 7, solved: 5, timeSeconds: 420 }] });
    const o = woodpeckerOverview(s, p, at(4))!;
    expect(o.open).toBeNull();
    expect(o.neededPerDay).toBeNull();
    expect(o.perDay).toBe(10);
    expect(o.today).toBe(7);
    expect(o.earlier).toEqual([]);
  });

  it('Frist vorbei: überfällig, keine Soll-Zahl; ohne Versuch im Zyklus Quote null', () => {
    const p = plan([
      cycle({ start: at(1) - 40 * DAY, days: 28, attempted: 100, solved: 70, timeSeconds: 9000 }),
      cycle({ number: 2, start: at(1), days: 2 }),
    ]);
    const o = woodpeckerOverview(stats([row({ number: 1 }), row({ number: 2 })]), p, at(5))!;
    expect(o.overdue).toBeTrue();
    expect(o.neededPerDay).toBeNull();
    expect(o.open).toBe(100);
    expect(o.ratePctNow).toBeNull();
    expect(o.secondsNow).toBe(0);
    expect(o.today).toBe(0);
  });

  it('planWithStats: /stats eines anderen Plans oder Zyklus lässt den Plan unverändert', () => {
    const p = plan3();
    expect(planWithStats(p, stats([row({}), row({ number: 2, attempted: 5 })]))).toBe(p);              // anderer Zyklus
    expect(planWithStats(p, stats([row({}), row({ number: 2 }), row({ number: 3 })], { planId: 9 }))).toBe(p);
    const merged = planWithStats(p, stats([row({}), row({ number: 2 }),
      row({ number: 3, attempted: 40, solved: 30, solvedClean: 28, hinted: 3, timeSeconds: 2000 })]));
    expect(merged.cycles[2]).toEqual(jasmine.objectContaining({ attempted: 40, solved: 30, timeSeconds: 2000, hinted: 3, solvedWithHint: 2 }));
    expect(merged.cycles[2].deadline).toBe(p.cycles[2].deadline);
  });
});
