// Shared API response interfaces for type safety
import { RepertoireKind } from './repertoire.types';

// ── Tournament (from Crawler via Proxy) ─────────────────────────────────

export interface Tournament {
  id: number;
  name: string;
  chessResultsId: string;
  location: string | null;
  date: string | null;
  totalRounds: number;
  knownRounds: number;
  createdAt: string;
  updatedAt: string;
}

export interface TournamentPlayer {
  id: number;
  snr: number;
  title: string | null;
  name: string;
  fideId: string | null;
  elo: number | null;
  country: string | null;
  teamName: string | null;
  boardNumber: number | null;
}

export interface TournamentTeam {
  id: number;
  snr: number;
  name: string;
  players: TournamentPlayer[];
}

export interface TournamentPairing {
  id: number;
  roundNumber: number;
  boardNumber: number;
  white: string;
  black: string;
  result: string | null;
}

export interface TeamPairingResponse {
  id: number;
  roundNumber: number;
  matchNumber: number;
  homeTeam: string;
  awayTeam: string;
  homeScore: number | null;
  awayScore: number | null;
}

/** Display model used after transforming raw pairing data */
export interface DisplayPairing {
  board: number;
  white: string;
  black: string;
  result: string;
}

// ── Subscriptions ───────────────────────────────────────────────────────

export interface Subscription {
  id: number;
  crawlerTournamentId: string;
  tournamentName: string;
  subscribedAt: string;
  tournamentDbId: number | null;
}

// ── Friends ─────────────────────────────────────────────────────────────

export interface Friend {
  friendshipId: number;
  userId: number;
  username: string;
  displayName: string | null;
  chessComUsername: string | null;
  lichessUsername: string | null;
  fideId: string | null;
  chessResultsId: string | null;
}

export interface FriendRequest {
  friendshipId: number;
  requesterId: number;
  requesterUsername: string;
  createdAt: string;
}

/** Von mir gesendete, noch nicht angenommene Anfrage (ausstehend). */
export interface SentFriendRequest {
  friendshipId: number;
  addresseeId: number;
  addresseeUsername: string;
  addresseeDisplayName: string | null;
  createdAt: string;
}

export interface UserSearchResult {
  userId: number;
  username: string;
  displayName: string | null;
  chessComUsername: string | null;
  lichessUsername: string | null;
  fideId: string | null;
  chessResultsId: string | null;
}

// ── Repertoires ─────────────────────────────────────────────────────────
// RepertoireKind ist in `core/repertoire.types.ts` definiert.

export interface Repertoire {
  id: number;
  name: string;
  description: string | null;
  isPublic: boolean;
  kind: RepertoireKind;
  fileCount: number;
  /** Soll dieses Repertoire von der Browser-Extension/dem Userscript genutzt werden? */
  useForExtension: boolean;
  createdAt: string;
  updatedAt: string;
  chessableCourseId: string | null;
}

export interface RepertoireDetail {
  id: number;
  name: string;
  description: string | null;
  isPublic: boolean;
  kind: RepertoireKind;
  files: RepertoireFile[];
  useForExtension: boolean;
  createdAt: string;
  updatedAt: string;
  chessableCourseId: string | null;
}

export interface RepertoireFile {
  id: number;
  fileName: string;
  fileSize: number;
  uploadedAt: string;
}

// ── Puzzle Stats ────────────────────────────────────────────────────────

export interface PuzzleStatsDto {
  totalAttempts: number;
  solved: number;
  accuracy: number;
  currentStreak: number;
  bestStreak: number;
  puzzleElo: number;
}

// ── Tournament Favorites ────────────────────────────────────────────────

export interface TournamentFavorite {
  id: number;
  crawlerTournamentId: string;
  playerSnr: number | null;
  teamSnr: number | null;
}

// ── Runden-Monitor (TournamentMonitorController) ─────────────────────────

export interface TournamentMonitorStatus {
  active: boolean;
  /** ISO-Datum, bis wann der Monitor aktiv ist (null, wenn inaktiv). */
  activeUntil: string | null;
  /** Zuletzt erkannte Rundenzahl. */
  lastKnownRounds: number;
}

// ── Crawl-Job (vom Crawler durchgereicht) ────────────────────────────────

export type CrawlJobStatus = 'Queued' | 'Running' | 'Completed' | 'Failed';

export interface CrawlJob {
  id: number;
  status: CrawlJobStatus;
  errorMessage?: string | null;
}
