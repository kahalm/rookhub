// Gestufte Dauer-Formatierung für die Trainingsziele-Seite. In eine eigene Util gezogen, damit
// sowohl TrainingGoalsComponent als auch die ausgegliederten Karten (z. B. ChessableThemesCardComponent)
// sie importieren können, OHNE einen Modul-Zyklus Parent ↔ Kind zu erzeugen.

/** Schwellen für die gestufte Dauer-Anzeige. */
const DURATION_HOURS_FROM_SECONDS = 120 * 60;   // bis 120 min → Minuten, danach Stunden
const DURATION_DAYS_FROM_SECONDS = 48 * 3600;   // ab 48 h → Tage

/**
 * Formatiert eine Dauer (Sekunden) gestuft als Zahl + i18n-Einheitenschlüssel:
 * < 120 min → Minuten (ganzzahlig), < 48 h → Stunden, sonst Tage (je 1 Nachkommastelle).
 * `lang` steuert nur das Dezimaltrennzeichen.
 */
export function formatDuration(seconds: number, lang: string | null | undefined = 'en'): { value: string; unitKey: string } {
  const s = Math.max(0, seconds);
  if (s < DURATION_HOURS_FROM_SECONDS) {
    return { value: String(Math.round(s / 60)), unitKey: 'trainingGoals.min' };
  }
  const isHours = s < DURATION_DAYS_FROM_SECONDS;
  const amount = isHours ? s / 3600 : s / 86400;
  let value: string;
  try {
    // ngx-translate 18: currentLang() ist Signal<string|null> → null/leer auf 'en' fallen lassen.
    value = new Intl.NumberFormat(lang || 'en', { maximumFractionDigits: 1 }).format(amount);
  } catch {
    value = amount.toFixed(1);
  }
  return { value, unitKey: isHours ? 'trainingGoals.hours' : 'trainingGoals.days' };
}
