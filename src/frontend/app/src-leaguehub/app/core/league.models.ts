// Datenformen der LeagueHub-API (GET /api/league/{tnr} usw.). Die Feldnamen folgen bewusst der
// Python-Fassung (snake_case) — die API prüft ihre Ausgabe direkt gegen deren Ausgabe.

export interface LeagueIndex {
  season: string;
  generated: string | null;
  leagues: { tnr: number; name: string }[];
}

/** Partien im Bestand je Quelle (0.626.0, `GET /api/league/sources`). `key`: Lumbra, Mega, chess-results, Lichess-Übertragung,
 *  Verein bzw. lichess, chess.com (oder ein frei benannter Import). */
export interface GameSourceRow { key: string; label: string; games: number }
export interface GameSources {
  board: GameSourceRow[]; boardTotal: number;
  online: GameSourceRow[]; onlineTotal: number;
  countedAt: string;
  /** Nur mit Liga (0.628.0): dieselbe Zählung für alle Meldelisten der Liga. */
  league?: PlayerSources;
  /** Nur mit Begegnung (0.628.0): dieselbe Zählung für die Meldeliste des Gegners. */
  opponent?: PlayerSources;
}
/** Zählung für eine Gruppe Spieler; online nur Seiten, auf denen einer von ihnen ein Konto hat. */
export interface PlayerSources {
  players: number;
  board: Record<string, number>; boardTotal: number;
  online: Record<string, { games: number; accounts: number }>; onlineTotal: number; onlineAccounts: number;
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

/** Ein Online-Konto. `conf` „sicher" = gesichert, sonst unsicher. Angemeldet kommen Kommentar und Abrufstand mit (0.605.0). */
export interface Account {
  /** Bei einem Minderjährigen (`hidden`) leer — der Server verrät Seite, Name und Adresse nicht (0.610.0). */
  site: string | null; user: string | null; url: string | null; conf: string;
  hidden?: boolean;
  /** Minderjährig, aber ein ADMIN sieht es (0.625.0) — vollständig, mit dem Hinweis „nur für Admins sichtbar". */
  minor?: boolean;
  id?: number; comment?: string | null; games?: number; syncedAt?: string | null; error?: string | null;
  /** Wer es eingetragen hat (0.630.0): Nutzername oder „anonym" (über einen Teilen-Link); nur angemeldet. */
  addedBy?: string | null;
}

/** Ein Konto, das die Konto-Suche gefunden hat (0.607.0) — ein Verwalter übernimmt oder verwirft es. */
export interface AccountSuggestion {
  /** Bei einem Minderjährigen (`hidden`) leer — entschieden wird nach den Hinweisen (0.610.0). */
  id: number; fide: string; site: string | null; user: string | null; url: string | null; hidden?: boolean;
  /** Minderjährig, für einen Admin trotzdem vollständig (0.625.0). */
  minor?: boolean;
  /** Wie stark die Hinweise sind (sortiert die Liste). */
  score: number;
  /** Die Hinweise als Satz („Nutzername aus dem Namen; Klarname im Profil …"). */
  evidence: string;
  profileName: string | null; location: string | null; lastActive: string | null;
  /** Nur in der Übersicht: Name und Mannschaft des Spielers. */
  name?: string; team?: string | null;
}

/** `GET …/suggestions`: offene Vorschläge; in der Übersicht dazu, wie viele Spieler schon abgesucht sind. */
/** Eine Prüfung der Konto-Prüfung (i), 0.619.0: `ok` spricht dafür, `weak` schwächer dafür (0.621.0), `warn` macht stutzig,
 *  `fail` spricht dagegen, `none` = nichts zu prüfen, `info` = zur Kenntnis. */
export interface AccountCheckItem { key: string; label: string; status: 'ok' | 'weak' | 'warn' | 'fail' | 'none' | 'info'; text: string }
export interface AccountChecks {
  site: string; user: string; url: string; player: string; elo: number | null; checkedAt: string;
  /** Konnte das Profil geholt werden? Ohne stehen die Profil-Prüfungen auf „nicht geprüft". */
  profileLoaded: boolean;
  items: AccountCheckItem[];
}

export interface SuggestionList {
  items: AccountSuggestion[];
  scanned?: number; total?: number;
  /** Nur nach „jetzt suchen": neue Vorschläge bzw. warum nicht gesucht wurde („minderjährig", „Jahrgang unbekannt"). */
  found?: number; skipped?: string | null;
}

/** Eine Lichess-Übertragung, deren Partien in die Spielerkarten kommen (0.608.0). */
export interface Broadcast {
  tourId: string; name: string; location: string | null; url: string;
  startsAt: string | null; endsAt: string | null;
  /** Per Link hinzugefügt (sonst über die Suche gefunden). */
  manual: boolean;
  importedAt: string | null;
  /** Alle Runden vorbei und eingespielt — wird nicht mehr geholt. */
  finished: boolean;
  /** Partien mit Ligaspielern beim letzten Einspielen. */
  games: number;
  error: string | null;
}

/** Eingabe für ein Konto: Name oder kopierte Profiladresse. Fehlende Felder bleiben beim Ändern, wie sie sind. */
export interface AccountInput { site?: string | null; user?: string | null; sure?: boolean | null; comment?: string | null }

/** Welche Partien der Eröffnungsbaum zählt (0.605.0): Brett, Brett + online, nur online. */
export type TreeSource = 'board' | 'both' | 'online';
/** Filter des Eröffnungsbaums (0.605.0): Quelle, Tempo der Online-Partien (leer = alle), nur die letzten x Jahre,
 * Online-Partien unsicherer Konten nur auf Wunsch (`withUnsure`, 0.612.0). */
export interface TreeFilter { source: TreeSource; speeds: string[]; years: number | null; withUnsure: boolean }

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

/** Eröffnungsprofil über gefilterte Partien (0.617.0, `GET …/player/{fide}/profile`). */
export interface ProfileView {
  fide: string;
  name?: string;
  n: number;
  board: number;
  online: number;
  with_moves?: number;
  years?: [string, string] | null;
  white?: OpeningStats;
  black_e4?: OpeningStats;
  black_d4?: OpeningStats;
  black_other?: OpeningStats;
}

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
  /** Geholte Online-Partien der gezeigten Konten — der Eröffnungsbaum kann sie einbeziehen. */
  online?: number;
  /** Davon aus unsicheren Konten — im Baum nur mit dem Schalter „auch unsichere Konten" (0.612.0). */
  onlineUnsure?: number;
}

/** `GET …/player/{fide}/recent` — die letzten Partien der Karte samt PGN (zum Nachspielen). */
/** Eine der letzten Partien samt PGN; die übrigen Angaben wie auf der Karte (seit 0.592.0 mitgeliefert). */
export interface RecentGame {
  date: string; vs: string; color: 'w' | 's'; pgn: string;
  event?: string; vs_elo?: string; score?: number | null; opening?: string;
}
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
  /** Davon Brett- bzw. Online-Partien (0.605.0). */
  board?: number;
  online?: number;
  /** Nächste Züge: Anzahl, Score aus SEINER Sicht (%, null = kein Ergebnis), jüngstes Jahr. */
  moves: { san: string; n: number; score: number | null; last: string | null }[];
}
