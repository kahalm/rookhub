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

/** UI-Größe je Gerät (0.800.x, `rookhub_ui_scale`): nur diese Stufen gibt es in der App. */
export const UI_SCALES = [100, 125, 150, 175, 200];

/** Speicher-Vorbelegung eines Laufs: Design-Schlüssel + UI-Größe (100 = Schlüssel leer → App bleibt bei 100 %). */
export function storageFor(theme, uiScale = 100) {
  if (!UI_SCALES.includes(uiScale)) throw new Error(`Unbekannte UI-Größe ${uiScale} (${UI_SCALES.join(', ')})`);
  return { ...themeSetup(theme).storage, ...(uiScale !== 100 ? { rookhub_ui_scale: String(uiScale) } : {}) };
}
