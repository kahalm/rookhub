/** Warum ein Konto keinen Verlauf hat — ein Grund ist besser als eine leere Tabelle. */
export type HistoryStatus = 'ok' | 'noName' | 'sourceUnavailable';

/** Bedenkzeit-Klasse eines Turniers, abgeleitet aus der Bedenkzeit auf chess-results. */
export type HistorySpeed = 'standard' | 'rapid' | 'blitz' | 'unknown';

/** Die drei Klassen in der Reihenfolge, in der sie ueberall stehen: von lang nach kurz. */
export const HISTORY_SPEEDS: HistorySpeed[] = ['standard', 'rapid', 'blitz'];

/** Was in einer Klasse zusammenkommt: wie viele Turniere und welche mittlere Performance. */
export interface SpeedSummary {
  speed: HistorySpeed;
  played: number;
  performance: number | null;
}

export interface PlayerHistoryEntry {
  chessResultsId: string;
  name: string;
  endDate: string | null;
  rank: number | null;
  playerCount: number | null;
  rounds: number | null;
  points: number | null;
  /** Turnier-Leistung — die Zahl, um die es hier eigentlich geht. */
  performanceRating: number | null;
  ratingChange: number | null;
  /** Wertung zu Turnierbeginn — der Bezugspunkt der Performance. */
  ratingBefore: number | null;
  /** `false` heisst „noch nicht gespielt" ODER „wird gerade geholt" (das Datum unterscheidet). */
  hasResult: boolean;
  /**
   * Ist die Spielerkarte schon abgerufen? Trennt „wird noch geholt" von „chess-results fuehrt
   * hier kein Einzelergebnis" — sonst wartet man auf eine Zahl, die nie kommt.
   */
  cardFetched: boolean;
  /**
   * Bedenkzeit-Klasse. Eine Performance im Blitz und eine im Turnierschach sind zwei
   * verschiedene Zahlen, auch wenn beide „Performance" heissen — deshalb steht sie an jeder
   * Zeile und trennt die Auswertung. `unknown`, solange die Turnierseite dafuer fehlt.
   */
  speed: HistorySpeed;
}

export interface PlayerHistory {
  userId: number;
  displayName: string;
  status: HistoryStatus;
  entries: PlayerHistoryEntry[];
  /**
   * Wie viele Ergebnisse noch geholt werden. Jedes kostet einen Seitenabruf und laeuft im
   * Hintergrund — solange die Zahl groesser als null ist, lohnt ein zweiter Blick.
   */
  pending: number;
}

/** Ein Freund, dessen Verlauf sich ansehen laesst. */
export interface HistoryFriend {
  userId: number;
  displayName: string;
  /** Traegt das Profil eine Kennung? Wenn nicht, sind Namensgleiche mit dabei. */
  exact: boolean;
  /**
   * Steht ein Nachname im Profil? Ohne ihn gibt es keinen Verlauf — der Freund steht trotzdem in
   * der Liste, nur nicht auswaehlbar: eine leere Auswahl nennt keinen Grund, ein ausgegrauter
   * Eintrag schon.
   */
  hasName: boolean;
}
