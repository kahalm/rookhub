/** Warum ein Konto keinen Verlauf hat — ein Grund ist besser als eine leere Tabelle. */
export type HistoryStatus = 'ok' | 'noName' | 'sourceUnavailable';

/** Bedenkzeit-Klasse eines Turniers, abgeleitet aus der Bedenkzeit auf chess-results. */
export type HistorySpeed = 'standard' | 'rapid' | 'blitz' | 'unknown';

/**
 * Die Klassen in der Reihenfolge, in der sie ueberall stehen: von lang nach kurz — und `unknown`
 * am Ende.
 *
 * <p><b>`unknown` gehoert dazu.</b> Die Bedenkzeit steht auf einer eigenen Seite, die erst der
 * naechtliche Durchgang holt; und manche Turniere nennen gar keine. Faellt die Klasse aus der
 * Auswertung, verschwindet mit ihr die PERFORMANCE — also genau die Zahl, um die es hier geht.
 * Direkt nach dem Deploy war jedes Turnier `unknown` und die Uebersicht damit leer.</p>
 */
export const HISTORY_SPEEDS: HistorySpeed[] = ['standard', 'rapid', 'blitz', 'unknown'];

/**
 * Was in einer Klasse zusammenkommt: Turniere, PARTIEN und die mittlere Performance.
 *
 * <p>Turniere und Partien sind zwei verschiedene Groessen: fuenf Wochenend-Opens sind fuenf
 * Turniere und rund 25 Partien, eine Ligasaison ein Turnier und drei Partien.</p>
 */
export interface SpeedSummary {
  speed: HistorySpeed;
  played: number;
  /** Summe der gespielten Partien; `null`, solange keine einzige Karte sie kennt. */
  games: number | null;
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
   * Tatsaechlich gespielte Partien — nicht die Rundenzahl des Turniers: in einer Liga wird ein
   * Spieler an einem TEIL der Termine aufgestellt. `null`, solange die Karte sie nicht kennt.
   */
  gamesPlayed: number | null;
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
