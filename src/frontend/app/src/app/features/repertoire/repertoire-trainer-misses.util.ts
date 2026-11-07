import type { ParsedGame } from '../../shared/pgn-viewer/pgn-parser';
import { whiteToMove } from '../../shared/chess/fen-side';
import type { TrainColor } from '../../shared/repertoire/repertoire-color.util';

/** Chessable-Fehler einer Zeile (`mid`) in einer Farbe, über alle Durchläufe summiert — Antwort von
 *  `GET /api/repertoires/{id}/training/chessable-misses` je oid. */
export interface ChessableRawMiss { mid: number; mode: string | null; count: number; last: string | null }
export type ChessableMisses = Record<string, ChessableRawMiss[]>;

/** Was der Trainer nach einem Fehlzug zusätzlich sagt: Chessable-Fehler an genau diesem Zug, RookHub-Fehler der Linie. */
export interface MissInfo { chessable: number; last: string | null; rookhub: number }

/** Numerische oid einer Linie (`[ChessableOid "…"]`), sonst `null`. */
export function chessableOid(line: ParsedGame | undefined): string | null {
  const oid = (line?.headers['ChessableOid'] ?? '').trim();
  return /^\d{1,32}$/.test(oid) ? oid : null;
}

/** Link zur Linie auf Chessable (wie Server `ChessableLineHistory.VariationUrl`). */
export function chessableUrl(oid: string): string {
  return `https://www.chessable.com/variation/${oid}/`;
}

/** Halbzug ab Linienbeginn zu Zeile `mid` (je Zeile ein Weiß- und ein Schwarzzug; −1, wenn die Linie mit Schwarz beginnt). */
export function plyOfMid(mid: number, color: TrainColor, startsWithBlack: boolean): number {
  return 2 * mid + (color === 'b' ? 1 : 0) - (startsWithBlack ? 1 : 0);
}

/** „7…Sc6": Zugnummer des Halbzugs + Zug (wie mitgeschnitten). */
export function moveLabelAt(line: ParsedGame, ply: number, move: string): string {
  const parts = (line.fens[0] ?? '').split(' ');
  const startNo = Number(parts[5]) > 0 ? Number(parts[5]) : 1;
  const abs = ply + (whiteToMove(line.fens[0] ?? '') ? 0 : 1);
  const no = startNo + Math.floor(abs / 2);
  return abs % 2 === 0 ? `${no}.${move}` : `${no}…${move}`;
}

/**
 * Fehler am Halbzug `ply` der Linie: Chessable (nur Einträge der eigenen Farbe bzw. ohne Farbe, `mid` → Halbzug) und die
 * RookHub-Fehlversuche der Linie (`lapses` — RookHub merkt sich Fehler je LINIE, nicht je Zug). `null`, wenn es nichts zu sagen gibt.
 */
export function missAt(line: ParsedGame | undefined, ply: number, color: TrainColor, misses: ChessableMisses | null,
                       rookhubLapses: number): MissInfo | null {
  if (!line) return null;
  const oid = chessableOid(line);
  const startsWithBlack = !whiteToMove(line.fens[0] ?? '');
  let count = 0;
  let last: string | null = null;
  for (const m of (oid && misses?.[oid]) || []) {
    if (m.mode && m.mode !== color) continue;
    if (plyOfMid(m.mid, color, startsWithBlack) !== ply) continue;
    count += m.count;
    if (m.last) last = m.last;
  }
  const rookhub = Math.max(0, rookhubLapses || 0);
  if (count === 0 && rookhub === 0) return null;
  return { chessable: count, last: last ? moveLabelAt(line, ply, last) : null, rookhub };
}
