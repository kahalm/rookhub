// Vereins-Datenbank (`/api/league/club/*`) — Formen wie in DTOs/LeagueClubDtos.cs (camelCase).
import { ScoresheetLanguage, ScoresheetPly, ScoresheetScan, ScoresheetStatus } from '@rh/features/games/scoresheet.service';

export type { ScoresheetLanguage, ScoresheetPly, ScoresheetScan, ScoresheetStatus };

export interface ClubGame {
  id: number;
  year: number | null;
  white: string;
  black: string;
  whiteFide: string | null;
  blackFide: string | null;
  whiteElo: number | null;
  blackElo: number | null;
  result: string;
  event: string | null;
  plies: number;
  opening: string;
  anonymized: boolean;
  canDelete: boolean;
}

export interface ClubList { total: number; page: number; pageSize: number; items: ClubGame[] }

export interface ClubFailure { index: number; white: string | null; black: string | null; reason: string }

export interface ClubImportResult {
  added: number;
  duplicates: number;
  anonymized: number;
  truncated: boolean;
  ids: number[];
  failed: ClubFailure[];
}

export interface RosterPerson { name: string; fide: string | null; teams: string[] }

export interface SideMatch { league: boolean; ambiguous: boolean; name: string | null; fide: string | null }

export interface ClubMatch { white: SideMatch; black: SideMatch }

/** `GET /api/league/club/scans/{id}` — Stand einer Liga-Einlesung. */
export interface LeagueScanState {
  scan: ScoresheetScan;
  notationLanguage: string;
  written: string[];
  boxes: (number[] | null)[];
  plies: ScoresheetPly[];
  unresolved: string[];
  unresolvedFrom: number | null;
  white: string | null;
  black: string | null;
  event: string | null;
  date: string | null;
  result: string | null;
  ownerSide: 'white' | 'black' | null;
}

export interface ClubGameRequest {
  moves: string[];
  white: string | null;
  black: string | null;
  whiteElo: number | null;
  blackElo: number | null;
  result: string;
  event: string | null;
  year: number | null;
  ownerSide: 'white' | 'black' | null;
  anonymize: boolean;
  scanId: number | null;
}
