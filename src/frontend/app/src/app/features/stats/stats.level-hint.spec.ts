import { of } from 'rxjs';
import { StatsComponent, emptyLevelHint } from './stats.component';

/** UI-Sweep 2026-10-11 (r2-rp-elo-level): leeres bevorzugtes Level → „Alle" + Hinweis auf das Level mit Daten. */
describe('Stats – Level ohne Versuche', () => {
  const pt = (vizLevel: number) => ({ attemptedAt: '2026-10-01T10:00:00Z', elo: 1600, vizLevel, solved: true });

  it('emptyLevelHint nennt das Level mit den meisten Versuchen, nur wenn das gewählte leer ist', () => {
    const pts = [pt(0), pt(0), pt(2)];
    expect(emptyLevelHint(pts, 1)).toEqual({ level: 1, other: 0, count: 2 });
    expect(emptyLevelHint(pts, 0)).toBeNull();
    expect(emptyLevelHint(pts, null)).toBeNull();
    expect(emptyLevelHint([], 1)).toBeNull();
  });

  function load(visualization: number, elo: any[]) {
    const breakdown = { themes: [], ratingBands: [], activity: [] };
    const puzzles = {
      getStats: () => of({ totalAttempts: 3, solved: 3, accuracy: 100, currentStreak: 3, bestStreak: 3, puzzleElo: 1630 }),
      getHistory: () => of([]), getEloHistory: () => of(elo), getBreakdown: () => of(breakdown),
    };
    const c = new StatsComponent(puzzles as any, { visualization } as any);
    c.ngOnInit();
    return c;
  }

  it('weicht beim Laden auf „Alle" aus und zeigt den Hinweis; Klick wechselt aufs Level mit Daten', () => {
    const c = load(1, [pt(0), pt(0)]);
    expect(c.level).toBe(-1);
    expect(c.overlay).not.toBeNull();
    expect(c.levelHint).toEqual({ level: 1, other: 0, count: 2 });

    c.showLevel(0);
    expect(c.level).toBe(0);
    expect(c.curve).not.toBeNull();
    expect(c.levelHint).toBeNull();
  });

  it('bleibt beim bevorzugten Level, wenn es Versuche hat', () => {
    const c = load(1, [pt(1), pt(1)]);
    expect(c.level).toBe(1);
    expect(c.levelHint).toBeNull();
  });
});
