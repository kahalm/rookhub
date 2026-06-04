import { buildGoalTracker, statusLevel } from './training-goals.component';

describe('statusLevel', () => {
  it('maps full→4, partial→2, none→0', () => {
    expect(statusLevel('full')).toBe(4);
    expect(statusLevel('partial')).toBe(2);
    expect(statusLevel('none')).toBe(0);
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
});
