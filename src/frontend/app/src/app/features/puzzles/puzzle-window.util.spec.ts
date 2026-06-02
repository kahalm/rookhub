import { puzzleWindow, DIFFICULTY_OFFSET, RATING_WINDOW } from './puzzle-window.util';

describe('puzzleWindow', () => {
  it('centers on elo + difficulty offset, ±RATING_WINDOW', () => {
    expect(puzzleWindow(1500, 'normal', null)).toEqual({ min: 1400, max: 1600 });
    expect(puzzleWindow(1500, 'schwer', null)).toEqual({ min: 1700, max: 1900 });   // +300
    expect(puzzleWindow(1500, 'leicht', null)).toEqual({ min: 1100, max: 1300 });   // -300
  });

  it('falls back to offset 0 for an unknown difficulty', () => {
    expect(puzzleWindow(1500, 'unbekannt', null)).toEqual({ min: 1400, max: 1600 });
  });

  it('clamps the center so the window stays within the DB rating bounds', () => {
    // sehr_schwer (+600) → Zentrum 2100, aber bounds.max 2000 → Zentrum auf 1900 geklemmt
    expect(puzzleWindow(1500, 'sehr_schwer', { min: 800, max: 2000 }))
      .toEqual({ min: 1800, max: 2000 });
  });

  it('never goes below rating 0', () => {
    const w = puzzleWindow(300, 'sehr_leicht', null);   // 300 - 600 = -300 → min 0
    expect(w.min).toBe(0);
  });

  it('exposes the expected constants', () => {
    expect(RATING_WINDOW).toBe(100);
    expect(DIFFICULTY_OFFSET['normal']).toBe(0);
    expect(DIFFICULTY_OFFSET['sehr_schwer']).toBe(600);
  });
});
