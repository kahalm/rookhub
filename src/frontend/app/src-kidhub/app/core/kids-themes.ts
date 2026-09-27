import { KidsTheme } from './kids-api.service';

/** Bild je Thema — gross auf der Stufenkarte, klein neben der Aufgabe. */
export const THEME_ICONS: Record<KidsTheme, string> = {
  mate1: '🏁',
  promote: '👑',
  capture: '🎁',
  fork: '🍴',
  skewer: '🍢',
  mate2: '🏆',
  pin: '📌',
  discovered: '💥',
};

/** Unbekanntes Thema (neuer Server, alte Seite) faellt auf ein neutrales Bild zurueck. */
export function themeIcon(theme: string): string {
  return THEME_ICONS[theme as KidsTheme] ?? '⭐';
}

/** i18n-Schluessel: Name des Themas und die Aufgabe, die das Kind liest. */
export function themeNameKey(theme: string): string {
  return theme in THEME_ICONS ? `kids.themes.${theme}.name` : 'kids.themes.other.name';
}

export function themeTaskKey(theme: string): string {
  return theme in THEME_ICONS ? `kids.themes.${theme}.task` : 'kids.themes.other.task';
}
