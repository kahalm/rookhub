/**
 * Merkt die bereits verbrachte (aktive) Lösezeit eines Puzzles je Schlüssel im localStorage,
 * damit ein Refresh/Wiederbesuch mitten im Lösen NICHT wieder bei 0 zählt, sondern kumuliert
 * weiterläuft — Pendant zu daily-elapsed.util (Tagespuzzle, je UTC-Datum) für generische
 * Schlüssel, z. B. `course:<bookPuzzleId>` im Kursmodus. Der Solver persistiert den
 * Zwischenstand im Sekunden-Tick und löscht den Eintrag, sobald der Versuch erfasst ist.
 */
const SOLVE_ELAPSED_KEY = 'rookhub_solve_elapsed';

/** Wie viele Einträge vorgehalten werden (die zuletzt beschriebenen gewinnen). Verwaiste
 *  Einträge — z. B. liefert der Kurs-Random-Modus nach einem Refresh ein ANDERES Puzzle,
 *  der gemerkte Stand des alten bleibt liegen — altern so von selbst weg. */
const MAX_ENTRIES = 30;

interface ElapsedEntry { s: number; at: number; }

function loadMap(): Record<string, ElapsedEntry> {
  try { return JSON.parse(localStorage.getItem(SOLVE_ELAPSED_KEY) || '{}') || {}; } catch { return {}; }
}

/** Bisher verbrachte Sekunden am Puzzle des Schlüssels (0 = nichts gemerkt). */
export function loadSolveElapsed(key: string): number {
  if (!key) return 0;
  const v = Math.floor(Number(loadMap()[key]?.s));
  return Number.isFinite(v) && v > 0 ? v : 0;
}

/** Zwischenstand fortschreiben (überschreibt; die ältesten Einträge werden weggeräumt). */
export function saveSolveElapsed(key: string, seconds: number): void {
  if (!key || !(seconds > 0)) return;
  try {
    const map = loadMap();
    map[key] = { s: Math.floor(seconds), at: Date.now() };
    // Auf die jüngsten MAX_ENTRIES begrenzen (nach Schreibzeitpunkt; älteste zuerst raus).
    const keys = Object.keys(map).sort((a, b) => (map[a].at || 0) - (map[b].at || 0));
    while (keys.length > MAX_ENTRIES) { delete map[keys.shift()!]; }
    localStorage.setItem(SOLVE_ELAPSED_KEY, JSON.stringify(map));
  } catch { /* Quota/Privatmodus → Zwischenstand eben nicht gemerkt */ }
}

/** Eintrag löschen — sobald der Versuch erfasst ist, wird nicht mehr kumuliert. */
export function clearSolveElapsed(key: string): void {
  if (!key) return;
  try {
    const map = loadMap();
    if (!(key in map)) return;
    delete map[key];
    localStorage.setItem(SOLVE_ELAPSED_KEY, JSON.stringify(map));
  } catch { /* ignore */ }
}
