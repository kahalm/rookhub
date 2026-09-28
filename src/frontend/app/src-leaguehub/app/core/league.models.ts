// Datenformen der LeagueHub-API (GET /api/league/{tnr} usw.). Die Feldnamen folgen bewusst der
// Python-Fassung (snake_case) — die API prüft ihre Ausgabe direkt gegen deren Ausgabe.

export interface LeagueIndex {
  season: string;
  generated: string | null;
  leagues: { tnr: number; name: string }[];
}

export interface Candidate { n: string; elo: number | null; rb: number | null; p: number; fide: string | null }

export interface ActualBoard {
  n: string; elo: number | null; rank: number | null; score: string | null; own: string | null; vs: string; vs_elo: number | null;
}

export interface Board {
  board: number;
  /** Farbe des GEGNERS an diesem Brett ("w" | "s"). */
  opp_color: 'w' | 's';
  cand: Candidate[];
  other: number;
  actual?: ActualBoard;
  actual_p?: number;
}

export interface Account { site: string; user: string; url: string; conf: string }

export interface RosterEntry {
  rb: number | null; n: string; elo: number | null; p: number; prev: string; cur: string;
  fide: string | null; g: number; acc: Account[];
}

export type Phase = 'R1' | 'R2+' | 'So vorab' | 'So nach Sa';

export interface Fixture {
  bye?: boolean;
  opp?: string;
  home?: boolean;
  date?: string | null;
  time?: string | null;
  venue?: string | null;
  status?: 'played' | 'open' | 'locked' | 'nodata';
  score?: string;
  unlock_after?: number;
  boards?: Board[];
  roster?: RosterEntry[];
  phase?: Phase;
  hit?: number | null;
}

export interface LeagueRound { round: number; date: string | null; played: boolean; open: boolean }

export interface League {
  tnr: number;
  name: string;
  season: string;
  level: number;
  boards: number;
  teams: string[];
  rounds: LeagueRound[];
  fixtures: Record<string, Record<string, Fixture>>;
  source: string;
}

export interface OpeningStats { n: number; first: [string, number, number | null][]; lines: [string, number, number | null][] }

export interface PlayerCard {
  fide: string;
  name?: string;
  n: number;
  with_moves?: number;
  years?: [string, string] | null;
  src?: Record<string, number>;
  white?: OpeningStats;
  black_e4?: OpeningStats;
  black_d4?: OpeningStats;
  black_other?: OpeningStats;
  recent?: { date: string; event: string; vs: string; vs_elo: string; color: 'w' | 's'; score: number | null; opening: string }[];
  accounts: Account[];
}

/** `GET …/player/{fide}/recent` — die letzten Partien der Karte samt PGN (zum Nachspielen). */
export interface RecentGame { date: string; vs: string; color: 'w' | 's'; pgn: string }
export interface RecentGames { fide: string; games: RecentGame[] }

export interface SharedFixture {
  league: string; season: string; round: number; team: string; fixture: Fixture; generated: string; expires: string;
}

export interface UpdateStatus { running: boolean; started: string | null; finished: string | null; ok: boolean | null; message: string | null }

/** `GET …/player/{fide}/tree` — Eröffnungsbaum eines Spielers mit einer Farbe ab einer Zugfolge. */
export interface OpeningTree {
  fide: string;
  name: string;
  color: 'w' | 's';
  /** Die Zugfolge bis hierher (englische SAN, mit Leerzeichen). */
  line: string;
  /** Partien mit dieser Farbe, die so begonnen haben. */
  total: number;
  /** Davon hier zu Ende (oder am Tiefen-Deckel). */
  ended: number;
  /** Nächste Züge: Anzahl, Score aus SEINER Sicht (%, null = kein Ergebnis), jüngstes Jahr. */
  moves: { san: string; n: number; score: number | null; last: string | null }[];
}
