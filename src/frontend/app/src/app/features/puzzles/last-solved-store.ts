/**
 * Persistiert das zuletzt gelöste Puzzle je Modus (standard / book / endless) in `sessionStorage`,
 * damit der „Letztes analysieren" / „♥ Letztes Puzzle" / „Letztes teilen"-Zustand ein
 * `router.navigate(['/analysis'])` samt Rückkehr überlebt (sonst wird die Solver-Komponente
 * verworfen und `lastSolvedPuzzleId` fällt auf `null`, die Knöpfe verschwinden).
 * sessionStorage = pro Tab, keine Kreuz-Tab-Leckage; Tab schließen räumt automatisch auf.
 */

export type LastSolvedScope = 'standard' | 'endless' | 'book';

export interface LastSolvedInfo {
  id: number;
  fen: string;
  moves: string;
  orientation: 'white' | 'black';
  /** Letzter Vorspiel-Halbzug (Kurs-Linien); fehlt = klassisch, `moves[0]` ist Vorspiel. Wird
   *  gebraucht, um die AUFGABEN-Stellung zurückzurechnen („letztes Puzzle aufs Aufgabenblatt"). */
  startPly?: number;
  /** Themen des Puzzles (leerzeichengetrennt) — Vorschlagsquelle für die Themen eines Aufgabenblatts. */
  themes?: string;
}

const KEY_PREFIX = 'rookhub_last_solved_';

export function saveLastSolved(scope: LastSolvedScope, info: LastSolvedInfo): void {
  try { sessionStorage.setItem(KEY_PREFIX + scope, JSON.stringify(info)); } catch { /* ignore */ }
}

export function loadLastSolved(scope: LastSolvedScope): LastSolvedInfo | null {
  try {
    const raw = sessionStorage.getItem(KEY_PREFIX + scope);
    if (!raw) return null;
    const p = JSON.parse(raw);
    if (p && typeof p.id === 'number' && typeof p.fen === 'string'
        && typeof p.moves === 'string'
        && (p.orientation === 'white' || p.orientation === 'black')) {
      const info: LastSolvedInfo = { id: p.id, fen: p.fen, moves: p.moves, orientation: p.orientation };
      if (typeof p.startPly === 'number') info.startPly = p.startPly;
      if (typeof p.themes === 'string') info.themes = p.themes;
      return info;
    }
    return null;
  } catch { return null; }
}

export function clearLastSolved(scope: LastSolvedScope): void {
  try { sessionStorage.removeItem(KEY_PREFIX + scope); } catch { /* ignore */ }
}
