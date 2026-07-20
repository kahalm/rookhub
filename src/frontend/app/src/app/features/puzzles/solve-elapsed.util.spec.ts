import { loadSolveElapsed, saveSolveElapsed, clearSolveElapsed } from './solve-elapsed.util';

describe('solve-elapsed.util', () => {
  const KEY = 'rookhub_solve_elapsed';
  beforeEach(() => localStorage.removeItem(KEY));
  afterEach(() => localStorage.removeItem(KEY));

  it('save/load/clear roundtrip per key', () => {
    expect(loadSolveElapsed('course:1')).toBe(0);
    saveSolveElapsed('course:1', 120);
    saveSolveElapsed('course:2', 45);
    expect(loadSolveElapsed('course:1')).toBe(120);
    expect(loadSolveElapsed('course:2')).toBe(45);
    clearSolveElapsed('course:1');
    expect(loadSolveElapsed('course:1')).toBe(0);
    expect(loadSolveElapsed('course:2')).toBe(45);   // andere Einträge bleiben
  });

  it('ignores non-positive values and floors fractions', () => {
    saveSolveElapsed('course:1', 0);
    expect(loadSolveElapsed('course:1')).toBe(0);
    saveSolveElapsed('course:1', 12.9);
    expect(loadSolveElapsed('course:1')).toBe(12);
  });

  it('keeps only the newest entries by write time (oldest pruned)', () => {
    let t = 1000;
    spyOn(Date, 'now').and.callFake(() => (t += 1000));
    for (let i = 1; i <= 32; i++) saveSolveElapsed(`course:${i}`, i);
    expect(loadSolveElapsed('course:1')).toBe(0);    // älteste raus
    expect(loadSolveElapsed('course:2')).toBe(0);
    expect(loadSolveElapsed('course:3')).toBe(3);    // jüngste 30 bleiben
    expect(loadSolveElapsed('course:32')).toBe(32);
  });

  it('survives corrupt storage content gracefully', () => {
    localStorage.setItem(KEY, '{kaputt');
    expect(loadSolveElapsed('course:1')).toBe(0);
    saveSolveElapsed('course:1', 7);                 // überschreibt den Schrott
    expect(loadSolveElapsed('course:1')).toBe(7);
  });
});
