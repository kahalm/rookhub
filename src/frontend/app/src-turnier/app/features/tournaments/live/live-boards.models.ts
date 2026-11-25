/** Antworten von `/api/live-boards/*` (Live-Bretter der Turnierseite, 1.014.0; `docs/api/live-bretter.md`). */

export interface LiveRoundInfo {
  round: number;
  lichessRoundId: string;
  name: string;
  startsAt: string | null;
  ongoing: boolean;
  finished: boolean;
}

export interface LiveStatus {
  linked: boolean;
  manual: boolean;
  tourId: string | null;
  tourName: string | null;
  url: string | null;
  rounds: LiveRoundInfo[];
  currentRound: number | null;
}

export interface LivePlayer {
  name: string;
  title: string | null;
  elo: number | null;
  fideId: string | null;
  team: string | null;
  /** Restzeit nach dem letzten eigenen Zug (Sekunden, aus `[%clk]`). */
  clockSeconds: number | null;
}

/** Bewertung aus Weiß-Sicht: `cp` ODER `mate` (negativ = Schwarz setzt matt). */
export interface LiveEval { cp: number | null; mate: number | null; source: 'broadcast' | 'cloud'; depth: number | null; }

export interface LiveGame {
  id: string;
  boardTag: string | null;
  white: LivePlayer;
  black: LivePlayer;
  fen: string;
  /** UCI, z. B. `e2e4`. */
  lastMove: string | null;
  plies: number;
  result: string;
  eval: LiveEval | null;
  finished: boolean;
  whiteToMove: boolean;
}

export interface LiveBoardSlot {
  board: number;
  white: string;
  black: string;
  result: string | null;
  gameId: string | null;
  /** Nur Mannschaftsturnier: spielt die Heimmannschaft an diesem Brett Weiß? */
  homeIsWhite: boolean | null;
}

export interface LiveMatchSlot {
  matchNumber: number;
  home: string;
  away: string;
  homeScore: number | null;
  awayScore: number | null;
  liveHome: number;
  liveAway: number;
  boards: LiveBoardSlot[];
}

export interface LiveRoundLayout {
  team: boolean;
  matches: LiveMatchSlot[];
  boards: LiveBoardSlot[];
  unassigned: string[];
}

/** Zustand der Verbindung RookHub ↔ Lichess bzw. Browser ↔ RookHub (`offline` = SSE zu, wird neu versucht). */
export type LiveState = 'connecting' | 'live' | 'reconnecting' | 'finished' | 'offline';

export type LiveStreamEvent =
  | { type: 'snapshot'; state: LiveState; round: number; layout: LiveRoundLayout; games: LiveGame[] }
  | { type: 'layout'; layout: LiveRoundLayout }
  | { type: 'game'; game: LiveGame }
  | { type: 'state'; state: LiveState };
