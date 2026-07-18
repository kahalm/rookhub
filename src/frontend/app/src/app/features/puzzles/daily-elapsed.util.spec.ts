import { loadDailyElapsed, saveDailyElapsed, clearDailyElapsed } from './daily-elapsed.util';

describe('daily-elapsed.util', () => {
  const KEY = 'rookhub_daily_elapsed';
  beforeEach(() => localStorage.removeItem(KEY));
  afterEach(() => localStorage.removeItem(KEY));

  it('save/load/clear roundtrip per date', () => {
    expect(loadDailyElapsed('20260718')).toBe(0);
    saveDailyElapsed('20260718', 120);
    saveDailyElapsed('20260717', 45);
    expect(loadDailyElapsed('20260718')).toBe(120);
    expect(loadDailyElapsed('20260717')).toBe(45);
    clearDailyElapsed('20260718');
    expect(loadDailyElapsed('20260718')).toBe(0);
    expect(loadDailyElapsed('20260717')).toBe(45);   // andere Daten bleiben
  });

  it('ignores non-positive values and floors fractions', () => {
    saveDailyElapsed('20260718', 0);
    expect(loadDailyElapsed('20260718')).toBe(0);
    saveDailyElapsed('20260718', 12.9);
    expect(loadDailyElapsed('20260718')).toBe(12);
  });

  it('keeps only the newest 14 dates (oldest pruned)', () => {
    for (let d = 1; d <= 16; d++) saveDailyElapsed(`202607${String(d).padStart(2, '0')}`, d);
    expect(loadDailyElapsed('20260701')).toBe(0);   // älteste raus
    expect(loadDailyElapsed('20260702')).toBe(0);
    expect(loadDailyElapsed('20260703')).toBe(3);   // jüngste 14 bleiben
    expect(loadDailyElapsed('20260716')).toBe(16);
  });

  it('survives corrupt storage content', () => {
    localStorage.setItem(KEY, '{kaputt');
    expect(loadDailyElapsed('20260718')).toBe(0);
    saveDailyElapsed('20260718', 30);               // überschreibt den Schrott
    expect(loadDailyElapsed('20260718')).toBe(30);
  });
});
