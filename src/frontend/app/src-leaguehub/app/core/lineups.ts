import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Chess } from 'chess.js';
import { de } from './league-format';

/**
 * Aufstellungen je Runde + erste Züge je Partie (2026-10-08, Wunsch: „für jede Runde einen Knopf, der mir alle Aufstellungen
 * dieser Runde anzeigt — dann bei jeder Partie die ersten paar Züge eingeben"). Server: `LeagueLineupsController`.
 */

export interface LineupBoard {
  board: number;
  homePlayer: string | null; homeTitle: string | null; homeElo: number | null;
  awayPlayer: string | null; awayTitle: string | null; awayElo: number | null;
  /** 0.727.2: FIDE-IDs der Paarung — mit ID öffnet der Name die Spielerkarte. */
  homeFide?: string | null; awayFide?: string | null;
  /** 0.739.0: aus der zugeordneten Vereinspartie (chess-results hat die Runde noch nicht). */
  provisional?: boolean;
  /** Farbe des HEIMspielers: „w" | „s". */
  homeColor: string | null;
  /** Aus Sicht Heim – Gast, wie chess-results („1 - 0" = Heim gewinnt). */
  result: string;
  forfeit: number;
  /** Englische SAN mit Leerzeichen. */
  moves: string | null;
  /** Mit vorhandener Partie (`game`) immer `false` — dann gibt es keine Zug-Eingabe mehr. */
  canEditMoves: boolean;
  /** 0.724.0: die vorhandene Partie des Bretts (ohne PGN), sonst `null`. */
  game?: LineupGame | null;
  /** 0.724.0: den Handeintrag löschen dürfen — auch neben einer Partie (dort ist er „ersetzt"). */
  canDeleteMoves?: boolean;
}

/**
 * Die vorhandene Partie eines Bretts (0.724.0, Wunsch 2026-10-08: „wenn ich die Partie hab, soll er nicht Züge eingeben lassen,
 * sondern die Partie ausweisen") — dieselbe Regel wie die Paarungen gespielter Runden: Vereinspartie des eigenen Vereins
 * (feste Zuordnung, sonst geraten) oder Spielerkarte. Das PGN holt erst „Nachspielen".
 */
export interface LineupGame {
  source: 'club' | 'profile';
  clubGameId: number | null;
  plies: number;
  result: string;
  white: string | null;
  black: string | null;
  /** Die ersten 10 Halbzüge, englische SAN. */
  firstMoves: string[];
  /** Darf die Vereinspartie bearbeiten/korrigieren (Verwalter oder Hochladender). */
  canEdit: boolean;
}

export interface LineupMatch {
  matchNo: number | null; home: string; away: string; homePts: number | null; awayPts: number | null;
  /** Eine Mannschaft des Vereins spielt mit. */
  own: boolean;
  /** Leer = noch keine Aufstellung. */
  boards: LineupBoard[];
}

export interface RoundLineups { tnr: number; round: number; date: string | null; canEdit: boolean; matches: LineupMatch[] }

/** Schlüssel einer Partie: Liga, Runde, Begegnung, Brett. */
export interface MovesKey { tnr: number; round: number; matchNo: number; board: number }

@Injectable({ providedIn: 'root' })
export class LineupsApiService {
  private readonly http = inject(HttpClient);

  lineups(tnr: number, round: number): Promise<RoundLineups> {
    return firstValueFrom(this.http.get<RoundLineups>(`/api/league/${tnr}/round/${round}/lineups`));
  }

  /** Speichern (leer = löschen) → die gespeicherten Züge (englische SAN) oder `null`. */
  async saveMoves(k: MovesKey, moves: string): Promise<string | null> {
    const r = await firstValueFrom(this.http.put<{ moves: string | null }>(
      `/api/league/${k.tnr}/round/${k.round}/match/${k.matchNo}/board/${k.board}/moves`, { moves }));
    return r.moves ?? null;
  }

  /** Den Handeintrag löschen (auch den „ersetzten" neben einer Partie). */
  async deleteMoves(k: MovesKey): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/league/${k.tnr}/round/${k.round}/match/${k.matchNo}/board/${k.board}/moves`));
  }

  /** PGN einer Vereinspartie samt Stand der Analyse (wie die Vereinsliste, `league.view`, Verein über `?club=`). */
  clubGame(id: number): Promise<{ pgn: string; analysis?: unknown | null }> {
    return firstValueFrom(this.http.get<{ pgn: string; analysis?: unknown | null }>(`/api/league/club/games/${id}`));
  }

  /** PGN einer Spielerkarten-Partie über denselben Weg wie `lh-fixture` (`…/round/{r}/games?team=`), Brett `board`. */
  async fixturePgn(tnr: number, round: number, team: string, board: number): Promise<string | null> {
    const list = await firstValueFrom(this.http.get<{ board: number; pgn: string | null }[]>(
      `/api/league/${tnr}/round/${round}/games?team=${encodeURIComponent(team)}`));
    return list.find(p => p.board === board)?.pgn ?? null;
  }
}

/** Höchstens so viele Halbzüge (wie der Server). */
export const MAX_PLIES = 60;

const GERMAN: Record<string, string> = { S: 'N', L: 'B', T: 'R', D: 'Q' };

/** Deutsche Figurenbuchstaben → englische (auch bei der Umwandlung), „0-0" → „O-O". */
export function englishToken(t: string): string {
  let s = t.replace(/^0-0-0/, 'O-O-O').replace(/^0-0/, 'O-O');
  if (GERMAN[s[0]]) s = GERMAN[s[0]] + s.slice(1);
  return s.replace(/([=18])([SLTD])/g, (_m, a: string, p: string) => a + GERMAN[p]);
}

export interface ParsedMoves {
  /** Die legalen Züge (englische SAN, wie chess.js sie schreibt) — bei einem Fehler die bis davor. */
  sans: string[];
  error: { kind: 'illegal'; ply: number; move: string } | { kind: 'tooLong' } | null;
}

/** Getippte/eingefügte Züge lesen: Zugnummern, Kommentare/Varianten in Klammern, Ergebnis, deutsche Buchstaben. */
export function parseMoves(text: string): ParsedMoves {
  const tokens = text.replace(/\{[^}]*\}|\([^)]*\)|\$\d+/g, ' ').replace(/\b\d+\s*(?:\.+|…)/g, ' ')
    .split(/\s+/).filter(t => t && !/^(?:1-0|0-1|1\/2-1\/2|½-½|\*|\.+|…)$/.test(t));
  const chess = new Chess();
  const sans: string[] = [];
  for (let i = 0; i < tokens.length; i++) {
    if (i >= MAX_PLIES) return { sans, error: { kind: 'tooLong' } };
    let m;
    try { m = chess.move(englishToken(tokens[i]), { strict: false }); } catch { m = null; }
    if (!m) return { sans, error: { kind: 'illegal', ply: i + 1, move: tokens[i] } };
    sans.push(m.san);
  }
  return { sans, error: null };
}

/** „1.e4 c5 2.Sf3" — mit Zugnummern, deutschen Figurenbuchstaben (wie der Rest von LeagueHub). */
export function formatMoves(sans: readonly string[] | string | null | undefined): string {
  const list = typeof sans === 'string' ? sans.split(' ').filter(Boolean) : [...(sans ?? [])];
  return de(list.map((s, i) => (i % 2 === 0 ? `${i / 2 + 1}.${s}` : s)).join(' '));
}

/** Stellung nach den Zügen (FEN). */
export function fenAfter(sans: readonly string[]): string {
  const chess = new Chess();
  for (const s of sans) chess.move(s);
  return chess.fen();
}

/** Letzter Zug als [von, nach] fürs Brett. */
export function lastMoveOf(sans: readonly string[]): [string, string] | undefined {
  if (!sans.length) return undefined;
  const chess = new Chess();
  let last: [string, string] | undefined;
  for (const s of sans) { const m = chess.move(s); last = [m.from, m.to]; }
  return last;
}

/** „2½" statt 2.5. */
export function points(p: number | null | undefined): string {
  if (p == null) return '';
  const whole = Math.floor(p);
  return p - whole >= 0.5 ? `${whole || ''}½` : String(whole);
}

/** Die Fehlermeldung des Servers (400/403/404) bzw. der eigenen Prüfung als Satz. */
export function movesErrorText(e: ParsedMoves['error'] | { reason?: string; move?: string; ply?: number } | null, status = 400): string {
  if (status === 403) return 'Diese Züge darfst du nicht ändern — nur an Begegnungen deines Vereins, und Einträge anderer nur als Verwalter.';
  if (status === 404) return 'Diese Paarung gibt es nicht mehr — bitte die Seite neu laden.';
  if (!e) return 'Speichern hat nicht geklappt.';
  const kind = 'kind' in e ? e.kind : e.reason;
  if (kind === 'tooLong') return `Höchstens ${MAX_PLIES} Halbzüge (${MAX_PLIES / 2} Züge) — es geht um die ersten Züge.`;
  if (kind === 'noGame') return 'An diesem Brett wurde nicht gespielt.';
  if (kind === 'illegal' && 'ply' in e && e.ply)
    return `Zug ${Math.ceil(e.ply / 2)}${e.ply % 2 ? '.' : '…'} ${e.move ?? ''} ist in dieser Stellung nicht möglich.`;
  return 'Speichern hat nicht geklappt.';
}
