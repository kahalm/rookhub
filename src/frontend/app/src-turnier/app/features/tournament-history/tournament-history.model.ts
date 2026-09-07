/** Warum ein Konto keinen Verlauf hat — ein Grund ist besser als eine leere Tabelle. */
export type HistoryStatus = 'ok' | 'noName' | 'sourceUnavailable';

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
}
