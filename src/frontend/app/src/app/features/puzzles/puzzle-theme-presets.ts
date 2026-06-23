/**
 * Kuratierte Themen-Bündel für die Schnellauswahl im Endless-Modus.
 *
 * Endless filtert mehrere Themen mit ODER (`themesAny`, seit v0.99.1) → ein Klick auf einen
 * Preset-Chip setzt `config.themes` auf das Bündel, und ein Puzzle muss nur EINES der Themen
 * tragen. Die Theme-Keys entsprechen den Lichess-Puzzle-Tags; Pool-Größen (dev-DB 2026-06-23,
 * PuzzleTags voll backfilled) stehen als Orientierung im Kommentar.
 */
export interface ThemePreset {
  /** i18n-Key des Chip-Labels (endless.themePreset.*). */
  labelKey: string;
  /** Puzzle-Theme-Keys, die der Preset setzt. */
  themes: string[];
}

export const PUZZLE_THEME_PRESETS: ThemePreset[] = [
  { labelKey: 'endless.themePreset.blitzMate',    themes: ['mateIn1'] },                                              // ≈698k
  { labelKey: 'endless.themePreset.mateHunt',     themes: ['mateIn1', 'mateIn2'] },                                   // ≈1,37M
  { labelKey: 'endless.themePreset.basicTactics', themes: ['fork', 'pin', 'skewer'] },                                // ≈1,17M
  { labelKey: 'endless.themePreset.combinations', themes: ['sacrifice', 'deflection', 'attraction', 'clearance', 'interference'] }, // ≈950k
  { labelKey: 'endless.themePreset.patternMates', themes: ['backRankMate', 'smotheredMate', 'arabianMate', 'anastasiaMate', 'bodenMate', 'operaMate', 'hookMate', 'epauletteMate', 'dovetailMate'] }, // ≈300k
  { labelKey: 'endless.themePreset.endgame',      themes: ['rookEndgame', 'pawnEndgame', 'queenEndgame', 'knightEndgame', 'bishopEndgame'] }, // ≈667k
];

/** True, wenn die aktuell gewählten Themen exakt dem Preset entsprechen (Reihenfolge egal). */
export function isThemePresetActive(preset: ThemePreset, selectedThemes: string[]): boolean {
  if (selectedThemes.length !== preset.themes.length) return false;
  const selected = new Set(selectedThemes);
  return preset.themes.every(t => selected.has(t));
}
