import { BoardArrow } from '../../shared/pgn-viewer/chess-board.component';
import { ExplorerPosition } from './repertoire-explorer.service';

/**
 * Baum-Modus: alle Fortsetzungen des Repertoires in der aktuellen Stellung als Pfeile (Wunsch 2026-10-25: „alle lines die
 * zur auswahl stehen als pfeile auf dem brett … größe des pfeils sollte häufigkeit auf lichess wiederspiegeln").
 * Breite nach dem Anteil der Partien am häufigsten Zug der Auswahl; ohne Explorer-Daten alle gleich breit.
 */

/** SAN ohne Schach-/Matt-/Bewertungszeichen, Rochade mit „O" — Explorer und Repertoire schreiben das verschieden. */
export function canonSan(san: string): string {
  return san.trim().replace(/[+#!?]+$/, '').replace(/0-0-0/, 'O-O-O').replace(/0-0/, 'O-O');
}

export interface TreeArrowMove { san: string; from: string; to: string; }

/** Breiten in chessground-Einheiten (Vorgabe-Pfeil 10). */
export const ARROW_MIN = 3;
export const ARROW_MAX = 18;
export const ARROW_UNIFORM = 9;

export function treeArrows(moves: readonly TreeArrowMove[], popularity: ExplorerPosition | null): BoardArrow[] {
  const games = new Map<string, number>();
  if (popularity?.status === 'ok') for (const m of popularity.moves) games.set(canonSan(m.san), m.games);
  const counts = moves.map(m => games.get(canonSan(m.san)) ?? 0);
  const max = Math.max(0, ...counts);
  const out = moves.map((m, i) => ({
    from: m.from, to: m.to, brush: 'green',
    lineWidth: max > 0 ? Math.round(ARROW_MIN + (ARROW_MAX - ARROW_MIN) * Math.sqrt(counts[i] / max)) : ARROW_UNIFORM,
  }));
  // Dünne zuerst: chessground zeichnet in Reihenfolge — der dickste Pfeil liegt oben und bleibt sichtbar.
  return out.sort((a, b) => a.lineWidth - b.lineWidth);
}

/** Gleicher Pfeilsatz? (Das Brett zeichnet nur bei echter Änderung neu.) */
export function sameArrows(a: readonly BoardArrow[], b: readonly BoardArrow[]): boolean {
  return a.length === b.length && a.every((x, i) => x.from === b[i].from && x.to === b[i].to
    && x.brush === b[i].brush && x.lineWidth === b[i].lineWidth);
}
