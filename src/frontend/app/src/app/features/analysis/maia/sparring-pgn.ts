import { Chess } from 'chess.js';

/** Eine Sparring-Partie gegen Maia: Ausgangsstellung, gespielte Züge (SAN), wer welche Seite hatte. */
export interface SparringGame {
  startFen: string;
  sans: string[];
  userColor: 'white' | 'black';
  /** Benutzername; leer → „Player". */
  userName: string;
  elo: number;
  date: Date;
}

/**
 * PGN einer Sparring-Partie für „Partie analysieren" (`POST /api/games/import`). Reine Funktion, ohne Angular.
 *
 * Die Züge spielt chess.js nach — es schreibt bei einer anderen Ausgangsstellung selbst `[SetUp "1"]` + `[FEN …]`
 * und nummeriert richtig. Kopfdaten: Event/Site/Date/Round, Namen nach Seite („Maia 1600" für Maia — die Stärke ist
 * keine echte Wertung, deshalb keine Elo-Kopfzeilen), Ergebnis aus der ENDstellung (Matt → die Seite am Zug hat
 * verloren, `isDraw()` → Remis, sonst `*`).
 *
 * **Schwarz am Zug**: chess.js schreibt den ersten Zug als „4. ... Bc5". Der PGN-Import des Servers las die drei
 * Punkte bis 2026-10-02 als eigenen Zug und lehnte die Partie als illegal ab. Im ZUGTEXT wird daraus deshalb die
 * kompakte Form „4... Bc5" — dann geht der Knopf auch gegen eine API ohne die Parser-Reparatur (Frontend und API
 * werden getrennt ausgerollt).
 *
 * @returns `null`, wenn es keine Züge gibt, die Stellung nicht lesbar ist oder ein Zug nicht geht.
 */
export function buildSparringPgn(g: SparringGame): string | null {
  if (g.sans.length === 0) return null;
  let chess: Chess;
  try {
    chess = new Chess(g.startFen);
    for (const san of g.sans) chess.move(san);
  } catch {
    return null;
  }

  const user = headerValue(g.userName) || 'Player';
  const maia = `Maia ${g.elo}`;
  chess.setHeader('Event', 'Sparring vs Maia');
  chess.setHeader('Site', 'RookHub');
  chess.setHeader('Date', pgnDate(g.date));
  chess.setHeader('Round', '-');
  chess.setHeader('White', g.userColor === 'white' ? user : maia);
  chess.setHeader('Black', g.userColor === 'white' ? maia : user);
  chess.setHeader('Result', resultOf(chess));

  const pgn = chess.pgn();
  const split = pgn.indexOf('\n\n');
  if (split < 0) return pgn;
  const head = pgn.slice(0, split + 2);
  const moves = pgn.slice(split + 2).replace(/(\d+)\. \.\.\. /g, '$1... ');
  return head + moves;
}

/** Ergebnis aus der Endstellung. */
function resultOf(chess: Chess): string {
  if (chess.isCheckmate()) return chess.turn() === 'w' ? '0-1' : '1-0';
  if (chess.isDraw()) return '1/2-1/2';
  return '*';
}

/** `YYYY.MM.DD` aus dem LOKALEN Datum. */
function pgnDate(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}.${pad(d.getMonth() + 1)}.${pad(d.getDate())}`;
}

/** chess.js maskiert Kopfzeilen nicht — Anführungszeichen, Backslash, Klammern und Zeilenumbrüche fallen weg. */
function headerValue(s: string | null | undefined): string {
  return (s ?? '').replace(/["\\[\]\r\n\t]/g, '').trim();
}
