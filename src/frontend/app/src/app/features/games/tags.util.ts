/** Eigene Tags einer Partie (0.662.0) — Auswahl und Filter der Liste, Bereinigung im Editor. Spiegel von `GameTags` im Server. */
export const MAX_TAGS = 10;
export const MAX_TAG_LENGTH = 30;

export interface Tagged {
  tags?: string[] | null;
}

/** Alle vergebenen Tags, alphabetisch (ohne Groß-/Kleinschreibung); gleiche Tags in anderer Schreibweise zählen einmal. */
export function distinctTags(games: readonly Tagged[]): string[] {
  const byKey = new Map<string, string>();
  for (const g of games) for (const t of g.tags ?? []) if (!byKey.has(t.toLowerCase())) byKey.set(t.toLowerCase(), t);
  return [...byKey.values()].sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base', numeric: true }));
}

/** Filter: leer = alle, sonst Partien mit genau diesem Tag (ohne Groß-/Kleinschreibung). */
export function filterByTag<T extends Tagged>(games: readonly T[], tag: string): T[] {
  const wanted = tag.trim().toLowerCase();
  return wanted ? games.filter(g => (g.tags ?? []).some(t => t.toLowerCase() === wanted)) : [...games];
}

/** Neuer Tag aus der Eingabe: getrimmt, Leerraum zusammengezogen, gekürzt; `null` bei leer, doppelt oder voller Liste. */
export function addTag(current: readonly string[], raw: string): string[] | null {
  let t = raw.replace(/[\r\n,]+/g, ' ').replace(/\s+/g, ' ').trim();
  if (t.length > MAX_TAG_LENGTH) t = t.slice(0, MAX_TAG_LENGTH).trimEnd();
  if (!t || current.length >= MAX_TAGS || current.some(c => c.toLowerCase() === t.toLowerCase())) return null;
  return [...current, t];
}
