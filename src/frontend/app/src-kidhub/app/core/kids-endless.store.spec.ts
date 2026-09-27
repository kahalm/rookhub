import { TestBed } from '@angular/core/testing';
import { ENDLESS_KEEP_RUNS, KidsEndlessStore } from './kids-endless.store';

describe('KidsEndlessStore', () => {
  const KEY = 'rh-kids-endless-v1';
  beforeEach(() => { localStorage.removeItem(KEY); TestBed.resetTestingModule(); });
  afterEach(() => localStorage.removeItem(KEY));

  const run = (solved: number) => ({ at: solved, solved, maxRating: 800, firstMistakeRating: 900 });

  it('merkt Laeufe und Rekord und meldet einen neuen Rekord', () => {
    const s = TestBed.inject(KidsEndlessStore);
    expect(s.record(run(5))).toBeTrue();
    expect(s.record(run(3))).toBeFalse();
    expect(s.record(run(5))).toBeFalse();            // gleich viel ist kein neuer Rekord
    expect(s.best()).toBe(5);
    expect(s.runs().length).toBe(3);

    TestBed.resetTestingModule();
    expect(TestBed.inject(KidsEndlessStore).best()).toBe(5);   // nach dem Neuladen noch da
  });

  it('behaelt nur die letzten Laeufe', () => {
    const s = TestBed.inject(KidsEndlessStore);
    for (let i = 1; i <= ENDLESS_KEEP_RUNS + 5; i++) s.record(run(i));
    expect(s.runs().length).toBe(ENDLESS_KEEP_RUNS);
    expect(s.runs()[0].solved).toBe(6);
  });

  it('kaputter Speicher: frisch anfangen', () => {
    localStorage.setItem(KEY, '{kaputt');
    expect(TestBed.inject(KidsEndlessStore).best()).toBe(0);
  });
});
