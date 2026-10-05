/**
 * Die zwei Klassifizierer der Partienliste (0.661.0): bei Online-Partien Seite + Modus („chess.com – Blitz"), bei
 * Ligapartien Liga + Jahrgang („Landesliga – 2026/27"). Der Server liefert je Partie den GELTENDEN Wert (gesetzt, sonst
 * bei chess.com/lichess abgeleitet); hier nur Auswahl und Filter der Liste.
 */
export interface Classified {
  classifier1?: string | null;
  classifier2?: string | null;
}

/** Filterwert für „kein Wert gesetzt". */
export const NO_CLASSIFIER = '\u0000none';

/** Die vorkommenden Werte, alphabetisch (ohne Groß-/Kleinschreibung) — Auswahl des Filters und Vorschläge im Editor. */
export function distinctClassifiers(games: readonly Classified[], which: 1 | 2): string[] {
  const seen = new Set<string>();
  for (const g of games) {
    const v = (which === 1 ? g.classifier1 : g.classifier2)?.trim();
    if (v) seen.add(v);
  }
  return [...seen].sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base', numeric: true }));
}

/** Ob es Partien ohne den Klassifizierer gibt — dann bietet der Filter „ohne Angabe" an. */
export function hasUnclassified(games: readonly Classified[], which: 1 | 2): boolean {
  return games.some(g => !(which === 1 ? g.classifier1 : g.classifier2)?.trim());
}

/** Filter: leer = alle, [NO_CLASSIFIER] = ohne Angabe, sonst genau dieser Wert. Der zweite Filter wirkt zusätzlich zum ersten. */
export function filterByClassifiers<T extends Classified>(games: readonly T[], first: string, second: string): T[] {
  const matches = (value: string | null | undefined, wanted: string): boolean => {
    if (!wanted) return true;
    const v = value?.trim() ?? '';
    return wanted === NO_CLASSIFIER ? v === '' : v === wanted;
  };
  return games.filter(g => matches(g.classifier1, first) && matches(g.classifier2, second));
}

/**
 * Vorschlag für den Jahrgang einer Ligapartie aus dem Spieldatum (`yyyy-MM-dd`): die Saison läuft ab Herbst —
 * ab August „2026/27", davor „2025/26". Kein Datum → `null`.
 */
export function seasonOf(isoDate: string | null | undefined): string | null {
  const m = (isoDate ?? '').match(/^(\d{4})-(\d{2})/);
  if (!m) return null;
  const year = Number(m[1]);
  const start = Number(m[2]) >= 8 ? year : year - 1;
  return `${start}/${String((start + 1) % 100).padStart(2, '0')}`;
}
