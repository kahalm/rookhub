import { Chess } from 'chess.js';
import { whiteToMove } from '../../shared/chess/fen-side';
import { EbookGame } from './ebook.service';

/**
 * Reine Helfer des Bretts neben dem Buch ({@link EbookBoardComponent}) — ohne Angular, ohne Zustand
 * (herausgezogen aus der Komponente, 1.010.1).
 */

/** Partie im Modus „Partie“ (0.831.0): Kopf, Hauptlinie und die Stellung nach jedem Halbzug (Index 0 = Grundstellung). */
export interface EbookGameLine {
  game: EbookGame;
  sans: string[];
  fens: string[];
}

/** UI-Sweep 2026-11-10 (d10-rk-ebook-partiekopf-pgn-rohwerte): PGN-Ergebnis zum Anzeigen (wie game-replay), „*“ ohne Anzeige. */
export function ebookResultLabel(r: string | null | undefined): string {
  if (!r || r === '*') return '';
  return r === '1/2-1/2' ? '½–½' : r.replace('-', '–');
}

/** Hauptlinie nachspielen; ab einem unspielbaren Zug wird abgeschnitten (kaputte Quelle soll den Leser nicht sprengen). */
export function replayGame(game: EbookGame): EbookGameLine {
  const chess = new Chess();
  const sans: string[] = [];
  const fens = [chess.fen()];
  for (const san of game.moves) {
    try { sans.push(chess.move(san).san); fens.push(chess.fen()); } catch { break; }
  }
  return { game, sans, fens };
}

/** Zugnummern ab einer Stellung vor eine SAN-Folge setzen („17… Txh2+ 18. Kxh2“). */
export function numberedSans(fen: string, sans: readonly string[]): string {
  if (sans.length === 0) return '';
  const parts = fen.split(' ');
  let n = Number(parts[5]) || 1;
  let white = whiteToMove(fen);
  const out: string[] = [];
  sans.forEach((san, i) => {
    if (white) out.push(`${n}. ${san}`);
    else { out.push(i === 0 ? `${n}… ${san}` : san); n++; }
    white = !white;
  });
  return out.join(' ');
}

/** Weiß-Sicht in Centipawns; Matt in n = ±(10000 − n). Ohne Bewertung (Partieende): Matt/Patt aus der Stellung. */
export function scoreCp(score: { cp?: number; mate?: number } | undefined, fen: string): number {
  if (score?.mate != null) return score.mate > 0 ? 10000 - score.mate : -10000 - score.mate;
  if (score?.cp != null) return score.cp;
  try {
    const c = new Chess(fen);
    if (c.isCheckmate()) return c.turn() === 'w' ? -10000 : 10000;
  } catch { /* unten */ }
  return 0;   // Patt, Remis, unbekannt
}

/** Gleiches Ergebnis aus Sicht des Ziehenden (Vergleich `reference`, eigener Zug `mine`). */
export function sameResult(reference: number, mine: number): boolean {
  if (mine >= reference - 50) return true;
  if (Math.abs(reference) <= 80 && mine >= -80) return true;   // Remis bleibt Remis
  if (reference >= 300 && mine >= 300) return true;            // Gewinn bleibt Gewinn
  return false;
}
