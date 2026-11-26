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
export interface LiveEval {
  cp: number | null; mate: number | null; source: 'broadcast' | 'cloud' | 'engine'; depth: number | null;
  /** Bester Zug dieser Bewertung (UCI, 1.030.0); `[%eval]` der Übertragung kennt keinen. */
  best?: string | null;
  /** Zweite Linie (MultiPV 2, 1.033.0), Weiß-Sicht wie `cp`/`mate`; fehlt, wenn unbekannt. */
  secondCp?: number | null;
  secondMate?: number | null;
  /** „Nur ein Zug“ (1.033.0): der Server hat entschieden (Regel in `LiveOnlyMove`), nur `true` oder weggelassen. */
  onlyMove?: boolean | null;
}

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
  /** Elo-Schnitt der Spieler an den Brettern (Strom, sonst Startliste; 1.025.0) und über wie viele Bretter. */
  homeElo?: number | null;
  homeEloBoards?: number;
  awayElo?: number | null;
  awayEloBoards?: number;
}

export interface LiveRoundLayout {
  team: boolean;
  matches: LiveMatchSlot[];
  boards: LiveBoardSlot[];
  unassigned: string[];
}

/** Zustand der Verbindung RookHub ↔ Lichess bzw. Browser ↔ RookHub (`offline` = SSE zu, wird neu versucht). */
export type LiveState = 'connecting' | 'live' | 'reconnecting' | 'finished' | 'offline';

/** Zeile der Partieauswahl einer fremden Runde (`GET /api/live-boards/external?url=`, 1.025.0). */
export interface ExternalGameRow {
  id: string; white: string; black: string; whiteTitle: string | null; blackTitle: string | null;
  whiteElo: number | null; blackElo: number | null; result: string;
}

/** Eine fremde Lichess-Runde mit ihren Partien; `gameId` = die Partie des eingefügten Partie-Links. */
export interface ExternalRoundView {
  roundId: string; roundName: string; tourName: string; url: string; ongoing: boolean; finished: boolean;
  gameId: string | null; games: ExternalGameRow[];
}

/**
 * Ereignisse des Stroms. `ext-*` (1.025.0) = zusätzliche Bretter aus fremden Übertragungen im SELBEN Strom, `round` =
 * Lichess-Runde; der Server schickt nur die gewählten Partien.
 */
export type LiveStreamEvent =
  | { type: 'snapshot'; state: LiveState; round: number; layout: LiveRoundLayout; games: LiveGame[] }
  | { type: 'layout'; layout: LiveRoundLayout }
  | { type: 'game'; game: LiveGame }
  | { type: 'state'; state: LiveState }
  | { type: 'ext-snapshot'; round: string; tourName: string; roundName: string; url: string; state: LiveState; games: LiveGame[] }
  | { type: 'ext-game'; round: string; game: LiveGame }
  | { type: 'ext-state'; round: string; state: LiveState }
  | { type: 'ext-error'; round: string; code: string }
  /** Geteilte Ansicht (1.029.0): Stand der Ansicht — zu Beginn und nach jeder Änderung des Besitzers, danach folgt ein neuer `snapshot`. */
  | { type: 'view'; token: string; tournamentId: string; view: LiveViewState }
  | { type: 'view-gone' };

/** Eine Live-Ansicht (wie die lange Adresse): `round` null = die laufende Runde. */
export interface LiveViewState {
  round: number | null;
  team: string | null;
  player: string | null;
  add: string[];
  ext: string[];
  eval: boolean;
}

/** Antwort von `/api/live-boards/views…`; `editKey` nur beim Anlegen. */
export interface LiveViewResponse {
  token: string;
  tournamentId: string;
  view: LiveViewState;
  canEdit: boolean;
  editKey?: string | null;
}
