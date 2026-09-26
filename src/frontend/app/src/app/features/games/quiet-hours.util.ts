/**
 * Sperrzeiten der Spark (0.546.0): bis wann das Sprachmodell auf eigener Hardware anderen gehört — als „Fr., 14:00"
 * in der Sprache der Oberfläche. Der Server nennt den Zeitpunkt (`quietUntil`), die Seite schreibt ihn nur hin; leer bei
 * fehlender oder unlesbarer Angabe.
 */
export function formatQuietUntil(iso: string | null | undefined, lang?: string | null): string {
  if (!iso) return '';
  const at = new Date(iso);
  if (isNaN(at.getTime())) return '';
  try {
    return at.toLocaleString(lang || undefined, { weekday: 'short', hour: '2-digit', minute: '2-digit' });
  } catch {
    return at.toLocaleString(undefined, { weekday: 'short', hour: '2-digit', minute: '2-digit' });
  }
}
