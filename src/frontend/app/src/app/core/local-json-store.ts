/**
 * Der Rumpf, den JEDER geräte-lokale Speicher dieser App teilt: lesen mit `JSON.parse` in
 * `try/catch` (kaputter Inhalt = kein Inhalt), schreiben mit `try/catch` und einer EHRLICHEN
 * Antwort (`false` = nichts geschrieben, Quota/Privatmodus), löschen, und „alle Schlüssel mit
 * diesem Präfix".
 *
 * Vorher stand dieser Rumpf sechsmal ausgeschrieben da (Buch-Offline-Cache, Repertoire-Offline,
 * Kalkulations-Speicher, Lösezeit-Zwischenstände, zuletzt gelöstes Puzzle) — jede Kopie mit ihrem
 * eigenen kleinen Dreh, obwohl die Regeln dieselben sind. Hier steht die Mechanik, in den Utils
 * bleibt nur noch ihr FACHLICHES (Id-Karte, Vollständigkeits-Marker, Validierung, Verdrängung).
 *
 * Zwei Dinge, die dabei nicht kippen dürfen:
 * 1. **Ein gesperrter Speicher darf NIE werfen.** Schon der Zugriff auf `localStorage` selbst
 *    kann eine `SecurityError` auslösen (Chrome mit blockierten Cookies) — deshalb holt man ihn
 *    über {@link localStore}/{@link sessionStore} und nicht direkt.
 * 2. **Ein fehlgeschlagener Schreibversuch wird GEMELDET**, nicht geschluckt: Wer „gespeichert"
 *    anzeigt, obwohl nichts liegt, verspricht dem Nutzer eine Kopie, die nach dem Neuladen weg
 *    ist (siehe `writeCalcLocal*`, `saveBookOffline`).
 */

/** `localStorage`, oder `null` wenn der Browser den Zugriff verweigert. */
export function localStore(): Storage | null {
  try { return localStorage; } catch { return null; }
}

/** `sessionStorage` (pro Tab), oder `null` wenn der Browser den Zugriff verweigert. */
export function sessionStore(): Storage | null {
  try { return sessionStorage; } catch { return null; }
}

/** Gespeichertes JSON lesen. `null` bei fehlendem Schlüssel, kaputtem JSON oder gesperrtem Speicher —
 *  für den Aufrufer ist das derselbe Fall: es gibt nichts. */
export function readJson<T>(storage: Storage | null | undefined, key: string): T | null {
  if (!storage || !key) return null;
  try {
    const raw = storage.getItem(key);
    if (raw == null) return null;
    return (JSON.parse(raw) ?? null) as T | null;
  } catch { return null; }
}

/** Wert als JSON ablegen. `false` = nichts geschrieben (Quota, Privatmodus, gesperrt). */
export function writeJson(storage: Storage | null | undefined, key: string, value: unknown): boolean {
  if (!storage || !key) return false;
  try { storage.setItem(key, JSON.stringify(value)); return true; }
  catch { return false; }
}

/** Rohen Wert lesen (für Marker wie `'1'`, die kein JSON brauchen). */
export function readRaw(storage: Storage | null | undefined, key: string): string | null {
  if (!storage || !key) return null;
  try { return storage.getItem(key); } catch { return null; }
}

/** Rohen Wert ablegen. `false` = nichts geschrieben. */
export function writeRaw(storage: Storage | null | undefined, key: string, value: string): boolean {
  if (!storage || !key) return false;
  try { storage.setItem(key, value); return true; }
  catch { return false; }
}

/** Liegt zu dem Schlüssel überhaupt etwas? (Gesperrter Speicher ⇒ „nein".) */
export function hasKey(storage: Storage | null | undefined, key: string): boolean {
  return readRaw(storage, key) != null;
}

/** Schlüssel entfernen (idempotent; ein gesperrter Speicher ist kein Fehlerfall). */
export function removeKey(storage: Storage | null | undefined, key: string): void {
  if (!storage || !key) return;
  try { storage.removeItem(key); } catch { /* gesperrt → egal */ }
}

/** Alle Schlüssel mit diesem Präfix (leer, wenn der Speicher nicht mitspielt). */
export function keysWithPrefix(storage: Storage | null | undefined, prefix: string): string[] {
  const out: string[] = [];
  if (!storage || !prefix) return out;
  try {
    for (let i = 0; i < storage.length; i++) {
      const k = storage.key(i);
      if (k && k.startsWith(prefix)) out.push(k);
    }
  } catch { /* gesperrt → nichts gefunden */ }
  return out;
}

/**
 * Eine Karte unter EINEM Schlüssel, gedeckelt auf `max` Einträge — das „begrenzte Map, älteste
 * raus"-Muster, das der Tagespuzzle-Cache und die beiden Lösezeit-Speicher gleichermaßen brauchen.
 *
 * Welcher Eintrag der ÄLTESTE ist, weiß nur das Fach: bei Datums-Schlüsseln (`yyyyMMdd`) ist es
 * der lexikografisch kleinste, bei Lösezeiten der zuletzt beschriebene. Deshalb ist die Reihenfolge
 * ein Parameter und keine Annahme.
 */
export class BoundedMapStore<T> {
  /**
   * @param key localStorage-Schlüssel der ganzen Karte.
   * @param max Höchstzahl der Einträge; darüber fallen die ältesten heraus.
   * @param oldestFirst Liefert die Schlüssel in Verdrängungs-Reihenfolge (ältester zuerst).
   *        Vorgabe: lexikografisch — bei `yyyyMMdd`-Schlüsseln ist das chronologisch.
   */
  constructor(
    private readonly key: string,
    private readonly max: number,
    private readonly oldestFirst: (entries: Record<string, T>) => string[] =
      entries => Object.keys(entries).sort(),
  ) {}

  /** Die ganze Karte (leer bei fehlendem/kaputtem Inhalt). */
  read(): Record<string, T> {
    const map = readJson<Record<string, T>>(localStore(), this.key);
    return map && typeof map === 'object' && !Array.isArray(map) ? map : {};
  }

  /** Ein Eintrag (oder `undefined`). */
  get(mapKey: string): T | undefined {
    return this.read()[mapKey];
  }

  /** Eintrag setzen/überschreiben und danach auf `max` eindampfen. `false` = nichts geschrieben. */
  set(mapKey: string, value: T): boolean {
    const map = this.read();
    map[mapKey] = value;
    const keys = this.oldestFirst(map);
    while (keys.length > this.max) { delete map[keys.shift()!]; }
    return writeJson(localStore(), this.key, map);
  }

  /** Eintrag entfernen. Ist er gar nicht da, wird auch NICHT geschrieben (kein Grund, den
   *  Speicher anzufassen). */
  remove(mapKey: string): boolean {
    const map = this.read();
    if (!(mapKey in map)) return true;
    delete map[mapKey];
    return writeJson(localStore(), this.key, map);
  }
}
