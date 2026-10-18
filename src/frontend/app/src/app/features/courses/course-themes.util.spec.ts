import { setThemeKeys } from './course-themes.util';

/** d3-rk-kurs-thema-taktik-vorgabe: EINE Regel für Kurskarte und Kursdetail. */
describe('setThemeKeys', () => {
  it('ohne Themen oder nur mit der Vorgabe „tactics“ → keine Abzeichen', () => {
    expect(setThemeKeys(undefined)).toEqual([]);
    expect(setThemeKeys([])).toEqual([]);
    expect(setThemeKeys(['tactics'])).toEqual([]);
  });

  it('gesetzte Themen → ihre Schlüssel (auch Taktik neben anderen)', () => {
    expect(setThemeKeys(['openings'])).toEqual(['trainingGoals.theme.openings']);
    expect(setThemeKeys(['endgames', 'tactics'])).toEqual(['trainingGoals.theme.endgames', 'trainingGoals.theme.tactics']);
  });
});
