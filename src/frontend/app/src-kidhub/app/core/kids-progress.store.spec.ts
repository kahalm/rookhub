import { TestBed } from '@angular/core/testing';
import { KidsProgressStore, starsFor } from './kids-progress.store';

describe('KidsProgressStore', () => {
  const KEY = 'rh-kids-progress-v1';

  beforeEach(() => {
    localStorage.removeItem(KEY);
    TestBed.resetTestingModule();
  });
  afterEach(() => localStorage.removeItem(KEY));

  const store = () => TestBed.inject(KidsProgressStore);

  it('Sterne: wenige Fehler = drei, geschafft ist immer mindestens einer', () => {
    expect([0, 1, 2, 4, 5, 20].map(starsFor)).toEqual([3, 3, 2, 2, 1, 1]);
  });

  it('Stufe 1 ist offen, die naechste erst nach einem geschafften Durchgang', () => {
    const s = store();
    expect(s.isUnlocked(1)).toBeTrue();
    expect(s.isUnlocked(2)).toBeFalse();

    s.recordSolved(1, 0);
    s.recordSolved(1, 2);
    expect(s.level(1).runIndex).toBe(2);
    expect(s.completeRun(1)).toBe(2);

    expect(s.isUnlocked(2)).toBeTrue();
    expect(s.level(1).runIndex).toBe(0);
    expect(s.completedLevels()).toBe(1);
    expect(s.totalStars()).toBe(2);
  });

  it('ein schlechterer Durchgang nimmt die besten Sterne nicht weg', () => {
    const s = store();
    s.completeRun(1);                 // 0 Fehler → 3
    s.recordSolved(1, 9);
    expect(s.completeRun(1)).toBe(1);
    expect(s.level(1).stars).toBe(3);
  });

  it('die aktuelle Stufe ist die erste ungeschaffte', () => {
    const s = store();
    expect(s.currentLevel([1, 2, 3])).toBe(1);
    s.completeRun(1);
    expect(s.currentLevel([3, 1, 2])).toBe(2);
    s.completeRun(2);
    s.completeRun(3);
    expect(s.currentLevel([1, 2, 3])).toBe(3);   // alles geschafft: die letzte
    expect(s.currentLevel([])).toBeNull();
  });

  it('bleibt nach dem Neuladen erhalten (localStorage)', () => {
    store().completeRun(1);
    store().recordCourseSolved(7, 42);
    TestBed.resetTestingModule();
    const again = store();
    expect(again.level(1).stars).toBe(3);
    expect(again.course(7).solved).toEqual([42]);
  });

  it('Kurs: jede Linie zaehlt einmal, „von vorn" leert', () => {
    const s = store();
    s.recordCourseSolved(7, 1);
    s.recordCourseSolved(7, 1);
    s.recordCourseSolved(7, 2);
    expect(s.course(7).solved).toEqual([1, 2]);
    s.resetCourse(7);
    expect(s.course(7).solved).toEqual([]);
  });

  it('kaputter Speicherinhalt: frisch anfangen statt abstuerzen', () => {
    localStorage.setItem(KEY, '{kaputt');
    expect(store().level(1).stars).toBe(0);
  });
});
