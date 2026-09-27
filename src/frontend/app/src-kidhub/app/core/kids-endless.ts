/**
 * Die Kurve des Endlos-Modus der Kinderseite — wie RookHubs Endlos-Modus (`endless-prefetch.util.ts`:
 * Startwert → T1 nach 10 Puzzles → T2 nach 25 → gleichmaessig weiter), nur viel flacher und ohne die steile
 * Erst-Lauf-Kurve. Rein, ohne Angular: die Regeln sind so einzeln testbar.
 *
 * Grundkurve (Wunsch 2026-09-27): Start 700, dann 5 Puzzles je 100 Elo = +20 je Puzzle. ADAPTIV wie in
 * RookHub: T1 = Ø Rating des ERSTEN Fehlers der letzten Laeufe, T2 = Ø Hoechst-Rating der letzten fuenf —
 * wer weit kommt, bekommt spaeter steilere Laeufe. Flacher als die Grundkurve wird es nie.
 */

export const ENDLESS_START = 700;
/** +20 Elo je Puzzle = 5 Puzzles je 100 Elo. */
export const ENDLESS_STEP = 20;
export const ENDLESS_T1_INDEX = 10;
export const ENDLESS_T2_INDEX = 25;
/** Breite des Rating-Fensters je Puzzle (± die Haelfte). */
export const ENDLESS_WINDOW = 40;
/** Puzzles je Nachladen. */
export const ENDLESS_BLOCK = 20;
/** Nachladen, wenn nur noch so viele in der Schlange sind. */
export const ENDLESS_REFILL_AT = 5;
export const ENDLESS_LIVES = 3;
/** So viele Tipps je Aufgabe kosten kein Herz — erst der naechste (der zweite zeigt den ganzen Zug; Wunsch 2026-09-27). */
export const ENDLESS_FREE_HINTS = 1;
/** Laeufe, aus denen T1 (erster Fehler) gemittelt wird. */
export const ENDLESS_T1_RUNS = 10;
/** Laeufe, aus denen T2 (Hoechst-Rating) gemittelt wird. */
export const ENDLESS_T2_RUNS = 5;

/** Ein beendeter Lauf, so weit die Kurve ihn braucht. */
export interface EndlessRun {
  /** Wann (ms). */
  at: number;
  /** Geschaffte Aufgaben. */
  solved: number;
  /** Hoechstes Rating einer OHNE Fehler geloesten Aufgabe (0 = keine). */
  maxRating: number;
  /** Rating der Aufgabe mit dem ersten Fehler (`null` = keiner — kommt nur vor, wenn man vorher aufhoert). */
  firstMistakeRating: number | null;
}

export interface EndlessThresholds { t1: number; t2: number; }

const avg = (xs: number[]) => Math.round(xs.reduce((s, x) => s + x, 0) / xs.length);

/** T1/T2 aus der Historie, nie unter der Grundkurve (700 → 900 → 1200). */
export function endlessThresholds(history: EndlessRun[], start = ENDLESS_START): EndlessThresholds {
  const baseT1 = start + ENDLESS_T1_INDEX * ENDLESS_STEP;
  const firsts = history.filter(r => r.firstMistakeRating != null).slice(-ENDLESS_T1_RUNS).map(r => r.firstMistakeRating!);
  const t1 = Math.max(baseT1, firsts.length ? avg(firsts) : baseT1);
  const baseT2 = t1 + (ENDLESS_T2_INDEX - ENDLESS_T1_INDEX) * ENDLESS_STEP;
  const maxes = history.filter(r => r.maxRating > 0).slice(-ENDLESS_T2_RUNS).map(r => r.maxRating);
  return { t1, t2: Math.max(baseT2, maxes.length ? avg(maxes) : baseT2) };
}

/** Rating des n-ten Puzzles (0-basiert): gerade Stuecke zwischen den Ankern, danach +{@link ENDLESS_STEP}. */
export function endlessRatingAt(n: number, t: EndlessThresholds, start = ENDLESS_START): number {
  if (n <= 0) return start;
  if (n < ENDLESS_T1_INDEX) return Math.round(start + (t.t1 - start) * (n / ENDLESS_T1_INDEX));
  if (n < ENDLESS_T2_INDEX)
    return Math.round(t.t1 + (t.t2 - t.t1) * ((n - ENDLESS_T1_INDEX) / (ENDLESS_T2_INDEX - ENDLESS_T1_INDEX)));
  return Math.round(t.t2 + (n - ENDLESS_T2_INDEX) * ENDLESS_STEP);
}

/** Rating-Fenster fuer die Puzzles [from, from+count). */
export function endlessWindows(from: number, count: number, t: EndlessThresholds, start = ENDLESS_START)
  : { minRating: number; maxRating: number }[] {
  const half = ENDLESS_WINDOW / 2;
  return Array.from({ length: count }, (_, i) => {
    const r = endlessRatingAt(from + i, t, start);
    return { minRating: Math.max(0, r - half), maxRating: r + half };
  });
}
