import { formatUtcDate, shiftDailyDate, weeklyStartIndex, formatSecondsClock } from './book-nav.util';

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
});
