import { PUZZLE_THEME_PRESETS, isThemePresetActive } from './puzzle-theme-presets';

describe('puzzle-theme-presets', () => {
  it('defines presets with non-empty unique theme lists and unique label keys', () => {
    expect(PUZZLE_THEME_PRESETS.length).toBeGreaterThan(0);
    const labels = new Set<string>();
    for (const p of PUZZLE_THEME_PRESETS) {
      expect(p.themes.length).toBeGreaterThan(0);
      expect(new Set(p.themes).size).toBe(p.themes.length);   // keine Duplikate im Bündel
      expect(p.labelKey.startsWith('endless.themePreset.')).toBe(true);
      labels.add(p.labelKey);
    }
    expect(labels.size).toBe(PUZZLE_THEME_PRESETS.length);    // Label-Keys eindeutig
  });

  it('isThemePresetActive matches the exact set regardless of order', () => {
    const preset = { labelKey: 'x', themes: ['fork', 'pin', 'skewer'] };
    expect(isThemePresetActive(preset, ['skewer', 'fork', 'pin'])).toBe(true);
    expect(isThemePresetActive(preset, ['fork', 'pin'])).toBe(false);          // zu wenig
    expect(isThemePresetActive(preset, ['fork', 'pin', 'skewer', 'mateIn1'])).toBe(false); // zu viel
    expect(isThemePresetActive(preset, ['fork', 'pin', 'mateIn1'])).toBe(false); // falsches Thema
    expect(isThemePresetActive(preset, [])).toBe(false);
  });
});
