import { WoodpeckerCycle, WoodpeckerOverlap, WoodpeckerPlan, WoodpeckerStats, WoodpeckerStatsCycle } from './woodpecker.service';
import {
  calendarDaysSince, calendarDaysUntil, planWithStats, woodpeckerCycleStats, woodpeckerDailyGoal, woodpeckerOverview,
  woodpeckerSchedule,
} from './woodpecker-stats.util';

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

  it('Zyklus 2, im Plan: Quote, Ø, offen, Restzeit, Tempo und voraussichtliches Ende', () => {
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
    expect(new Date(s.etaIso!).getTime()).toBe(at(10));   // 50 offen / 10 pro Tag = 5 Tage
    expect(s.onTrack).toBeTrue();
    expect(s.projectedSet).toBeNull();
  });

  it('Zyklus 2, zu langsam: Ende nach der Frist', () => {
    const c2 = cycle({ number: 2, start: at(1), days: 7, attempted: 10, solved: 10, timeSeconds: 600 });
    const s = woodpeckerCycleStats(plan([cycle({ start: at(1) - 30 * DAY, days: 28 }), c2]), at(5))!;
    expect(s.perDay).toBe(2);
    expect(s.onTrack).toBeFalse();
  });

  it('Zyklus 2 gerade begonnen, 0 Versuche: offen = Satz, aber kein Tempo, kein Ende, kein Ø', () => {
    const now = at(3);
    const c2 = cycle({ number: 2, start: now - 60_000, days: 14 });
    const s = woodpeckerCycleStats(plan([cycle({ start: at(1) - 30 * DAY, days: 28, attempted: 100, solved: 50, timeSeconds: 100 }), c2]), now)!;
    expect(s.ratePct).toBeNull();
    expect(s.avgSeconds).toBeNull();
    expect(s.open).toBe(100);
    expect(s.remainingSeconds).toBeNull();
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

  it('Frist überschritten: überfällig, Ende nach der Frist', () => {
    const c2 = cycle({ number: 2, start: at(1), days: 2, attempted: 20, solved: 18, timeSeconds: 400 });
    const s = woodpeckerCycleStats(plan([cycle({ start: at(1) - 30 * DAY, days: 28 }), c2]), at(5))!;
    expect(s.overdue).toBeTrue();
    expect(s.daysLeft).toBe(0);
    expect(s.onTrack).toBeFalse();
    expect(s.open).toBe(80);
  });

  it('Zyklus 2 alles bearbeitet: nichts offen, keine Hochrechnung', () => {
    const c2 = cycle({ number: 2, start: at(1), days: 14, attempted: 100, solved: 90, timeSeconds: 1000 });
    const s = woodpeckerCycleStats(plan([cycle({ start: at(1) - 30 * DAY, days: 28 }), c2]), at(4))!;
    expect(s.open).toBe(0);
    expect(s.etaIso).toBeNull();
    expect(s.remainingSeconds).toBe(0);
  });

  it('Zyklus 1: kein offen/Ende, stattdessen ≈ Satz bis zur Frist aus dem Tempo', () => {
    // Beginn am 1. um 12 Uhr, heute der 4. um 12 Uhr: 4 Kalendertage, 40 Aufgaben → 10/Tag, noch 25 Tage.
    const s = woodpeckerCycleStats(plan([cycle({ start: at(1), days: 28, attempted: 40, solved: 30, timeSeconds: 2400 })]), at(4))!;
    expect(s.open).toBeNull();
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
// Wunsch 2026-11-18: „auch ohne Statistik visualisieren, wenn ich heute das notwendige Minimum erledigt habe“.
describe('woodpeckerDailyGoal (Tagesziel für Kachel, Löser-Kopf und Fenster, 0.993.0)', () => {
  /** Zyklus 2 über 14 Tage: Beginn am 9. um 14 Uhr → Frist am 23. um 14 Uhr; Satz 388. */
  const wp = (attempted: number, todayAttempted: number | undefined, over: Partial<WoodpeckerPlan> = {}) => plan([
    cycle({ start: at(9) - 40 * DAY, days: 28, attempted: 388, solved: 337, timeSeconds: 51379, finishedAt: 'x' }),
    cycle({ number: 2, start: at(9, 14), days: 14, attempted, solved: attempted, timeSeconds: 60 * attempted }),
  ], { setSize: 388, todayAttempted, ...over });

  it('calendarDaysUntil: heute und der Fristtag zählen mit; Frist genau um Mitternacht → ihr Tag zählt nicht; mindestens 1', () => {
    expect(calendarDaysUntil(at(23, 14), at(18, 15))).toBe(6);     // 18., 19., 20., 21., 22., 23.
    expect(calendarDaysUntil(at(23, 14), at(18, 0))).toBe(6);      // die Uhrzeit von heute spielt keine Rolle
    expect(calendarDaysUntil(at(23, 14), at(23, 9))).toBe(1);      // Fristtag
    expect(calendarDaysUntil(at(23, 0), at(18, 15))).toBe(5);      // 18. bis 22. — der 23. hat keine Zeit mehr
    expect(calendarDaysUntil(at(18, 9), at(18, 15))).toBe(1);      // schon vorbei: nie unter 1
  });

  it('calendarDaysUntil rechnet Kalendertage, nicht 24-h-Blöcke: die Zeitumstellung (25.10.2026) fügt keinen Tag hinzu', () => {
    // 24.10. mittags bis 26.10. 00:00 Ortszeit = 24. und 25. — in Mitteleuropa sind das 49 Stunden (⌈49 / 24⌉ wäre 3).
    expect(calendarDaysUntil(new Date(2026, 9, 26, 0, 0, 0).getTime(), new Date(2026, 9, 24, 12, 0, 0).getTime())).toBe(2);
    expect(calendarDaysUntil(new Date(2026, 9, 26, 12, 0, 0).getTime(), new Date(2026, 9, 24, 12, 0, 0).getTime())).toBe(3);
  });

  it('Soll heute = ⌈(offen + heute) / Tage bis zur Frist⌉ — bleibt über den Tag FEST, bis es erreicht ist', () => {
    const now = at(18, 15);   // 6 Kalendertage bis zum 23.
    // morgens 270 offen; im Lauf des Tages 23, 45, 52 bearbeitet — das Soll bleibt ⌈270 / 6⌉ = 45
    expect(woodpeckerDailyGoal(wp(118, 0), now)).toEqual({ today: 0, target: 45, open: 270, days: 6, reached: false });
    expect(woodpeckerDailyGoal(wp(141, 23), now)).toEqual({ today: 23, target: 45, open: 247, days: 6, reached: false });
    expect(woodpeckerDailyGoal(wp(162, 44), now)).toEqual({ today: 44, target: 45, open: 226, days: 6, reached: false });
    expect(woodpeckerDailyGoal(wp(163, 45), now)).toEqual({ today: 45, target: 45, open: 225, days: 6, reached: true });    // genau erreicht
    expect(woodpeckerDailyGoal(wp(170, 52), now)).toEqual({ today: 52, target: 45, open: 218, days: 6, reached: true });    // übererfüllt
  });

  it('Tageswechsel: am nächsten Tag zählt heute wieder von 0, das Soll kommt aus dem Rest und einem Tag weniger', () => {
    const g = woodpeckerDailyGoal(wp(170, 0), at(19, 0) + 60_000)!;   // 19. um 00:01
    expect(g).toEqual({ today: 0, target: 44, open: 218, days: 5, reached: false });   // ⌈218 / 5⌉
  });

  it('Fristtag: alles Offene ist das Soll — erreicht erst, wenn nichts mehr offen ist', () => {
    expect(woodpeckerDailyGoal(wp(358, 10), at(23, 9))).toEqual({ today: 10, target: 40, open: 30, days: 1, reached: false });
    expect(woodpeckerDailyGoal(wp(388, 40), at(23, 9))).toEqual({ today: 40, target: 40, open: 0, days: 1, reached: true });
  });

  it('Zyklus beginnt heute: die heutigen Aufgaben sind alle bisherigen', () => {
    const now = at(18, 12);
    const p = plan([
      cycle({ start: now - 40 * DAY, days: 28, attempted: 100, finishedAt: 'x' }),
      cycle({ number: 2, start: now - 10 * 60_000, days: 7, attempted: 5, solved: 5, timeSeconds: 300 }),   // Frist am 25. um 11:50
    ], { setSize: 100, todayAttempted: 5 });
    expect(woodpeckerDailyGoal(p, now)).toEqual({ today: 5, target: 13, open: 95, days: 8, reached: false });   // ⌈100 / 8⌉
  });

  it('Zyklus 1 hat kein Soll: nur die Zahl von heute, nie „erreicht“', () => {
    const p = plan([cycle({ start: at(1), days: 28, attempted: 40, solved: 30, timeSeconds: 2400 })], { todayAttempted: 7 });
    expect(woodpeckerDailyGoal(p, at(4))).toEqual({ today: 7, target: null, open: null, days: null, reached: false });
  });

  it('keine Anzeige: Feld fehlt (älterer Server), Frist vorbei, Pause/bereit/abgeschlossen/archiviert, ohne Plan', () => {
    const now = at(18, 15);
    expect(woodpeckerDailyGoal(wp(141, undefined), now)).toBeNull();          // kein „0 von 42“ aus einem fehlenden Feld
    expect(woodpeckerDailyGoal(wp(141, 23), at(23, 15))).toBeNull();          // Frist (23., 14 Uhr) vorbei
    for (const status of ['break', 'ready', 'completed', 'archived'] as const)
      expect(woodpeckerDailyGoal(wp(141, 23, { status }), now)).withContext(status).toBeNull();
    expect(woodpeckerDailyGoal(null, now)).toBeNull();
    expect(woodpeckerDailyGoal(plan([], { todayAttempted: 3 }), now)).toBeNull();
  });

  it('„heute“ lässt sich übergeben (Statistik-Fenster: Zahl aus /stats) und ist nie größer als das im Zyklus Bearbeitete', () => {
    const now = at(18, 15);
    expect(woodpeckerDailyGoal(wp(141, undefined), now, 23)).toEqual({ today: 23, target: 45, open: 247, days: 6, reached: false });
    expect(woodpeckerDailyGoal(wp(141, 5), now, 23)!.today).toBe(23);         // die übergebene Zahl gewinnt
    expect(woodpeckerDailyGoal(wp(10, 99), now)!.today).toBe(10);             // 99 heute bei 10 im Zyklus kann nicht sein
    expect(woodpeckerDailyGoal(wp(141, undefined), now, null)).toBeNull();
  });
});

// Wunsch 2026-11-18: „neben x pro Tag für fertig auch noch ein Checkmark, wenn ich % der Gesamtpuzzles passend zum
// aktuellen Tag hab (und in Statistik oben ausgewiesen: noch x Puzzle bis z. B. 55 % erfüllt)“.
describe('woodpeckerSchedule (Zeitplan für Kachel, Löser-Kopf und Fenster, 0.994.0)', () => {
  /** Zyklus 2 über 14 Tage ab lokaler Mitternacht (rückdatierter Beginn): 9. 00:00 → Frist 23. 00:00 = 14 Kalendertage
   *  (9. bis 22.; der 23. hat keine Zeit mehr). Satz 270. */
  const wp = (attempted: number, over: Partial<WoodpeckerPlan> = {}, c2: Partial<WoodpeckerCycle> = {}) => plan([
    cycle({ start: at(9, 0) - 40 * DAY, days: 28, attempted: 270, solved: 240, timeSeconds: 30000, finishedAt: 'x' }),
    cycle({ number: 2, start: at(9, 0), days: 14, attempted, solved: attempted, timeSeconds: 60 * attempted, ...c2 }),
  ], { setSize: 270, ...over });

  it('Tag 8 von 14: Soll = ⌈270 × 8 / 14⌉ = 155 (57 %) — „noch 12“, solange 143 bearbeitet sind', () => {
    expect(woodpeckerSchedule(wp(143), at(16, 15))).toEqual({
      day: 8, days: 14, targetPct: 57, target: 155, attempted: 143, setSize: 270, currentPct: 53, left: 12, reached: false, overdue: false,
    });
  });

  it('genau auf Soll: im Zeitplan; eine Aufgabe darunter noch nicht; darüber bleibt es erreicht', () => {
    const now = at(16, 15);
    expect(woodpeckerSchedule(wp(154), now)).toEqual(jasmine.objectContaining({ target: 155, left: 1, reached: false }));
    expect(woodpeckerSchedule(wp(155), now)).toEqual(jasmine.objectContaining({ target: 155, left: 0, reached: true, currentPct: 57 }));
    expect(woodpeckerSchedule(wp(165), now)).toEqual(jasmine.objectContaining({ left: 0, reached: true, targetPct: 57, currentPct: 61 }));
  });

  it('Tag 1: schon am ersten Tag gibt es ein Soll (⌈270 / 14⌉ = 20, 7 %)', () => {
    const now = at(9, 10);
    expect(woodpeckerSchedule(wp(0), now)).toEqual(jasmine.objectContaining({ day: 1, days: 14, targetPct: 7, target: 20, left: 20, reached: false, currentPct: 0 }));
    expect(woodpeckerSchedule(wp(20), now)).toEqual(jasmine.objectContaining({ day: 1, left: 0, reached: true }));
  });

  it('letzter Tag: das Soll ist der ganze Satz — im Zeitplan erst, wenn nichts mehr offen ist', () => {
    const now = at(22, 9);
    expect(woodpeckerSchedule(wp(260), now)).toEqual(jasmine.objectContaining({ day: 14, days: 14, targetPct: 100, target: 270, left: 10, reached: false, overdue: false }));
    expect(woodpeckerSchedule(wp(260), now)!.currentPct).toBe(96);
    expect(woodpeckerSchedule(wp(270), now)).toEqual(jasmine.objectContaining({ left: 0, reached: true, currentPct: 100 }));
  });

  it('nach der Frist: Soll 100 %, „noch x“ = offene Aufgaben, der Tag bleibt beim letzten stehen', () => {
    expect(woodpeckerSchedule(wp(230), at(25, 9))).toEqual({
      day: 14, days: 14, targetPct: 100, target: 270, attempted: 230, setSize: 270, currentPct: 85, left: 40, reached: false, overdue: true,
    });
    // am Fristtag selbst, nach der Uhrzeit der Frist (Beginn mittags → Frist am 23. um 12 Uhr)
    const noon = wp(230, {}, { startedAt: new Date(at(9)).toISOString(), deadline: new Date(at(23)).toISOString() });
    expect(woodpeckerSchedule(noon, at(23, 13))).toEqual(jasmine.objectContaining({ day: 15, days: 15, targetPct: 100, left: 40, overdue: true }));
  });

  it('Beginn mittags: ein Zyklus über 14 × 24 h berührt 15 Kalendertage — dieselbe Zählung wie das Tagesziel', () => {
    // 9. um 12 Uhr → Frist 23. um 12 Uhr: der 9. und der 23. sind halbe Tage, beide zählen.
    const noon = (attempted: number) => wp(attempted, {}, { startedAt: new Date(at(9)).toISOString(), deadline: new Date(at(23)).toISOString() });
    expect(woodpeckerSchedule(noon(100), at(18, 15))).toEqual(jasmine.objectContaining({ day: 10, days: 15, targetPct: 67, target: 180, left: 80 }));
    // D = k − 1 + Tage bis zur Frist — an JEDEM Tag des Zyklus (Kalender-Resttage des Tagesziels, calendarDaysUntil)
    for (let d = 9; d <= 23; d++) {
      const now = at(d, d === 23 ? 9 : 15);
      const s = woodpeckerSchedule(noon(0), now)!;
      expect(s.days).withContext(`Tag ${d}`).toBe(15);
      expect(s.day - 1 + calendarDaysUntil(at(23), now)).withContext(`Tag ${d}`).toBe(s.days);
      expect(s.day).withContext(`Tag ${d}`).toBe(calendarDaysSince(at(9), now));
    }
  });

  it('Zeitumstellung im Zyklus (25.10.2026): kein Tag mehr oder weniger', () => {
    const start = new Date(2026, 9, 20, 0, 0, 0).getTime();          // 20.10. 00:00 Ortszeit
    const deadline = new Date(2026, 9, 27, 0, 0, 0).getTime();       // 7 Kalendertage später (in Mitteleuropa 169 h)
    const p = wp(0, { setSize: 70 }, { startedAt: new Date(start).toISOString(), deadline: new Date(deadline).toISOString() });
    expect(woodpeckerSchedule(p, new Date(2026, 9, 26, 12, 0, 0).getTime())).toEqual(jasmine.objectContaining({ day: 7, days: 7, target: 70 }));
    expect(woodpeckerSchedule(p, new Date(2026, 9, 24, 12, 0, 0).getTime())).toEqual(jasmine.objectContaining({ day: 5, days: 7, target: 50, targetPct: 71 }));
  });

  it('das Soll rechnet exakt: geht Satz × Tag / Tage auf, wird nicht aufgerundet (300 × 7 / 14 = 150, nicht 151)', () => {
    expect(woodpeckerSchedule(wp(150, { setSize: 300 }), at(15, 12))).toEqual(jasmine.objectContaining({ day: 7, target: 150, targetPct: 50, reached: true }));
  });

  it('„aktuell“ zeigt 100 % erst, wenn nichts mehr offen ist (269 von 270 = 99 %)', () => {
    expect(woodpeckerSchedule(wp(269), at(16, 15))!.currentPct).toBe(99);
    expect(woodpeckerSchedule(wp(999), at(16, 15))).toEqual(jasmine.objectContaining({ attempted: 270, currentPct: 100, reached: true }));   // nie über den Satz
  });

  it('keine Anzeige: Zyklus 1, Pause/bereit/abgeschlossen/archiviert, beendeter Zyklus, ohne Plan', () => {
    const now = at(16, 15);
    expect(woodpeckerSchedule(plan([cycle({ start: at(9, 0), days: 28, attempted: 40 })], { setSize: 40 }), now)).toBeNull();   // Satz wächst noch
    for (const status of ['break', 'ready', 'completed', 'archived'] as const)
      expect(woodpeckerSchedule(wp(143, { status }), now)).withContext(status).toBeNull();
    expect(woodpeckerSchedule(wp(270, {}, { finishedAt: 'x' }), now)).toBeNull();
    expect(woodpeckerSchedule(null, now)).toBeNull();
    expect(woodpeckerSchedule(plan([]), now)).toBeNull();
  });

  it('keine Anzeige bei fehlenden oder unbrauchbaren Daten: Beginn, Frist, Satz, bearbeitet', () => {
    const now = at(16, 15);
    expect(woodpeckerSchedule(wp(143, {}, { startedAt: '' }), now)).toBeNull();
    expect(woodpeckerSchedule(wp(143, {}, { deadline: 'kein Datum' }), now)).toBeNull();
    expect(woodpeckerSchedule(wp(143, {}, { deadline: new Date(at(9, 0)).toISOString() }), now)).toBeNull();   // Frist nicht nach dem Beginn
    expect(woodpeckerSchedule(wp(143, { setSize: 0 }), now)).toBeNull();
    expect(woodpeckerSchedule(wp(143, { setSize: undefined as unknown as number }), now)).toBeNull();
    expect(woodpeckerSchedule(wp(undefined as unknown as number), now)).toBeNull();
  });

  it('braucht todayAttempted nicht (anders als das Tagesziel): auch ein Plan ohne das Feld hat einen Zeitplan', () => {
    const p = wp(143);
    expect(p.todayAttempted).toBeUndefined();
    expect(woodpeckerDailyGoal(p, at(16, 15))).toBeNull();
    expect(woodpeckerSchedule(p, at(16, 15))!.left).toBe(12);
  });

  it('Uhr vor dem Zyklusbeginn (verstellte Geräteuhr): Tag 1, nie 0', () => {
    expect(woodpeckerSchedule(wp(0), at(8, 12))).toEqual(jasmine.objectContaining({ day: 1, target: 20 }));
  });
});

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

  it('Zyklus 3: Zeit und Quote jetzt, dieselben Aufgaben je früherem Zyklus (jüngster zuerst), Tagesziel aus /stats, heute', () => {
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
    // Frist am 8. um 8 Uhr, jetzt der 5. um 12 Uhr → Kalendertage 5., 6., 7., 8. = 4; Soll = ⌈(60 offen + 12 heute) / 4⌉ = 18
    expect(o.goal).toEqual({ today: 12, target: 18, open: 60, days: 4, reached: false });
    expect(o.today).toBe(12);
    expect(o.overdue).toBeFalse();
    // Zeitplan mit dem Stand aus /stats (40, nicht 39 aus dem Plan): Beginn am 1. um 8 Uhr, Frist am 8. um 8 Uhr = 8
    // Kalendertage; heute Tag 5 → Soll ⌈100 × 5 / 8⌉ = 63 (63 %), noch 23.
    expect(o.schedule).toEqual({ day: 5, days: 8, targetPct: 63, target: 63, attempted: 40, setSize: 100, currentPct: 40, left: 23, reached: false, overdue: false });
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
    expect(o.goal).toEqual({ today: 7, target: null, open: null, days: null, reached: false });   // kein Soll
    expect(o.schedule).toBeNull();           // Zyklus 1: der Satz wächst noch, kein Anteil
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
    expect(o.goal).toBeNull();
    // Zeitplan nach der Frist: Soll = der ganze Satz, noch 100 offen
    expect(o.schedule).toEqual(jasmine.objectContaining({ targetPct: 100, target: 100, left: 100, reached: false, overdue: true }));
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
