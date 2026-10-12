/**
 * Anzeige-Hilfen der Kurs-Detailseite (UI-Sweep v1): Schlagwörter als Abzeichen. (Die Beschreibung kürzt seit der
 * Nachrunde `shared/clamp-more/clamp-more.directive.ts` gemessen auf drei Zeilen.)
 */

/** Rein technische Schlagwörter, die niemandem etwas sagen („personal" markiert eigene Kurse im Backend). */
const TECHNICAL_TAGS = new Set(['personal']);

/**
 * Der rohe Tag-String („Meisterpartien,kommentiert,gemeinfrei") als Liste einzelner Schlagwörter
 * (v1-rk-kursseite-schlagwoerter-ohne-leerzeichen) — ohne Leereinträge, Dubletten und technische Tags.
 */
export function courseTagList(tags: string | null | undefined): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const raw of (tags ?? '').split(',')) {
    const t = raw.trim();
    if (!t || TECHNICAL_TAGS.has(t.toLowerCase()) || seen.has(t.toLowerCase())) continue;
    seen.add(t.toLowerCase());
    out.push(t);
  }
  return out;
}
