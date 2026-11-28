/**
 * Theoretischer Elo-Gewinn einer Partie — international (FIDE) und national (Oesterreich, OeSB).
 *
 * <h3>Gemeinsam</h3>
 * Beide Wertungen rechnen je Partie `ΔR = K · (W − We)`: W = Ergebnis (1 / ½ / 0), We = Erwartung
 * aus der Wertungsdifferenz D = eigene − gegnerische Wertung, abgelesen aus DERSELBEN Tabelle
 * (FIDE Rating Regulations 8.1.2 bzw. TUWO Anhang 1, 5.3: 0–3 → .50, 4–10 → .51 … 392–411 → .92,
 * 412–432 → .93, 433–450 → .94). Gerundet wird erst die Summe einer Periode bzw. eines Turniers
 * (kaufmaennisch); der Rechner zeigt deshalb die ungerundete Partie mit einer Nachkommastelle,
 * wie chess-results auf der Spielerkarte.
 *
 * <h3>FIDE</h3>
 * Quelle: FIDE Rating Regulations (B.02, gueltig ab 1. 1. 2024), 8.2/8.3 —
 * https://handbook.fide.com/chapter/B022024
 * - Differenzen ueber 400 zaehlen als 400.
 * - K = 40 fuer neue Spieler bis 30 Partien und fuer Spieler bis zum 18. Geburtstag, solange ihre
 *   Wertung unter 2300 bleibt; K = 20, solange die Wertung unter 2400 bleibt; K = 10, sobald die
 *   veroeffentlichte Wertung einmal 2400 erreicht hat. Schnell- und Blitzschach: K = 20.
 *   Ableitbar ist hier nur Alter + aktuelle Wertung; „bis 30 Partien" und „war schon einmal 2400"
 *   kennt der Verlauf nicht — der K-Faktor bleibt deshalb waehlbar.
 *
 * <h3>OeSB (nationale Elo, OeEL)</h3>
 * Quelle: OeSB-Turnier- und Wettkampfordnung (TUWO, Stand 04/2021), § 22 und Anhang 1
 * „Durchfuehrungsbestimmungen fuer die Oesterreichische Elowertung", Punkte 4 und 5 —
 * https://www.chess.at/downloads/oesb_tuwo.pdf
 * - Faktor K = (3400 − eigene Elo)² / 100000, hoechstens 50; ab 2400 konstant 10 (5.2).
 * - Differenzen ueber 450 zaehlen als 450 (5.3) — anders als bei der FIDE.
 * - Untergrenze 800 (4) — der Rechner zeigt die Aenderung, nicht den Stand; die Untergrenze greift
 *   erst bei der Liste.
 * - Druckfehler in der TUWO-Tabelle: dort steht „201–215 → .77"; gemeint ist 207–215 (198–206 ist
 *   schon .76, die FIDE-Tabelle sagt 207–215).
 */

export type GameResult = 'win' | 'draw' | 'loss';

export const GAME_RESULTS: GameResult[] = ['win', 'draw', 'loss'];

const SCORE: Record<GameResult, number> = { win: 1, draw: 0.5, loss: 0 };

/**
 * Obergrenzen der Differenz-Stufen; Stufe i ergibt fuer den Hoeheren .50 + i/100. Gilt fuer FIDE
 * und OeSB gleich — nur die Deckelung unterscheidet sich (400 bzw. 450).
 */
const DIFF_STEPS = [
  3, 10, 17, 25, 32, 39, 46, 53, 61, 68, 76, 83, 91, 98, 106, 113, 121, 129, 137, 145, 153, 162,
  170, 179, 188, 197, 206, 215, 225, 235, 245, 256, 267, 278, 290, 302, 315, 328, 344, 357, 374,
  391, 411, 432, 450,
];

/** FIDE: Differenzen ueber 400 zaehlen als 400. */
export const FIDE_DIFF_CAP = 400;
/** OeSB: Differenzen ueber 450 zaehlen als 450. */
export const OESB_DIFF_CAP = 450;

/**
 * Erwartete Punktzahl des Spielers mit `own` gegen `opponent` aus der Tabelle, nach Deckelung der
 * Differenz auf `cap`.
 */
export function expectedScore(own: number, opponent: number, cap: number): number {
  const diff = Math.min(Math.abs(Math.round(own - opponent)), cap);
  let step = DIFF_STEPS.findIndex(upper => diff <= upper);
  if (step < 0) step = DIFF_STEPS.length - 1;
  const higher = Math.round((50 + step)) / 100;
  return own >= opponent ? higher : Math.round((1 - higher) * 100) / 100;
}

/** Aenderung EINER Partie: K · (W − We), ungerundet. */
export function ratingChange(
  own: number, opponent: number, k: number, result: GameResult, cap: number,
): number {
  return k * (SCORE[result] - expectedScore(own, opponent, cap));
}

/** FIDE-K-Faktoren, die zur Wahl stehen. */
export const FIDE_K_FACTORS = [40, 20, 10] as const;

/**
 * FIDE-K-Faktor aus dem, was der Verlauf kennt: Wertung und (falls bekannt) Geburtsjahr.
 * „Unter 18" heisst: der 18. Geburtstag liegt noch nicht im laufenden Jahr hinter einem —
 * mit nur dem Jahr bekannt zaehlt, wer in diesem Jahr hoechstens 17 wird.
 */
export function fideKFactor(rating: number, birthYear?: number | null, year = new Date().getFullYear()): number {
  if (rating >= 2400) return 10;
  if (birthYear != null && year - birthYear < 18 && rating < 2300) return 40;
  return 20;
}

/** OeSB-Faktor: (3400 − R)² / 100000, hoechstens 50; ab 2400 konstant 10. Ungerundet. */
export function oesbKFactor(rating: number): number {
  if (rating >= 2400) return 10;
  return Math.min(50, (3400 - rating) ** 2 / 100000);
}

/** FIDE-Aenderung (Deckel 400). */
export function fideChange(own: number, opponent: number, k: number, result: GameResult): number {
  return ratingChange(own, opponent, k, result, FIDE_DIFF_CAP);
}

/** OeSB-Aenderung (Deckel 450). */
export function oesbChange(own: number, opponent: number, k: number, result: GameResult): number {
  return ratingChange(own, opponent, k, result, OESB_DIFF_CAP);
}

/** Anzeige mit einer Nachkommastelle und Vorzeichen („+14,6", „−5,4", „0,0"). */
export function formatChange(value: number, locale: string): string {
  const rounded = Math.round(value * 10) / 10;
  const text = new Intl.NumberFormat(locale, { minimumFractionDigits: 1, maximumFractionDigits: 1 })
    .format(Math.abs(rounded));
  return rounded > 0 ? `+${text}` : rounded < 0 ? `−${text}` : text;
}
