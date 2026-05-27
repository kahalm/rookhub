// Shared API response interfaces for type safety

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

export interface Repertoire {
  id: number;
  name: string;
  description: string | null;
  isPublic: boolean;
  fileCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface RepertoireDetail {
  id: number;
  name: string;
  description: string | null;
  isPublic: boolean;
  files: RepertoireFile[];
  createdAt: string;
  updatedAt: string;
}

export interface RepertoireFile {
  id: number;
  fileName: string;
  fileSize: number;
  uploadedAt: string;
}

// ── Tournament Favorites ────────────────────────────────────────────────

export interface TournamentFavorite {
  id: number;
  crawlerTournamentId: string;
  playerSnr: number | null;
  teamSnr: number | null;
}
