// Vereins-Datenbank (`/api/league/club/*`) — Formen wie in DTOs/LeagueClubDtos.cs (camelCase).
import { ScoresheetEditState, ScoresheetLanguage, ScoresheetPly, ScoresheetScan, ScoresheetStatus } from '@rh/features/games/scoresheet.service';

export type { ScoresheetLanguage, ScoresheetPly, ScoresheetScan, ScoresheetStatus };
/** Aufbewahrtes Formular einer Vereinspartie (0.660.0) — dieselbe Form wie bei einer eigenen Partie in RookHub. */
export type ClubSheetState = ScoresheetEditState;

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
  /** Züge als UCI mit Leerzeichen — für „Analyse" (RookHubs Analysebrett `?moves=`). */
  uci?: string;
  /** Die Partie als PGN (0.592.0) — „Analyse" gibt sie mit (`?pgn=`). */
  pgn?: string;
  /** Seite ohne FIDE-ID, deren Name in einer Meldeliste steht (Ligaspieler ohne FIDE-ID, 0.594.0). */
  whiteInRoster?: boolean;
  blackInRoster?: boolean;
  /** Stand der Hintergrund-Analyse (0.593.0); fehlt/`null` = noch keine. */
  analysis?: ClubGameAnalysis | null;
  /** Fest zugeordnete Brettpaarung (0.678.0) — nur für wer bearbeiten darf, sonst fehlt sie. */
  leagueGameId?: number | null;
  /** „2026/27 · Landesliga · Runde 2 · Brett 4 (04.10.2026)". */
  leagueGameLabel?: string | null;
}

/** Eine Brettpaarung, die eine Vereinspartie sein könnte (0.678.0, `LeagueClubPairingDto`). */
export interface ClubPairing {
  id: number;
  label: string;
  white: string;
  whiteFide: string | null;
  black: string;
  blackFide: string | null;
  result: string;
  /** Die Seite spielte für Schwaz — wird beim Übernehmen zu „Schwaz". */
  whiteOwnClub: boolean;
  blackOwnClub: boolean;
  /** Beide Spieler in ihren Farben und der Tag passen. */
  exact: boolean;
}

/** Stand der Analyse einer Vereinspartie — dieselbe Form wie `analysis` in RookHubs Partienliste. */
export interface ClubGameAnalysis {
  status: 'pending' | 'running' | 'done' | 'failed';
  analyzed: number;
  total: number;
  /** Genauigkeit in Prozent, nur bei `done`; `null` = die Seite hat keinen bewertbaren Zug. */
  accuracyWhite: number | null;
  accuracyBlack: number | null;
}

/** Eine Vereinspartie zum Nachspielen (`GET …/club/games/{id}`). */
export interface ClubGameDetail extends ClubGame { pgn: string }

export interface ClubList { total: number; page: number; pageSize: number; items: ClubGame[] }

export interface ClubFailure { index: number; white: string | null; black: string | null; reason: string }

export interface ClubImportResult {
  added: number;
  duplicates: number;
  anonymized: number;
  /** Aus den Korrekturen neu gemerkte Namens-Zuordnungen (nur mit Konto). */
  remembered?: number;
  truncated: boolean;
  ids: number[];
  failed: ClubFailure[];
  /** Nur über einen Teilen-Link (0.656.0): Schlüssel für eine Zuordnung nach dem Anmelden. */
  claimKey?: string | null;
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
  /** Elo laut jüngster Meldeliste — belegt beim Auswählen das Elo-Feld vor (0.672.7). */
  elo?: number | null;
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
  /** Über eine gemerkte Zuordnung — jemand hat diesen Namen schon einmal so korrigiert. */
  alias?: boolean;
  /** Nicht erkannt, aber ähnlich geschriebene Ligaspieler („Spindlberger" → „Spindelberger") zur Schnellauswahl (0.596.0). */
  similar?: RosterPerson[];
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
  /** Die Partie als eigener PGN-Text (0.590.0, fehlt bei harten Fehlern) — damit importiert die Seite portionsweise. */
  pgn?: string | null;
  /** Brettpaarungen, die diese Partie sein könnten (0.678.0), genaue zuerst. */
  pairings?: ClubPairing[];
  /** Die vorgewählte: genau EIN genauer Vorschlag, sonst keine. */
  pairingId?: number | null;
}

export interface ClubPreview { games: PreviewGame[]; truncated: boolean }

export interface SideDecision { name: string | null; fide: string | null; replace: boolean }

/** `leagueGameId`: die Brettpaarung (0.678.0) — `0` = keine, fehlt = die eindeutig erkannte. */
export interface ImportGameDecision { index: number; white: SideDecision; black: SideDecision; leagueGameId?: number | null }

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
  /** Formular über mehrere Blätter (0.690.1): Zahl der Fotos und je Eintrag seine Seite (ab 1). */
  pageCount?: number;
  pages?: number[];
  /** Je Seite (Index = Seite − 1) die Zahl WEITERER Fotos derselben Seite (0.736.0). */
  viewCounts?: number[];
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
  /** Die Brettpaarung (0.678.0): `0` = keine, fehlt = die eindeutig erkannte. */
  leagueGameId?: number | null;
  /** Der Tag (JJJJ.MM.TT), wenn bekannt — nur für die Erkennung der Paarung. */
  date?: string | null;
  /** Stand je Zug beim Übernehmen eines Formulars (0.693.4) — fürs Archiv. */
  plies?: ScoresheetPly[];
}

/** `POST …/club/pairings` — Vorschläge für eine noch nicht gespeicherte Partie (Formular). */
export interface PairingQuery {
  white: string | null;
  whiteFide: string | null;
  black: string | null;
  blackFide: string | null;
  date: string | null;
  year: number | null;
}

/** Eine eigene Einlesung: angemeldet über die Nummer, ohne Konto über den geheimen Schlüssel. */
export interface ScanRef { ref: string; scan: ScoresheetScan }

/** `GET …/admin/scans` — eine offene Liga-Einlesung für die Verwalter (auch fremde und über Teilen-Links). */
export interface OpenScan { scan: ScoresheetScan; viaShareLink: boolean; mine: boolean }

/** `PUT …/games/{id}` — eine Seite ohne Angabe bleibt, wie sie ist; „Schwaz" lässt sich nicht ändern. */
/** `leagueGameId`: fehlt = unverändert, `0` = keine Ligapartie (0.678.0). */
export interface ClubGameUpdate {
  white?: SideDecision | null; black?: SideDecision | null; result?: string | null; leagueGameId?: number | null;
}

/** Ein Entwurf eines PGN-Imports (0.595.0): liegt online, bis alles importiert oder verworfen ist. `ref` = Nummer
 *  (angemeldet) bzw. geheimer Schlüssel (ohne Konto). */
/** Eine ChessBase-Datenbank als PGN (0.598.0, `POST …/club/games/chessbase`). */
export interface ChessBaseResult {
  format: 'cbh' | '2cbh';
  name: string;
  pgn: string;
  games: number;
  converted: number;
  deleted: number;
  truncated: boolean;
  skippedCount: number;
  skipped: { id: number; white: string; black: string; reason: string }[];
}

export interface ClubDraft {
  ref: string;
  id: number;
  key?: string | null;
  source: string | null;
  label: string | null;
  gameCount: number;
  importedCount: number;
  createdAt: string;
  updatedAt: string;
  /** Wer eingereicht hat (Liste der Verwalter). */
  owner?: string | null;
  viaShareLink: boolean;
  mine: boolean;
}

export interface ClubDraftDetail extends ClubDraft {
  pgn: string;
  /** Stand der Übersicht (JSON dieser Seite). */
  state: string | null;
  imported: number[];
}
