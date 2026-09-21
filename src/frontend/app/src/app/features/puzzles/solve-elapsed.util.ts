import { BoundedMapStore } from '../../core/local-json-store';

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

/** Verdrängt wird nach SCHREIBZEITPUNKT (`at`) — die Schlüssel sind hier beliebige Zeichenketten
 *  (`course:<id>`) und sagen über das Alter nichts aus. */
const store = new BoundedMapStore<ElapsedEntry>(
  SOLVE_ELAPSED_KEY, MAX_ENTRIES,
  entries => Object.keys(entries).sort((a, b) => (entries[a]?.at || 0) - (entries[b]?.at || 0)));

/** Bisher verbrachte Sekunden am Puzzle des Schlüssels (0 = nichts gemerkt). */
export function loadSolveElapsed(key: string): number {
  if (!key) return 0;
  const v = Math.floor(Number(store.get(key)?.s));
  return Number.isFinite(v) && v > 0 ? v : 0;
}

/** Zwischenstand fortschreiben (überschreibt; die ältesten Einträge werden weggeräumt).
 *  Quota/Privatmodus → Zwischenstand eben nicht gemerkt. */
export function saveSolveElapsed(key: string, seconds: number): void {
  if (!key || !(seconds > 0)) return;
  store.set(key, { s: Math.floor(seconds), at: Date.now() });
}

/** Eintrag löschen — sobald der Versuch erfasst ist, wird nicht mehr kumuliert. */
export function clearSolveElapsed(key: string): void {
  if (!key) return;
  store.remove(key);
}
