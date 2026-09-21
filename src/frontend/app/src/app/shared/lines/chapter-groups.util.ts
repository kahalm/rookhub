/**
 * Eine Gruppe gleicher Kapitel-Zugehörigkeit. Der Schlüssel ist generisch, weil die beiden
 * Linien-Listen ihn verschieden ausdrücken: die Kurs-Ansicht nennt „ohne Kapitel" `null`, die
 * Repertoire-Liste die leere Zeichenkette (dort ist er zugleich der Schlüssel der
 * Einklapp-/Farb-Karten, und `null` wäre dort ein zweiter Sonderfall).
 */
export interface ChapterGroupOf<T, K> {
  key: K;
  lines: T[];
}

/**
 * Gruppiert Linien nach Kapitel — in der Reihenfolge des ERSTEN Auftretens, nicht alphabetisch:
 * die Reihenfolge im Buch ist die Lesereihenfolge, und die Listen zeigen sie so, wie der Server
 * (bzw. das PGN) sie liefert.
 *
 * Stand vorher zweimal ausgeschrieben da (`course-browse.component`, `repertoire-lines.component`)
 * — einmal über ein `Map<string|null, Group>` mit mitgeführter Ergebnisliste, einmal über
 * `[...map.entries()]`; beide Male dieselbe Regel.
 */
export function groupByChapter<T, K>(items: readonly T[], keyOf: (item: T) => K): ChapterGroupOf<T, K>[] {
  const groups: ChapterGroupOf<T, K>[] = [];
  const byKey = new Map<K, ChapterGroupOf<T, K>>();
  for (const item of items ?? []) {
    const key = keyOf(item);
    let group = byKey.get(key);
    if (!group) { group = { key, lines: [] }; byKey.set(key, group); groups.push(group); }
    group.lines.push(item);
  }
  return groups;
}
