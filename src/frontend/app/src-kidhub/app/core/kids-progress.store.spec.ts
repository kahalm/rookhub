import { TestBed } from '@angular/core/testing';
import { KidsProgressDto, KidsProgressStore, mergeProgress, starsFor } from './kids-progress.store';

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

  it('merkt, wann sich Durchgang und Kurs-Linien geaendert haben (fuer den Abgleich)', () => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(5000));
    try {
      const s = store();
      const before = s.revision();
      s.recordSolved(1, 1);
      s.recordCourseSolved(7, 42);
      expect(s.level(1).runAt).toBe(5000);
      expect(s.course(7).solvedAt).toEqual({ 42: 5000 });
      expect(s.revision()).toBe(before + 2);
      s.resetCourse(7);
      expect(s.course(7)).toEqual({ solved: [], solvedAt: {}, resetAt: 5000 });
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('toDto: Linien von vor dem Abgleich (ohne Zeit) gelten als 1 ms', () => {
    localStorage.setItem(KEY, JSON.stringify({ levels: { 1: { stars: 2, runIndex: 3, runMistakes: 1 } }, courses: { 7: { solved: [4, 9] } } }));
    expect(store().toDto()).toEqual({
      levels: [{ level: 1, stars: 2, runIndex: 3, runMistakes: 1, runAt: 0 }],
      courses: [{ bookId: 7, resetAt: 0, solved: [{ id: 4, at: 1 }, { id: 9, at: 1 }] }],
    });
  });

  it('adopt fuehrt zusammen statt zu ersetzen und merkt das Konto; keine Aenderung durch das Kind', () => {
    const s = store();
    s.recordCourseSolved(7, 1);                       // waehrend der Anfrage lokal geloest
    const rev = s.revision();
    s.adopt({ levels: [{ level: 1, stars: 3, runIndex: 0, runMistakes: 0, runAt: 10 }], courses: [{ bookId: 7, resetAt: 0, solved: [{ id: 2, at: 5 }] }] }, 42);
    expect(s.level(1).stars).toBe(3);
    expect(s.course(7).solved.sort()).toEqual([1, 2]);
    expect(s.owner()).toBe(42);
    expect(s.revision()).toBe(rev);
    s.clear();
    expect(s.owner()).toBeNull();
    expect(s.totalStars()).toBe(0);
  });

  // ---- mergeProgress: dieselben LITERALEN Faelle wie KidsProgressTests (Server) ----
  const L = (level: number, stars: number, runIndex: number, runMistakes: number, runAt: number) => ({ level, stars, runIndex, runMistakes, runAt });
  const C = (bookId: number, resetAt: number, ...solved: [number, number][]) => ({ bookId, resetAt, solved: solved.map(([id, at]) => ({ id, at })) });
  const P = (levels: KidsProgressDto['levels'], courses: KidsProgressDto['courses'] = []): KidsProgressDto => ({ levels, courses });
  const show = (p: KidsProgressDto) =>
    p.levels.map(l => `L${l.level}:${l.stars}/${l.runIndex}/${l.runMistakes}@${l.runAt}`).join(' ')
    + ' | ' + p.courses.map(c => `C${c.bookId}@${c.resetAt}[${c.solved.map(x => `${x.id}@${x.at}`).join(',')}]`).join(' ');

  it('A: Sterne nach Hoehe, Durchgang der juengere', () => {
    expect(show(mergeProgress(P([L(1, 2, 3, 1, 1000)]), P([L(1, 3, 0, 0, 2000), L(2, 0, 4, 2, 1500)]))))
      .toBe('L1:3/0/0@2000 L2:0/4/2@1500 | ');
  });

  it('A2: aelterer Durchgang mit hoeheren Sternen — beides bleibt', () => {
    expect(show(mergeProgress(P([L(1, 3, 2, 0, 5000)]), P([L(1, 1, 6, 4, 4000)])))).toBe('L1:3/2/0@5000 | ');
  });

  it('B: gleiche Zeit — der erste gewinnt', () => {
    expect(show(mergeProgress(P([L(1, 1, 5, 2, 3000)]), P([L(1, 0, 7, 0, 3000)])))).toBe('L1:1/5/2@3000 | ');
  });

  it('C: Linien vereinigt, „von vorn" wirft aeltere weg', () => {
    expect(show(mergeProgress(P([], [C(5, 0, [10, 100], [11, 200])]), P([], [C(5, 150, [12, 300]), C(6, 0, [20, 50])]))))
      .toBe(' | C5@150[11@200,12@300] C6@0[20@50]');
  });

  it('D: nach dem „von vorn" wieder geloest — zaehlt mit juengster Zeit', () => {
    expect(show(mergeProgress(P([], [C(5, 0, [10, 100])]), P([], [C(5, 150, [10, 400])])))).toBe(' | C5@150[10@400]');
  });

  it('E: leerer Kurs ohne „von vorn" faellt weg', () => {
    expect(show(mergeProgress(P([], [C(7, 0)]), P([])))).toBe(' | ');
  });
});
