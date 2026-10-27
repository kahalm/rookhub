import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Chess } from 'chess.js';
import { de } from './league-format';
import { ReplayMark } from '@rh/shared/player-card/game-replay.component';
import { GameEvals, reviewGame } from '@rh/features/games/game-review.util';
import { uciOf } from '@rh/features/games/move-tactics.util';
import { parsePgnText } from '@rh/shared/pgn-viewer/pgn-parser';

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

/** Vereinsübersicht (0.848.0, `GET /api/league/club/overview`): je Liga die Runden mit den eigenen Begegnungen; `marks` = Zahl
 *  markierter Stellungen je Vereinspartie (fehlt = keine). */
export interface ClubOverview {
  season: string | null;
  leagues: { tnr: number; name: string; rounds: { number: number; date: string | null; matches: LineupMatch[] }[] }[];
  marks: Record<string, number>;
}

/** Eingabe der „!!"-Marker je Vereinspartie: vorgerechnet (`brilliant`, 0.854.0) oder PGN + Bewertungen zum Selbstrechnen. */
export interface ClubGameReviewInput {
  pgn?: string | null;
  evals?: GameEvals | null;
  brilliant?: number[] | null;
}

/** Die brillanten Halbzüge („!!", 0-basiert) einer Partie — derselbe Rückblick wie auf RookHubs Partieseite (`reviewGame`). */
export function brilliantPlies(pgn: string, evals: GameEvals): number[] {
  let g;
  try { g = parsePgnText(pgn)[0]; } catch { return []; }
  if (!g) return [];
  return reviewGame(evals, g.fens, g.moves.map(uciOf)).moves.filter(m => m?.cls === 'brilliant').map(m => m!.ply);
}

/** Zahl der brillanten Züge — siehe {@link brilliantPlies}. */
export function brilliantMoves(pgn: string, evals: GameEvals): number {
  return brilliantPlies(pgn, evals).length;
}

const NONE: readonly number[] = [];

/** Klick auf ein Abzeichen (0.852.0): beim Nachspielen zur ersten markierten Stellung bzw. zu diesem Halbzug springen. Jeder Klick
 *  ist ein neues Objekt — das Nachspielen springt erneut, auch wenn es schon offen ist. */
export type ReplayStart = { kind: 'mark' } | { kind: 'ply'; ply: number };

/**
 * „+ n" (markierte Stellungen) und „!! n" (brillante Züge) je Vereinspartie — geteilt von der Vereinsübersicht und den
 * Aufstellungen einer Runde (0.850.0, Wunsch 2026-10-27: „die Anzeige von + und !! auch, wenn man sich eine Runde anschaut") und
 * der Liste der Vereinspartien (0.851.0: „markiere diese Partien auch so in der Liste Vereinsspiele").
 * `load` holt die Zahlen in Paketen nach; ein neuer `load` verwirft späte Antworten des vorigen. Nur angemeldet (`league.view`).
 */
export class GameMarkers {
  readonly marks = signal<Record<number, number>>({});
  /** Je Vereinspartie die brillanten Halbzüge (0.852.0: für den Sprung beim Klick aufs Abzeichen). */
  readonly brilliant = signal<Record<number, number[]>>({});
  private run = 0;

  constructor(private readonly api: LineupsApiService) {}

  /** Die Vereinspartien aller Bretter. */
  static idsOf(matches: readonly LineupMatch[]): number[] {
    return [...new Set(matches.flatMap(m => m.boards).map(b => b.game?.clubGameId).filter((id): id is number => !!id))];
  }

  /** `marks` schon bekannt (Übersicht) → nur „!!" nachladen. Leert vorher; späte Antworten eines früheren `load` fallen weg. */
  load(ids: readonly number[], marks?: Record<string, number>): Promise<void> {
    this.clear();
    return this.add(ids, marks);
  }

  /** Weitere Partien dazu, ohne die bisherigen Marker zu leeren (Vereinspartien: nächste Seite, 0.851.0). */
  async add(ids: readonly number[], marks?: Record<string, number>): Promise<void> {
    const run = this.run;
    const asNum = (r: Record<string, number>) => Object.fromEntries(Object.entries(r).map(([k, v]) => [Number(k), v]));
    if (marks) this.marks.update(cur => ({ ...cur, ...asNum(marks) }));
    else {
      for (let i = 0; i < ids.length; i += 200) {
        try {
          const got = await this.api.clubGameMarkCounts(ids.slice(i, i + 200));
          if (run !== this.run) return;
          this.marks.update(cur => ({ ...cur, ...asNum(got) }));
        } catch { /* ohne Marker weiter */ }
      }
    }
    for (let i = 0; i < ids.length; i += 60) {
      let got: Record<string, ClubGameReviewInput>;
      try { got = await this.api.clubGameReviews(ids.slice(i, i + 60)); } catch { continue; }
      if (run !== this.run) return;
      // Je Partie ein eigener Task (0.851.1): der Rückblick kostet ~50 ms, ein ganzes Paket am Stück fror den Browser ein
      // („this page is slowing down Firefox").
      // Vorgerechnet (0.854.0, Erntelauf am Server): die „!!"-Halbzüge kommen fertig — auf einmal übernehmen.
      const ready = Object.entries(got).filter(([, r]) => r.brilliant);
      if (ready.length) this.brilliant.update(cur => ({ ...cur, ...Object.fromEntries(ready.map(([id, r]) => [Number(id), r.brilliant!])) }));
      for (const [id, r] of Object.entries(got)) {
        if (r.brilliant || !r.pgn || !r.evals) continue;
        await new Promise(resolve => setTimeout(resolve));
        if (run !== this.run) return;
        const plies = brilliantPlies(r.pgn, r.evals);
        this.brilliant.update(cur => ({ ...cur, [Number(id)]: plies }));
      }
    }
  }

  /** Nach Id statt Brett (Vereinspartien-Liste). */
  marksOf(id: number): number {
    return this.marks()[id] ?? 0;
  }

  brilliantOf(id: number): readonly number[] {
    return this.brilliant()[id] ?? NONE;
  }

  /** Verwirft laufende Antworten und leert die Marker (z. B. über einen Teilen-Link). */
  clear(): void {
    this.run++;
    this.marks.set({});
    this.brilliant.set({});
  }

  markCount(b: LineupBoard): number {
    const id = b.game?.clubGameId;
    return id ? this.marks()[id] ?? 0 : 0;
  }

  brilliantPlies(b: LineupBoard): readonly number[] {
    const id = b.game?.clubGameId;
    return id ? this.brilliant()[id] ?? NONE : NONE;
  }
}

/** Schlüssel einer Partie: Liga, Runde, Begegnung, Brett. */
export interface MovesKey { tnr: number; round: number; matchNo: number; board: number }

@Injectable({ providedIn: 'root' })
export class LineupsApiService {
  private readonly http = inject(HttpClient);

  /** Angemeldet über Liga und Runde — oder OHNE Anmeldung über einen Teilen-Link (0.838.0): dann bestimmt der Server
   *  Liga, Runde und Verein, und nichts ist beschreibbar. */
  lineups(tnr: number | null, round: number | null, token: string | null = null): Promise<RoundLineups> {
    const url = token ? `/api/league/s/${encodeURIComponent(token)}/lineups` : `/api/league/${tnr}/round/${round}/lineups`;
    return firstValueFrom(this.http.get<RoundLineups>(url));
  }

  /** PGN einer Partie der GETEILTEN Begegnung über den Link (`…/s/{token}/games`, 0.838.0) — nur deren Bretter kennt der Server. */
  async sharedPgn(token: string, board: number): Promise<string | null> {
    const list = await firstValueFrom(this.http.get<{ board: number; pgn: string | null }[]>(
      `/api/league/s/${encodeURIComponent(token)}/games`));
    return list.find(p => p.board === board)?.pgn ?? null;
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

  /** Interessante Stellungen einer Vereinspartie (0.848.0, `league.view`) — Sprungmarken fürs Nachspielen. */
  clubGameMarks(id: number): Promise<ReplayMark[]> {
    return firstValueFrom(this.http.get<ReplayMark[]>(`/api/league/club/games/${id}/marks`));
  }

  /** Alle Begegnungen der eigenen Mannschaften der laufenden Saison mit Brettern, Partien und Markern (0.848.0). */
  overview(): Promise<ClubOverview> {
    return firstValueFrom(this.http.get<ClubOverview>('/api/league/club/overview'));
  }

  /** PGN + schlanke Bewertungen der analysierten Vereinspartien unter `ids` (höchstens 60, 0.849.0) — für die „!!"-Marker; seit
   *  0.854.0 bei geernteten Partien gleich die fertigen „!!"-Halbzüge (`brilliant`) ohne PGN und Bewertungen. */
  clubGameReviews(ids: readonly number[]): Promise<Record<string, ClubGameReviewInput>> {
    return firstValueFrom(this.http.get<Record<string, ClubGameReviewInput>>(
      `/api/league/club/games/reviews?ids=${ids.join(',')}`));
  }

  /** Zahl der markierten Stellungen je Vereinspartie unter `ids` (höchstens 200, 0.850.0) — für die „◆"-Marker einer Runde. */
  clubGameMarkCounts(ids: readonly number[]): Promise<Record<string, number>> {
    return firstValueFrom(this.http.get<Record<string, number>>(`/api/league/club/games/marks?ids=${ids.join(',')}`));
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
