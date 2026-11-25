/**
 * Gemerkte Wahlen mit Zeitstempel, die am Gerät UND im Profil liegen (Spielweise je Puzzle-Bereich, Sprache je Kurs).
 * Der Server führt einen PUT je Schlüssel zusammen (der jüngere gewinnt, `ProfileChoiceMaps`); hier dasselbe in die
 * Gegenrichtung, wenn das Profil geladen ist. Reine Daten, ohne Angular.
 */
export interface Stamped { at: number; }

export interface StampedMerge<T extends Stamped> {
  /** Neuer Stand am Gerät. */
  merged: Record<string, T>;
  /** Was das Profil noch nicht (oder nur älter) hat — hochladen. Beim ersten Laden eines leeren Profils: alles. */
  upload: Record<string, T>;
  /** Hat sich am Gerät etwas geändert? */
  changed: boolean;
}

/**
 * Gerät ⇄ Profil zusammenführen. Das Profil gewinnt, bei einem Konflikt je Schlüssel der jüngere Eintrag (`at`);
 * bei gleichem Zeitstempel das Profil. Ungültige Einträge (auf beiden Seiten) fallen weg.
 */
export function mergeStamped<T extends Stamped>(
  local: Record<string, T>,
  server: Record<string, T> | null | undefined,
  valid: (key: string, e: T) => boolean,
): StampedMerge<T> {
  const merged: Record<string, T> = {};
  const upload: Record<string, T> = {};
  let changed = false;
  const srv = server && typeof server === 'object' ? server : {};
  for (const k of new Set([...Object.keys(local), ...Object.keys(srv)])) {
    const l = ok(local[k], k, valid) ? local[k] : null;
    const s = ok(srv[k], k, valid) ? srv[k] : null;
    if (s && (!l || s.at >= l.at)) {
      merged[k] = { ...s };
      if (!l || l.at !== s.at || JSON.stringify(l) !== JSON.stringify(s)) changed = true;
    } else if (l) {
      merged[k] = l;
      upload[k] = l;
    } else if (k in local) {
      changed = true;   // kaputter lokaler Eintrag fliegt
    }
  }
  return { merged, upload, changed };
}

function ok<T extends Stamped>(e: T | undefined, key: string, valid: (key: string, e: T) => boolean): boolean {
  return !!e && typeof e === 'object' && typeof e.at === 'number' && e.at > 0 && valid(key, e);
}
