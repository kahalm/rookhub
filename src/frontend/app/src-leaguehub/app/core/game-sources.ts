import { GameSourceRow, GameSources } from './league.models';

/** „1.100" — fest mit Punkt (wie `chessbase-upload.ts`): `toLocaleString('de-AT')` setzt je nach Umgebung ein schmales Leerzeichen. */
export const thousands = (n: number): string => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, '.');

/** Wie eine Quelle im Satz heißt — Schlüssel wie `LeagueGameSources` auf dem Server; unbekannte „aus <Name>". */
const PHRASE: Record<string, string> = {
  'Lumbra': 'aus Lumbra',
  'Mega': 'aus der ChessBase-Megabase',
  'chess-results': 'von chess-results',
  'Lichess-Übertragung': 'aus Lichess-Übertragungen',
  'Verein': 'aus der Vereins-Datenbank',
  'lichess': 'von Lichess',
  'chess.com': 'von chess.com',
};

const part = (r: GameSourceRow): string => `${thousands(r.games)} ${PHRASE[r.key] ?? `aus ${r.label}`}`;

/** „58.492 Brettpartien: 34.838 aus Lumbra, 18.839 aus der ChessBase-Megabase, …" — leer, wenn es keine gibt. */
export function boardSourcesText(s: GameSources): string {
  return s.boardTotal ? `${thousands(s.boardTotal)} Brettpartien: ${s.board.map(part).join(', ')}` : '';
}

/** „697.409 Online-Partien: 667.881 von Lichess, 29.528 von chess.com" — leer, wenn es keine gibt. */
export function onlineSourcesText(s: GameSources): string {
  return s.onlineTotal ? `${thousands(s.onlineTotal)} Online-Partien: ${s.online.map(part).join(', ')}` : '';
}
