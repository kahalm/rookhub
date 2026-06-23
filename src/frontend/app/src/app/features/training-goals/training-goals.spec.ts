import { buildGoalTracker, statusLevel, toMinutes, orderHistory, isMinutesKind, breakdownRows } from './training-goals.component';
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

describe('isMinutesKind', () => {
  it('treats OTB games as a count, everything else as minutes', () => {
    expect(isMinutesKind('OtbGame')).toBeFalse();
    expect(isMinutesKind('OfflinePuzzle')).toBeTrue();
    expect(isMinutesKind('OfflineStudy')).toBeTrue();
    expect(isMinutesKind('Coaching')).toBeTrue();
  });
});
