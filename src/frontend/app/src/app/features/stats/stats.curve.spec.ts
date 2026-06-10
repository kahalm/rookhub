import { buildEloCurve, buildOverlay, LEVEL_COLORS, buildHeatmap, heatLevel, smoothPath, smoothSeries } from './stats.component';
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

  it('exposes a smoothed bezier path through the points', () => {
    const c = buildEloCurve([pt(1500, 1), pt(1560, 2), pt(1520, 3)])!;
    expect(c.path.startsWith('M')).toBeTrue();
    expect(c.path).toContain('C');                 // kubische Béziers = geglättet
    expect(c.path).not.toContain('NaN');
  });

  it('keeps axis min/max from the raw series even though the line is pre-smoothed', () => {
    // Spitze bei 2000 wird durch den gleitenden Mittel gedämpft, die Achse zeigt aber das Roh-Maximum.
    const c = buildEloCurve([pt(1500, 1), pt(1500, 2), pt(2000, 3), pt(1500, 4), pt(1500, 5)])!;
    expect(c.minElo).toBe(1500);
    expect(c.maxElo).toBe(2000);
    expect(c.path).not.toContain('NaN');
  });
});

describe('smoothSeries', () => {
  it('returns a copy unchanged for fewer than 3 values', () => {
    expect(smoothSeries([1500])).toEqual([1500]);
    expect(smoothSeries([1500, 1600])).toEqual([1500, 1600]);
  });

  it('preserves length and dampens a single spike toward its neighbours', () => {
    const out = smoothSeries([1500, 1500, 2000, 1500, 1500]);
    expect(out.length).toBe(5);
    expect(out[2]).toBeLessThan(2000);             // Spitze gedämpft
    expect(out[2]).toBeGreaterThan(1500);
  });

  it('leaves a constant series unchanged', () => {
    expect(smoothSeries([1500, 1500, 1500, 1500])).toEqual([1500, 1500, 1500, 1500]);
  });
});

describe('smoothPath', () => {
  it('returns empty for fewer than 2 points', () => {
    expect(smoothPath([])).toBe('');
    expect(smoothPath([{ x: 0, y: 0 }])).toBe('');
  });

  it('uses a straight line segment for exactly 2 points', () => {
    const d = smoothPath([{ x: 0, y: 10 }, { x: 20, y: 30 }]);
    expect(d).toBe('M0.0,10.0 L20.0,30.0');
  });

  it('builds one cubic bezier per segment for 3+ points', () => {
    const d = smoothPath([{ x: 0, y: 0 }, { x: 10, y: 10 }, { x: 20, y: 0 }]);
    expect(d.startsWith('M0.0,0.0')).toBeTrue();
    expect((d.match(/C/g) ?? []).length).toBe(2);   // 3 Punkte → 2 Segmente
  });
});

describe('buildOverlay', () => {
  it('builds one colored line per level (sorted), shared scale + global date range', () => {
    const o = buildOverlay([
      pt(1500, 1, 0), pt(1520, 5, 0),
      pt(1400, 2, 2), pt(1450, 3, 2), pt(1460, 4, 2),
    ])!;
    expect(o.lines.map(l => l.level)).toEqual([0, 2]);
    expect(o.lines[0].color).toBe(LEVEL_COLORS[0]);
    expect(o.lines[1].color).toBe(LEVEL_COLORS[2]);
    // gemeinsame Y-Skala über ALLE Punkte
    expect(o.minElo).toBe(1400);
    expect(o.maxElo).toBe(1520);
    // ein Punktpaar je Datenpunkt
    expect(o.lines[0].poly.trim().split(/\s+/).length).toBe(2);
    expect(o.lines[1].poly.trim().split(/\s+/).length).toBe(3);
    // jede Linie hat einen geglätteten Pfad
    expect(o.lines.every(l => l.path.startsWith('M'))).toBeTrue();
  });

  it('drops levels with fewer than 2 points', () => {
    const o = buildOverlay([
      pt(1500, 1, 0), pt(1520, 2, 0),
      pt(1400, 1, 1),                    // nur 1 Punkt → keine Linie
    ])!;
    expect(o.lines.map(l => l.level)).toEqual([0]);
  });

  it('returns null when no level has enough data', () => {
    expect(buildOverlay([pt(1500, 1, 0), pt(1400, 1, 1)])).toBeNull();
    expect(buildOverlay([])).toBeNull();
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
