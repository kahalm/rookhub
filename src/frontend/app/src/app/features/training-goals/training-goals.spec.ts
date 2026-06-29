import {
  buildGoalTracker, statusLevel, toMinutes, orderHistory, isMinutesKind, breakdownRows,
  ymd, parseYmd, periodBounds, shiftAnchor, sumBreakdown, formatDuration,
} from './training-goals.component';
import { TrackerDay, SOURCE_KEYS, THEME_KEYS } from './training-goals.service';

describe('statusLevel', () => {
  it('maps full→4, partial→2, none→0', () => {
    expect(statusLevel('full')).toBe(4);
    expect(statusLevel('partial')).toBe(2);
    expect(statusLevel('none')).toBe(0);
  });
});

describe('toMinutes', () => {
  it('rounds seconds to nearest minute', () => {
    expect(toMinutes(0)).toBe(0);
    expect(toMinutes(29)).toBe(0);
    expect(toMinutes(30)).toBe(1);
    expect(toMinutes(90)).toBe(2);
    expect(toMinutes(600)).toBe(10);
  });
});

describe('formatDuration', () => {
  it('shows minutes up to 120 min (exclusive of the 2 h threshold)', () => {
    expect(formatDuration(0)).toEqual({ value: '0', unitKey: 'trainingGoals.min' });
    expect(formatDuration(90)).toEqual({ value: '2', unitKey: 'trainingGoals.min' });      // 90 s → 2 min
    expect(formatDuration(119 * 60)).toEqual({ value: '119', unitKey: 'trainingGoals.min' });
    expect(formatDuration(7199)).toEqual({ value: '120', unitKey: 'trainingGoals.min' });   // knapp unter 2 h
  });

  it('switches to hours from 120 min up to 48 h', () => {
    expect(formatDuration(120 * 60, 'en')).toEqual({ value: '2', unitKey: 'trainingGoals.hours' });
    expect(formatDuration(150 * 60, 'en')).toEqual({ value: '2.5', unitKey: 'trainingGoals.hours' });
    expect(formatDuration(47 * 3600, 'en')).toEqual({ value: '47', unitKey: 'trainingGoals.hours' });
  });

  it('switches to days from 48 h', () => {
    expect(formatDuration(48 * 3600, 'en')).toEqual({ value: '2', unitKey: 'trainingGoals.days' });
    expect(formatDuration(60 * 3600, 'en')).toEqual({ value: '2.5', unitKey: 'trainingGoals.days' });
  });

  it('honours the locale decimal separator', () => {
    expect(formatDuration(150 * 60, 'de').value).toBe('2,5');
  });

  it('clamps negatives to zero', () => {
    expect(formatDuration(-100)).toEqual({ value: '0', unitKey: 'trainingGoals.min' });
  });
});

describe('orderHistory', () => {
  const day = (date: string): TrackerDay => ({
    date, totalSeconds: 0,
    bySource: { randomPuzzleSeconds: 0, courseBookSeconds: 0, chessableSeconds: 0 },
    byTheme: { openingSeconds: 0, middlegameSeconds: 0, endgameSeconds: 0, tacticsSeconds: 0, otherSeconds: 0 },
    playGames: 0, status: 'none', hasManual: false,
  });

  it('returns days newest-first without mutating the input', () => {
    const input = [day('2026-06-01'), day('2026-06-02'), day('2026-06-03')];
    const out = orderHistory(input);
    expect(out.map(d => d.date)).toEqual(['2026-06-03', '2026-06-02', '2026-06-01']);
    // Eingabe bleibt unangetastet (aufsteigend).
    expect(input.map(d => d.date)).toEqual(['2026-06-01', '2026-06-02', '2026-06-03']);
  });

  it('handles an empty list', () => {
    expect(orderHistory([])).toEqual([]);
  });
});

describe('buildGoalTracker', () => {
  const today = new Date(2026, 5, 2); // 2 Jun 2026
  const tk = '2026-06-02';

  it('builds a weeks × 7 grid', () => {
    const grid = buildGoalTracker([], today, 4);
    expect(grid.length).toBe(4);
    expect(grid.every(w => w.length === 7)).toBeTrue();
  });

  it('places the status on the matching day with the right level', () => {
    const grid = buildGoalTracker([{ date: tk, status: 'full' }], today, 4);
    const cell = grid.flat().find(c => c.date === tk)!;
    expect(cell.status).toBe('full');
    expect(cell.level).toBe(4);
  });

  it('maps a partial day to level 2', () => {
    const grid = buildGoalTracker([{ date: tk, status: 'partial' }], today, 4);
    expect(grid.flat().find(c => c.date === tk)!.level).toBe(2);
  });

  it('marks days after today as level -1, others >= 0', () => {
    const grid = buildGoalTracker([], today, 4);
    for (const c of grid.flat()) {
      if (c.date > tk) expect(c.level).toBe(-1);
      else expect(c.level).toBeGreaterThanOrEqual(0);
    }
  });

  it('flags days with manual activity', () => {
    const grid = buildGoalTracker([{ date: tk, status: 'none', hasManual: true }], today, 4);
    const cell = grid.flat().find(c => c.date === tk)!;
    expect(cell.manual).toBeTrue();
    // a day without manual activity is not flagged
    expect(grid.flat().filter(c => c.manual).length).toBe(1);
  });
});

describe('breakdownRows', () => {
  it('returns only non-zero buckets with share-of-total percent, in key order', () => {
    const rows = breakdownRows(
      { randomPuzzleSeconds: 300, courseBookSeconds: 0, chessableSeconds: 100 },
      SOURCE_KEYS);
    expect(rows.map(r => r.label)).toEqual(['randomPuzzle', 'chessable']); // courseBook (0) gefiltert
    expect(rows[0].seconds).toBe(300);
    expect(rows[0].pct).toBe(75);   // 300 / 400
    expect(rows[1].pct).toBe(25);
  });

  it('returns an empty list when everything is zero', () => {
    const rows = breakdownRows(
      { openingSeconds: 0, middlegameSeconds: 0, endgameSeconds: 0, tacticsSeconds: 0, otherSeconds: 0 },
      THEME_KEYS);
    expect(rows).toEqual([]);
  });
});

describe('ymd / parseYmd', () => {
  it('round-trips a local date to yyyy-MM-dd and back', () => {
    expect(ymd(new Date(2026, 0, 5))).toBe('2026-01-05'); // zero-padded month/day
    expect(ymd(parseYmd('2026-12-31'))).toBe('2026-12-31');
  });
});

describe('periodBounds', () => {
  it('day → start === end === anchor', () => {
    expect(periodBounds('day', '2026-06-23', '2026-01-01', '2026-06-30'))
      .toEqual({ start: '2026-06-23', end: '2026-06-23' });
  });

  it('week → Monday..Sunday containing the anchor', () => {
    // 2026-06-23 is a Tuesday → week is Mon 22nd .. Sun 28th
    expect(periodBounds('week', '2026-06-23', '2026-01-01', '2026-06-30'))
      .toEqual({ start: '2026-06-22', end: '2026-06-28' });
  });

  it('month → first..last day of the anchor month', () => {
    expect(periodBounds('month', '2026-02-15', '2026-01-01', '2026-06-30'))
      .toEqual({ start: '2026-02-01', end: '2026-02-28' }); // 2026 not a leap year
  });

  it('year → Jan 1..Dec 31 of the anchor year', () => {
    expect(periodBounds('year', '2026-06-23', '2026-01-01', '2026-06-30'))
      .toEqual({ start: '2026-01-01', end: '2026-12-31' });
  });

  it('all → firstDate..today', () => {
    expect(periodBounds('all', '2026-06-23', '2025-03-04', '2026-06-30'))
      .toEqual({ start: '2025-03-04', end: '2026-06-30' });
  });

  it('all → today..today when there is no history', () => {
    expect(periodBounds('all', '2026-06-23', '', '2026-06-30'))
      .toEqual({ start: '2026-06-30', end: '2026-06-30' });
  });
});

describe('shiftAnchor', () => {
  it('steps a day', () => {
    expect(shiftAnchor('day', '2026-06-23', -1)).toBe('2026-06-22');
    expect(shiftAnchor('day', '2026-06-23', 1)).toBe('2026-06-24');
  });
  it('steps a week by 7 days', () => {
    expect(shiftAnchor('week', '2026-06-23', -1)).toBe('2026-06-16');
  });
  it('steps a month to the 1st, no day overflow across short months', () => {
    expect(shiftAnchor('month', '2026-03-31', -1)).toBe('2026-02-01'); // not back into March
    expect(shiftAnchor('month', '2026-01-15', 1)).toBe('2026-02-01');
    expect(shiftAnchor('month', '2026-01-10', -1)).toBe('2025-12-01'); // year boundary
  });
  it('steps a year to Jan 1', () => {
    expect(shiftAnchor('year', '2026-06-23', -1)).toBe('2025-01-01');
  });
});

describe('sumBreakdown', () => {
  const day = (date: string, rp: number, op: number): TrackerDay => ({
    date, totalSeconds: rp,
    bySource: { randomPuzzleSeconds: rp, courseBookSeconds: 0, chessableSeconds: 0 },
    byTheme: { openingSeconds: op, middlegameSeconds: 0, endgameSeconds: 0, tacticsSeconds: 0, otherSeconds: 0 },
    playGames: 0, status: 'none', hasManual: false,
  });

  it('sums only days within [start,end] inclusive', () => {
    const days = [day('2026-06-01', 100, 10), day('2026-06-15', 200, 20), day('2026-07-01', 400, 40)];
    const { bySource, byTheme } = sumBreakdown(days, '2026-06-01', '2026-06-30');
    expect(bySource.randomPuzzleSeconds).toBe(300); // 100 + 200, July excluded
    expect(byTheme.openingSeconds).toBe(30);
  });

  it('returns zeros when no day matches', () => {
    const { bySource } = sumBreakdown([day('2026-06-01', 100, 10)], '2026-07-01', '2026-07-31');
    expect(bySource.randomPuzzleSeconds).toBe(0);
  });
});

describe('isMinutesKind', () => {
  it('treats OTB games as a count, everything else as minutes', () => {
    expect(isMinutesKind('OtbGame')).toBeFalse();
    expect(isMinutesKind('OfflinePuzzle')).toBeTrue();
    expect(isMinutesKind('OfflineStudy')).toBeTrue();
    expect(isMinutesKind('Coaching')).toBeTrue();
  });
});
