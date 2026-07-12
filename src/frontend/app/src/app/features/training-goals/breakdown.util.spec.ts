import { ymd, parseYmd, periodBounds, shiftAnchor, sumBreakdown, breakdownRows } from './breakdown.util';
import { TrackerDay } from './training-goals.service';

function day(date: string, rnd = 0, course = 0, chess = 0): TrackerDay {
  return {
    date,
    bySource: { randomPuzzleSeconds: rnd, courseBookSeconds: course, chessableSeconds: chess },
    byTheme: { openingSeconds: 0, middlegameSeconds: 0, endgameSeconds: 0, tacticsSeconds: 0, otherSeconds: 0 },
  } as TrackerDay;
}

describe('breakdown.util', () => {
  it('ymd / parseYmd round-trip (local midnight)', () => {
    expect(ymd(parseYmd('2026-03-31'))).toBe('2026-03-31');
    expect(ymd(new Date(2026, 0, 5))).toBe('2026-01-05');
  });

  describe('periodBounds', () => {
    it('day = single day', () => {
      expect(periodBounds('day', '2026-07-08', '2026-01-01', '2026-07-12')).toEqual({ start: '2026-07-08', end: '2026-07-08' });
    });
    it('week = Monday..Sunday containing anchor', () => {
      // 2026-07-08 is a Wednesday
      expect(periodBounds('week', '2026-07-08', '2026-01-01', '2026-07-12')).toEqual({ start: '2026-07-06', end: '2026-07-12' });
    });
    it('month = first..last of the month', () => {
      expect(periodBounds('month', '2026-02-15', '2026-01-01', '2026-07-12')).toEqual({ start: '2026-02-01', end: '2026-02-28' });
    });
    it('year = Jan 1..Dec 31', () => {
      expect(periodBounds('year', '2026-07-08', '2026-01-01', '2026-07-12')).toEqual({ start: '2026-01-01', end: '2026-12-31' });
    });
    it('all = firstDate..today', () => {
      expect(periodBounds('all', '2026-07-08', '2025-05-01', '2026-07-12')).toEqual({ start: '2025-05-01', end: '2026-07-12' });
    });
  });

  describe('shiftAnchor', () => {
    it('day/week step by 1/7 days', () => {
      expect(shiftAnchor('day', '2026-07-08', -1)).toBe('2026-07-07');
      expect(shiftAnchor('week', '2026-07-08', 1)).toBe('2026-07-15');
    });
    it('month/year normalize to the period start (no day overflow)', () => {
      expect(shiftAnchor('month', '2026-03-31', -1)).toBe('2026-02-01');
      expect(shiftAnchor('year', '2026-07-08', 1)).toBe('2027-01-01');
    });
    it('all does not step', () => {
      expect(shiftAnchor('all', '2026-07-08', -1)).toBe('2026-07-08');
    });
  });

  it('sumBreakdown only counts days within [start,end]', () => {
    const days = [day('2026-07-05', 10), day('2026-07-08', 20), day('2026-07-20', 100)];
    const { bySource } = sumBreakdown(days, '2026-07-06', '2026-07-12');
    expect(bySource.randomPuzzleSeconds).toBe(20);
  });

  it('breakdownRows filters empty buckets + computes percentage of the bucket total', () => {
    const rows = breakdownRows({ a: 30, b: 10, c: 0 }, [{ key: 'a', label: 'A' }, { key: 'b', label: 'B' }, { key: 'c', label: 'C' }]);
    expect(rows.map(r => r.label)).toEqual(['A', 'B']);
    expect(rows[0]).toEqual({ label: 'A', seconds: 30, pct: 75 });
  });
});
