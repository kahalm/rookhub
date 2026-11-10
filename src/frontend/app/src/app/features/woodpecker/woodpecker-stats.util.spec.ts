import { WoodpeckerCycle, WoodpeckerPlan } from './woodpecker.service';
import { calendarDaysSince, woodpeckerCycleStats } from './woodpecker-stats.util';

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
    expect(s.prev).toEqual({ number: 1, ratePct: 70, avgSeconds: 90 });
  });

  it('Zyklus 2, zu langsam: Ende nach der Frist', () => {
    const c2 = cycle({ number: 2, start: at(1), days: 7, attempted: 10, solved: 10, timeSeconds: 600 });
    const s = woodpeckerCycleStats(plan([cycle({ start: at(1) - 30 * DAY, days: 28 }), c2]), at(5))!;
    expect(s.perDay).toBe(2);
    expect(s.neededPerDay).toBe(30);      // 90 offen, 3 Tage
    expect(s.onTrack).toBeFalse();
    expect(s.prev).toBeNull();            // Vorzyklus ohne Versuch: kein Vergleich
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
    expect(s.prev).toBeNull();
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
