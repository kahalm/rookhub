/**
 * Merkt die bereits verbrachte (aktive) Lösezeit des Tagespuzzles je UTC-Datum im localStorage,
 * damit ein Wiederbesuch des Links NICHT wieder bei 0 zählt, sondern kumuliert weiterläuft.
 * Der Solver persistiert den Zwischenstand im Sekunden-Tick und löscht den Eintrag, sobald ein
 * Versuch serverseitig erfasst wurde (ab da zählt für die Bestenliste ohnehin der Erstversuch).
 */
const DAILY_ELAPSED_KEY = 'rookhub_daily_elapsed';

/** Wie viele Datums-Einträge vorgehalten werden (jüngste gewinnen — wie der Daily-Offline-Cache). */
const MAX_ENTRIES = 14;

function loadMap(): Record<string, number> {
  try { return JSON.parse(localStorage.getItem(DAILY_ELAPSED_KEY) || '{}') || {}; } catch { return {}; }
}

/** Bisher verbrachte Sekunden am Tagespuzzle des Datums (0 = nichts gemerkt). */
export function loadDailyElapsed(date: string): number {
  if (!date) return 0;
  const v = Math.floor(Number(loadMap()[date]));
  return Number.isFinite(v) && v > 0 ? v : 0;
}

/** Zwischenstand fortschreiben (überschreibt; ältere Datums-Einträge werden weggeräumt). */
export function saveDailyElapsed(date: string, seconds: number): void {
  if (!date || !(seconds > 0)) return;
  try {
    const map = loadMap();
    map[date] = Math.floor(seconds);
    // Auf die jüngsten MAX_ENTRIES Datumsschlüssel begrenzen (lexikografisch = chronologisch bei yyyyMMdd).
    const keys = Object.keys(map).sort();
    while (keys.length > MAX_ENTRIES) { delete map[keys.shift()!]; }
    localStorage.setItem(DAILY_ELAPSED_KEY, JSON.stringify(map));
  } catch { /* Quota/Privatmodus → Zwischenstand eben nicht gemerkt */ }
}

/** Eintrag löschen — sobald ein Versuch erfasst wurde, wird nicht mehr kumuliert. */
export function clearDailyElapsed(date: string): void {
  if (!date) return;
  try {
    const map = loadMap();
    if (!(date in map)) return;
    delete map[date];
    localStorage.setItem(DAILY_ELAPSED_KEY, JSON.stringify(map));
  } catch { /* ignore */ }
}
