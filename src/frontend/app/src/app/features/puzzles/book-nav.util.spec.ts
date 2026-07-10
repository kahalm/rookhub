import { formatUtcDate, shiftDailyDate, weeklyStartIndex, formatSecondsClock, formatEtaShort, estimateRemainingSeconds } from './book-nav.util';

describe('book-nav.util', () => {
  describe('formatUtcDate', () => {
    it('formatiert ein Datum als yyyyMMdd (UTC, nullgepolstert)', () => {
      expect(formatUtcDate(new Date(Date.UTC(2026, 6, 8)))).toBe('20260708');   // Monat 0-basiert → Juli
      expect(formatUtcDate(new Date(Date.UTC(2026, 0, 1)))).toBe('20260101');
    });
  });

  describe('shiftDailyDate', () => {
    it('verschiebt um ganze Tage (UTC), auch über Monats-/Jahresgrenzen', () => {
      expect(shiftDailyDate('20260708', -1)).toBe('20260707');
      expect(shiftDailyDate('20260708', 1)).toBe('20260709');
      expect(shiftDailyDate('20260731', 1)).toBe('20260801');
      expect(shiftDailyDate('20260101', -1)).toBe('20251231');
    });
  });

  describe('weeklyStartIndex', () => {
    const puzzles = [{ id: 10 }, { id: 11 }, { id: 12 }] as any;
    it('springt zum ersten noch nicht gespielten Puzzle', () => {
      expect(weeklyStartIndex(puzzles, { completed: false, playedIndices: [10] } as any)).toBe(1);
      expect(weeklyStartIndex(puzzles, { completed: false, playedIndices: [10, 11] } as any)).toBe(2);
    });
    it('0 ohne Fortschritt und bei 100 %', () => {
      expect(weeklyStartIndex(puzzles, { completed: false } as any)).toBe(0);
      expect(weeklyStartIndex(puzzles, { completed: true, playedIndices: [10, 11] } as any)).toBe(0);
      // alle gespielt, aber nicht als completed markiert → 0 (findIndex −1)
      expect(weeklyStartIndex(puzzles, { completed: false, playedIndices: [10, 11, 12] } as any)).toBe(0);
    });
  });

  describe('formatSecondsClock', () => {
    it('m:ss unter einer Stunde, h:mm:ss darüber', () => {
      expect(formatSecondsClock(0)).toBe('0:00');
      expect(formatSecondsClock(65)).toBe('1:05');
      expect(formatSecondsClock(600)).toBe('10:00');
      expect(formatSecondsClock(3661)).toBe('1:01:01');
      expect(formatSecondsClock(-5)).toBe('0:00');   // negativ → 0
      expect(formatSecondsClock(90.9)).toBe('1:30');  // floored
    });
  });

  describe('formatEtaShort', () => {
    it('rundet auf Minuten (mind. 1) bzw. Stunden+Minuten', () => {
      expect(formatEtaShort(0)).toBe('1 min');       // mind. 1 min
      expect(formatEtaShort(30)).toBe('1 min');
      expect(formatEtaShort(150)).toBe('3 min');     // 2,5 min → 3
      expect(formatEtaShort(60 * 60)).toBe('1 h');
      expect(formatEtaShort(80 * 60)).toBe('1 h 20 min');
    });
  });

  describe('estimateRemainingSeconds', () => {
    it('extrapoliert aus Zeit pro angefasstem Puzzle × offene Puzzles', () => {
      // 5 versucht, 300 s → 60 s/Puzzle; 10 gesamt, 4 gelöst → 6 offen → 360 s
      expect(estimateRemainingSeconds({ total: 10, solvedCount: 4, attemptedCount: 5, totalSeconds: 300 })).toBe(360);
    });
    it('liefert null wenn alles gelöst oder nichts versucht', () => {
      expect(estimateRemainingSeconds({ total: 10, solvedCount: 10, attemptedCount: 10, totalSeconds: 600 })).toBeNull();
      expect(estimateRemainingSeconds({ total: 10, solvedCount: 0, attemptedCount: 0, totalSeconds: 0 })).toBeNull();
    });
  });
});
