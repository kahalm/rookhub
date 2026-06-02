import { buildEloCurve } from './stats.component';
import { EloHistoryPoint } from '../puzzles/puzzle.service';

function pt(elo: number, day: number): EloHistoryPoint {
  return { elo, attemptedAt: `2026-01-${String(day).padStart(2, '0')}T12:00:00`, vizLevel: 0, solved: true };
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
