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

/** Ein Spieler zum Auswählen: aus den Meldelisten (`source: 'liga'`) oder dem Spielerverzeichnis der Megabase. */
export interface RosterPerson {
  name: string;
  fide: string | null;
  teams: string[];
  club: boolean;
  /** Ligaspieler (bei Megabase-Treffern nur mit passender FIDE-ID); fehlt = ja. */
  league?: boolean;
  source?: 'liga' | 'mega';
  games?: number | null;
  lastYear?: number | null;
  maxElo?: number | null;
}

/** Abgleich eines Namens mit den Meldelisten. `club` = spielt (jüngste Saison) für Schwaz. */
export interface SideMatch {
  league: boolean;
  ambiguous: boolean;
  name: string | null;
  fide: string | null;
  club: boolean;
  candidates: RosterPerson[];
  /** Nur über den Nachnamen gefunden (die Partie nennt keinen Vornamen) — prüfen. */
  lastNameOnly?: boolean;
  /** Kein Ligaspieler, aber eindeutig im Megabase-Verzeichnis (Name und FIDE-ID von dort) — „nicht in Liga". */
  mega?: boolean;
}

/** Eine Seite in der Übersicht vor dem Import (`POST …/games/preview`). */
export interface PreviewSide {
  raw: string | null;
  elo: number | null;
  match: SideMatch;
  /** Laut Profil die Seite des Hochladenden. */
  owner: boolean;
  /** Vorgabe „durch Schwaz ersetzen". */
  replace: boolean;
}

export interface PreviewGame {
  index: number;
  year: number | null;
  result: string;
  event: string | null;
  plies: number;
  opening: string;
  /** Nicht übernehmbar, egal wie man die Namen setzt: illegal, noMoves, tooLong, fromPosition. */
  error: string | null;
  duplicate: boolean;
  white: PreviewSide;
  black: PreviewSide;
}

export interface ClubPreview { games: PreviewGame[]; truncated: boolean }

export interface SideDecision { name: string | null; fide: string | null; replace: boolean }

export interface ImportGameDecision { index: number; white: SideDecision; black: SideDecision }

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
  whiteFide: string | null;
  blackFide: string | null;
  whiteElo: number | null;
  blackElo: number | null;
  whiteReplace: boolean;
  blackReplace: boolean;
  result: string;
  event: string | null;
  year: number | null;
  scanId: number | null;
}

/** Eine eigene Einlesung: angemeldet über die Nummer, ohne Konto über den geheimen Schlüssel. */
export interface ScanRef { ref: string; scan: ScoresheetScan }
