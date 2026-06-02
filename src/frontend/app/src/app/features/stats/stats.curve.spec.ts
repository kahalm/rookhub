import { buildEloCurve, buildCurvesPerLevel, buildHeatmap, heatLevel } from './stats.component';
import { EloHistoryPoint } from '../puzzles/puzzle.service';

function pt(elo: number, day: number, vizLevel = 0): EloHistoryPoint {
  return { elo, attemptedAt: `2026-01-${String(day).padStart(2, '0')}T12:00:00`, vizLevel, solved: true };
}

describe('buildEloCurve', () => {
  it('returns null for fewer than 2 points', () => {
    expect(buildEloCurve([])).toBeNull();
    expect(buildEloCurve([pt(1500, 1)])).toBeNull();
  });

  it('computes min/max and a polyline with one coord pair per point', () => {
    const c = buildEloCurve([pt(1500, 1), pt(1560, 2), pt(1520, 3)])!;
    expect(c.minElo).toBe(1500);
    expect(c.maxElo).toBe(1560);
    expect(c.poly.trim().split(/\s+/).length).toBe(3);
  });

  it('maps the highest elo to the top (smallest y) and lowest to the bottom', () => {
    const c = buildEloCurve([pt(1400, 1), pt(1800, 2)])!;
    const [p0, p1] = c.poly.split(' ').map(s => s.split(',').map(Number));
    expect(p1[1]).toBeLessThan(p0[1]); // 1800 (höher) -> kleinere y-Koordinate (weiter oben)
  });

  it('handles a flat series without dividing by zero', () => {
    const c = buildEloCurve([pt(1500, 1), pt(1500, 2)])!;
    expect(c.poly).toContain(',');
    expect(Number.isFinite(c.minElo)).toBeTrue();
  });
});

describe('buildCurvesPerLevel', () => {
  it('builds one curve per visualization level (sorted), each with >= 2 points', () => {
    const res = buildCurvesPerLevel([
      pt(1500, 1, 0), pt(1520, 2, 0),
      pt(1400, 1, 2), pt(1450, 2, 2), pt(1460, 3, 2),
    ]);
    expect(res.map(r => r.level)).toEqual([0, 2]);
    expect(res[0].curve.poly.trim().split(/\s+/).length).toBe(2);
    expect(res[1].curve.poly.trim().split(/\s+/).length).toBe(3);
  });

  it('drops levels with fewer than 2 points', () => {
    const res = buildCurvesPerLevel([
      pt(1500, 1, 0), pt(1520, 2, 0),
      pt(1400, 1, 1),                    // nur 1 Punkt → kein Graph
    ]);
    expect(res.map(r => r.level)).toEqual([0]);
  });

  it('returns empty when no level has enough data', () => {
    expect(buildCurvesPerLevel([pt(1500, 1, 0), pt(1400, 1, 1)])).toEqual([]);
  });
});

describe('heatLevel', () => {
  it('buckets counts into 0–4', () => {
    expect(heatLevel(0)).toBe(0);
    expect(heatLevel(2)).toBe(1);
    expect(heatLevel(5)).toBe(2);
    expect(heatLevel(9)).toBe(3);
    expect(heatLevel(20)).toBe(4);
  });
});

describe('buildHeatmap', () => {
  const today = new Date(2026, 5, 2); // 2 Jun 2026
  const tk = '2026-06-02';

  it('builds a weeks × 7 grid', () => {
    const grid = buildHeatmap([], today, 4);
    expect(grid.length).toBe(4);
    expect(grid.every(w => w.length === 7)).toBeTrue();
  });

  it('places the count on the matching day with the right level', () => {
    const grid = buildHeatmap([{ date: tk, count: 5 }], today, 4);
    const cell = grid.flat().find(c => c.date === tk)!;
    expect(cell.count).toBe(5);
    expect(cell.level).toBe(2);
  });

  it('marks days after today as level -1, others >= 0', () => {
    const grid = buildHeatmap([], today, 4);
    for (const c of grid.flat()) {
      if (c.date > tk) expect(c.level).toBe(-1);
      else expect(c.level).toBeGreaterThanOrEqual(0);
    }
  });
});
