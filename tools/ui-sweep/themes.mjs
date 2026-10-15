// Design-Achse des Sweeps (--theme). Ein Design bestimmt das Farbschema des Browsers, das App-Design
// (rookhub_app_theme + Cookie rookhub_theme) und Zusatzschlüssel im Speicher.
//   eink — E-Ink-Modus (0.799.x): helles Design + `rookhub_eink` = '1' (je Gerät, Profil → Darstellung).
//          Gedacht zusammen mit --viewport boox (Boox Tab X C des Betreibers).
export const THEMES = {
  dark: { colorScheme: 'dark', appTheme: 'dark', storage: { rookhub_eink: '0' } },
  light: { colorScheme: 'light', appTheme: 'light', storage: { rookhub_eink: '0' } },
  eink: { colorScheme: 'light', appTheme: 'light', storage: { rookhub_eink: '1' } },
};

/** Einstellungen eines Designs; wirft bei unbekanntem Namen. */
export function themeSetup(name) {
  const t = THEMES[name];
  if (!t) throw new Error(`Unbekanntes Design ${name} (${Object.keys(THEMES).join(', ')})`);
  return t;
}
