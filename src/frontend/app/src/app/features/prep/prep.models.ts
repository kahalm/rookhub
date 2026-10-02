import { Account, AccountSuggestion, PlayerCard } from '@lh/core/league.models';

/** Ein Treffer der Spielersuche (`GET /api/prep/players`). */
export interface PrepHit {
  id: number;
  name: string;
  fide: string | null;
  games: number;
  firstYear: number | null;
  lastYear: number | null;
  maxElo: number | null;
}

/** Namens-Zwilling: dieselbe Schreibweise ohne FIDE-ID (nur angeboten, wenn genau EIN FIDE-Spieler so heißt). */
export interface PrepTwin { id: number; name: string; games: number }

/** Was die Karte geladen hat — die Felder, die die Spielervorbereitung zusätzlich zur Liga-Karte liefert. */
export interface PrepScope {
  id: number;
  /** Partien im Bestand (mit Zwilling: beide zusammen). */
  games: number;
  /** Davon geladen (die jüngsten). */
  loaded: number;
  /** Es gibt ältere, die nicht geladen sind. */
  limited: boolean;
  /** Grenze dieser Anfrage: Vorgabe 500, mit `all` höchstens {@link max}. */
  limit: number;
  /** So viele lädt „alle laden" höchstens (Einstellung des Servers, Vorgabe 3 000). */
  max: number;
  /** Datum der ältesten geladenen Partie („2015.10.11"), nur bei `limited`. */
  since: string | null;
  twin: PrepTwin | null;
  twinIncluded: boolean;
  /** Knopf „Online-Konten suchen" anbieten: Verwalter (`prep.manage`), Schalter `Prep:AccountSearch` an, FIDE-ID da (Phase 4). */
  accountSearch?: boolean;
}

/** Offene Vorschläge der Konto-Suche für einen Spieler — ohne die eines Minderjährigen (`GET …/suggestions`). */
export interface PrepSuggestionList {
  items: AccountSuggestion[];
  perHour: number;
  /** Suchen, die der Verwalter in dieser Stunde noch hat. */
  remaining: number;
  /** Nur nach einer Suche: neue Vorschläge. */
  found?: number;
  /** Seine eingetragenen Konten (0.639.0) — die eines Minderjährigen nie. */
  accounts?: Account[];
  /** Er steht auch in LeagueHub: seine Konten pflegt man dort, hier nur ansehen. */
  leagueHub?: boolean;
}

/** Die Karte, wie die API sie schickt: Form der Liga-Karte, `fide` darf fehlen. */
export type PrepCardJson = Omit<PlayerCard, 'fide'> & PrepScope & { fide: string | null };

/** Was die Seite an der Karte umschalten kann. */
export interface PrepOptions { all: boolean; twin: boolean }
