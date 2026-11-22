import { CrawlerPairing, TeamPairingResponse, TournamentPairing } from '@rh/core/models';

/**
 * Begegnungen EINES Spielers in einem Turnier (Wunsch 2026-11-22: „wenn ich einen Spieler anklicke, seine Begegnungen
 * sehen — und ggf. die Auslosung der naechsten Runde"). Reine Funktionen, damit die Zuordnung ohne Komponente testbar ist.
 *
 * Zwei Quellen, je nach Turniertyp:
 * - **Einzelturnier**: die Paarungen ALLER Runden (`GET /api/tournaments/{id}/pairings` ohne `round`) — daraus Farbe,
 *   Gegner und Ergebnis. Eine ausgeloste, noch nicht gespielte Runde hat kein Ergebnis: das ist die „naechste Runde".
 * - **Mannschaftsturnier**: der Crawler kennt nur die Mannschaftskaempfe (Heim/Gast, Mannschaftsergebnis), keine
 *   Brettpaarungen. Die Einzelergebnisse stehen nur, wenn die Spielerseite (chess-results art=9) geholt wurde
 *   (`GET /api/tournaments/{id}/players/{snr}/results`). Ligen tragen den Spielplan oft schon fuer alle Runden — die
 *   erste Runde ohne Mannschaftsergebnis ist dann die naechste Begegnung der Mannschaft.
 */

/** Eine Zeile der Spielerseite (Crawler `PlayerResultResponse`). */
export interface PlayerRoundResult {
  roundNumber: number;
  boardNumber: number;
  opponentSnr: number | null;
  opponentName: string | null;
  opponentElo: number | null;
  points: string | null;
  result: string | null;
}

export type PieceColour = 'white' | 'black';

export interface TeamMatch {
  opponentTeam: string;
  home: boolean;
  /** Mannschaftsergebnis aus Sicht der EIGENEN Mannschaft („3.5 : 2.5"), `null` = noch nicht gespielt. */
  score: string | null;
}

export interface EncounterRow {
  round: number;
  /** Brett (Einzelturnier: Brett der Paarung; Mannschaftsturnier: Brett aus der Spielerseite). */
  board: number | null;
  colour: PieceColour | null;
  /** Gegner; `null` ohne Einzelpaarung (Mannschaftsturnier ohne Spielerseite, spielfrei). */
  opponent: string | null;
  opponentSnr: number | null;
  opponentElo: number | null;
  /** Ergebnis aus Sicht des Spielers: „1", „½", „0", „+", „−" (kampflos) oder der Rohtext; `null` = noch offen. */
  result: string | null;
  /** Spielfrei / Freilos (Einzelturnier: Paarung ohne Gegner). */
  bye: boolean;
  /** Mannschaftsturnier mit Spielerseite: die Mannschaft spielte, der Spieler war nicht aufgestellt. */
  notPlayed: boolean;
  /** Nur Mannschaftsturniere. */
  match: TeamMatch | null;
  /** Ausgelost bzw. angesetzt, aber noch ohne Ergebnis. */
  pending: boolean;
  /** Die erste offene Runde — „Naechste Runde". */
  next: boolean;
}

export interface PlayerEncounters {
  teamTournament: boolean;
  rows: EncounterRow[];
  /** Mannschaftsturnier: liegt die Spielerseite (Einzelergebnisse) vor? Im Einzelturnier immer `true`. */
  hasIndividual: boolean;
  /** Punkte aus den gewerteten Partien, `null` ohne Einzelergebnisse. */
  points: number | null;
  /** Zahl der gewerteten Partien (inkl. kampflos und spielfrei). */
  played: number;
}

/** Rohergebnis eines Einzelteils („1", „0", „½", „1/2", „0.5") auf „1" / „½" / „0". */
function normalizePart(part: string): string | null {
  switch (part) {
    case '1': return '1';
    case '0': return '0';
    case '½': case '1/2': case '0.5': case '0,5': return '½';
    default: return null;
  }
}

/**
 * Ergebnis einer Einzelpaarung („1-0", „½-½", „+--", „--+", „0-1F") aus Sicht der gegebenen Farbe. `null`, wenn die
 * Partie noch nicht gespielt ist. Unbekannte Schreibweisen kommen unveraendert zurueck — lieber Rohtext als falsch.
 */
export function resultForColour(raw: string | null | undefined, colour: PieceColour): string | null {
  const r = (raw ?? '').replace(/\s+/g, '');
  if (!r || r === '-' || r === '-:-' || r === '–') return null;
  if (r === '+--' || r === '+:-' || r === '+/-') return colour === 'white' ? '+' : '−';
  if (r === '--+' || r === '-:+' || r === '-/+') return colour === 'white' ? '−' : '+';
  if (r === '---' || r === '--') return '−';
  const m = /^(1\/2|½|0[.,]5|1|0)[-:](1\/2|½|0[.,]5|1|0)([FfKk])?$/.exec(r);
  if (m) {
    const own = normalizePart(colour === 'white' ? m[1] : m[2]);
    if (own === null) return raw!.trim();
    // „1-0F" / „0-1K" = kampflos: wie „+--" anzeigen.
    if (m[3]) return own === '1' ? '+' : own === '0' ? '−' : own;
    return own;
  }
  return raw!.trim();
}

/** Punktwert eines Ergebnisses aus Spielersicht; `null` = nicht wertbar (offen, unbekannt). */
export function resultPoints(result: string | null): number | null {
  switch ((result ?? '').trim()) {
    case '1': case '+': return 1;
    case '½': case '1/2': case '0.5': case '0,5': return 0.5;
    case '0': case '−': case '-': return 0;
    default: return null;
  }
}

function isTeamPairing(p: CrawlerPairing): p is TeamPairingResponse {
  return 'homeTeam' in p && p.homeTeam !== undefined;
}

function sameName(a: string | null | undefined, b: string | null | undefined): boolean {
  return !!a && !!b && a.trim().toLowerCase() === b.trim().toLowerCase();
}

/**
 * Die Begegnungen eines Spielers. `pairings` = alle Runden; `results` = Spielerseite (darf leer sein, wird nur im
 * Mannschaftsturnier gelesen); `teamName` = Mannschaft des Spielers (nur Mannschaftsturnier).
 */
export function buildEncounters(
  playerName: string,
  teamName: string | null | undefined,
  pairings: CrawlerPairing[],
  results: PlayerRoundResult[] = [],
): PlayerEncounters {
  const teamTournament = pairings.length > 0 && isTeamPairing(pairings[0]);
  const rows = teamTournament
    ? teamRows(teamName, pairings.filter(isTeamPairing), results)
    : individualRows(playerName, pairings.filter((p): p is TournamentPairing => !isTeamPairing(p)));
  const hasIndividual = !teamTournament || results.length > 0;

  const first = rows.find(r => r.pending);
  if (first) first.next = true;

  let points = 0;
  let played = 0;
  for (const row of rows) {
    const value = resultPoints(row.result);
    if (value === null) continue;
    points += value;
    played++;
  }
  return { teamTournament, rows, hasIndividual, points: hasIndividual ? points : null, played };
}

function individualRows(playerName: string, pairings: TournamentPairing[]): EncounterRow[] {
  const rows: EncounterRow[] = [];
  for (const p of pairings) {
    const colour: PieceColour | null = sameName(p.white, playerName) ? 'white' : sameName(p.black, playerName) ? 'black' : null;
    if (!colour) continue;
    const opponent = (colour === 'white' ? p.black : p.white)?.trim() || null;
    // Freilos: eine Paarung ohne Gegner, das Ergebnis ist schon der Punktwert („1", „½", „0").
    const bye = opponent === null;
    const raw = (p.result ?? '').trim();
    const result = bye ? (raw ? (normalizePart(raw) ?? raw) : null) : resultForColour(p.result, colour);
    rows.push({
      round: p.roundNumber, board: p.boardNumber || null, colour: bye ? null : colour, opponent,
      opponentSnr: null, opponentElo: null, result, bye, notPlayed: false, match: null,
      pending: !bye && result === null, next: false,
    });
  }
  return rows.sort((a, b) => a.round - b.round);
}

function teamRows(teamName: string | null | undefined, pairings: TeamPairingResponse[], results: PlayerRoundResult[]): EncounterRow[] {
  const byRound = new Map<number, PlayerRoundResult>();
  for (const r of results) byRound.set(r.roundNumber, r);
  const hasIndividual = results.length > 0;

  const rows: EncounterRow[] = [];
  const seen = new Set<number>();
  for (const p of pairings) {
    const home = sameName(p.homeTeam, teamName);
    if (!home && !sameName(p.awayTeam, teamName)) continue;
    const own = home ? p.homeScore : p.awayScore;
    const other = home ? p.awayScore : p.homeScore;
    const score = own != null && other != null ? `${own} : ${other}` : null;
    const match: TeamMatch = { opponentTeam: home ? p.awayTeam : p.homeTeam, home, score };
    const ind = byRound.get(p.roundNumber);
    seen.add(p.roundNumber);
    rows.push(teamRow(p.roundNumber, match, ind, hasIndividual));
  }
  // Einzelergebnisse ohne bekannten Mannschaftskampf (Runde noch nicht geholt): trotzdem zeigen.
  for (const r of results) {
    if (!seen.has(r.roundNumber)) rows.push(teamRow(r.roundNumber, null, r, true));
  }
  return rows.sort((a, b) => a.round - b.round);
}

function teamRow(round: number, match: TeamMatch | null, ind: PlayerRoundResult | undefined, hasIndividual: boolean): EncounterRow {
  const raw = (ind?.result ?? '').trim();
  const result = raw ? (normalizePart(raw) ?? (raw === '-' ? '−' : raw)) : null;
  const opponent = ind?.opponentName?.trim() || null;
  const matchPlayed = match?.score != null;
  return {
    round,
    board: ind?.boardNumber || null,
    colour: null,
    opponent,
    opponentSnr: ind?.opponentSnr ?? null,
    opponentElo: ind?.opponentElo ?? null,
    result,
    bye: !!ind && !opponent && result !== null,
    // Mannschaft hat gespielt, der Spieler steht in dieser Runde nicht auf der Spielerseite.
    notPlayed: hasIndividual && matchPlayed && !ind,
    match,
    pending: result === null && !matchPlayed && !(hasIndividual && matchPlayed),
    next: false,
  };
}
