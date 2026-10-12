/**
 * Anzeige-Hilfen der Kurs-Detailseite (UI-Sweep v1): gekürzte Beschreibung mit „Mehr" und Schlagwörter als Abzeichen.
 */

/** Ab so vielen Zeichen bzw. Zeilen wird die Beschreibung zugeklappt (DESIGN 4.6 Nr. 3: Erklärtext ≤ 3 Zeilen). */
export const DESCRIPTION_MAX_CHARS = 180;
export const DESCRIPTION_MAX_LINES = 3;

/**
 * Kurzfassung einer langen Kursbeschreibung — am Wortende abgeschnitten, mit „…" — oder `null`, wenn der Text kurz
 * genug ist und ganz stehen bleibt (v1-rk-kursseite-beschreibung-vorlauf-handy). Am Handy schob eine
 * siebenzeilige Beschreibung „Sequenziell" auf 55 % der Höhe.
 */
export function shortDescription(text: string | null | undefined): string | null {
  const t = (text ?? '').trim();
  const lines = t.split('\n');
  if (t.length <= DESCRIPTION_MAX_CHARS && lines.length <= DESCRIPTION_MAX_LINES) return null;
  let cut = lines.slice(0, DESCRIPTION_MAX_LINES).join('\n');
  if (cut.length > DESCRIPTION_MAX_CHARS) {
    cut = cut.slice(0, DESCRIPTION_MAX_CHARS);
    const space = cut.search(/\s\S*$/);
    if (space > DESCRIPTION_MAX_CHARS / 2) cut = cut.slice(0, space);
  }
  return cut.replace(/[\s,;:.–-]+$/, '') + ' …';
}

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
