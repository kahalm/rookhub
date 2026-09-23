/**
 * Partie-Rückblick aus RookHubs EIGENER Analyse: Gewinnchance je Stellung (Kurve), Genauigkeit je
 * Seite und Zug-Klassen — reine Funktionen, ohne Angular, einzeln mit Zahlen testbar.
 *
 * Quellen der Formeln (bewusst übernommen statt erfunden, damit die Zahlen mit dem vergleichbar sind,
 * was Spieler von dort kennen):
 * - Gewinnchance und Genauigkeit: Lichess, https://lichess.org/page/accuracy (Konstanten aus lila
 *   `WinPercent.scala`/`AccuracyPercent.scala`).
 * - Klassen-Bänder: chess.com-Hilfe „How are moves classified?" — Verlust in Erwartungspunkten
 *   (0,00 / 0,02 / 0,05 / 0,10 / 0,20). Brilliant/Great/Miss/Book kommen in einem späteren Schritt.
 *
 * ALLE Bewertungen, die hereinkommen, stehen aus WEISS-Sicht (der Server dreht sie, siehe
 * `GameEvals.PlyOf`). Umgerechnet auf den Ziehenden wird erst hier, und zwar über die Seite am Zug
 * aus der FEN — nicht über die Parität des Halbzugs: eine Partie aus einer Stellung mit Schwarz am
 * Zug fängt mit einem schwarzen Zug an.
 */

/** Eine Bewertung, genau eines von beiden gesetzt. */
export interface EvalScore {
  cp?: number | null;
  mate?: number | null;
}

/** Eine gerechnete Stellung (die VOR dem Halbzug `ply`), Weiß-Sicht — `GameEvalPlyDto`. */
export interface GameEvalPly extends EvalScore {
  ply: number;
  depth: number;
  bestUci?: string | null;
  playedUci: string;
  playedCp?: number | null;
  playedMate?: number | null;
  /** Zweitbester Kandidat — heute ungenutzt (Grundlage für „Great" in einem späteren Schritt). */
  secondCp?: number | null;
  secondMate?: number | null;
}

export type GameEvalsStatus = 'none' | 'pending' | 'running' | 'done' | 'failed';

/** Antwort von `GET /api/games/{id}/evals` bzw. `/api/games/shared/{token}/evals`. */
export interface GameEvals {
  status: GameEvalsStatus;
  analyzed: number;
  total: number;
  targetDepth: number;
  analysisId?: number | null;
  /** Nur gerechnete Stellungen, nach Halbzug sortiert — Lücken bleiben Lücken. */
  plies: GameEvalPly[];
  /** Bewertung nach dem letzten Zug (für die Endstellung gibt es keine eigene Zeile). */
  final?: EvalScore | null;
}

export type MoveClass = 'best' | 'excellent' | 'good' | 'inaccuracy' | 'mistake' | 'blunder';

/** Reihenfolge der Anzeige (Zähler, Legende). */
export const MOVE_CLASSES: readonly MoveClass[] = ['best', 'excellent', 'good', 'inaccuracy', 'mistake', 'blunder'];

/** Lichess-Konstante der Gewinnchance (lila `WinPercent`, PR #11148). */
const WIN_MULTIPLIER = -0.00368208;

/**
 * Obergrenzen des Verlusts je Klasse in PROZENTPUNKTEN der Gewinnchance (= Erwartungspunkte × 100).
 * In Prozentpunkten und nicht als 0,02 usw., weil `0.6 - 0.58` in Gleitkomma 0,020000000000000018
 * ergibt — eine Grenze in Erwartungspunkten verschöbe Züge, die genau auf ihr liegen, in die
 * schlechtere Klasse.
 */
export const CLASS_LIMITS = { excellent: 2, good: 5, inaccuracy: 10, mistake: 20 } as const;

/** Wer ist in dieser FEN am Zug? Ohne lesbares zweites Feld: Weiß (wie der Server). */
export function whiteToMove(fen: string | null | undefined): boolean {
  return (fen ?? '').split(' ')[1] !== 'b';
}

/**
 * Gewinnchance für WEISS in Prozent (0..100). `null`, wenn keine Bewertung da ist.
 *
 * Matt ist ein Rand, keine große Zahl: `mate > 0` (Weiß setzt matt) = 100, `< 0` = 0. `mate == 0`
 * heißt, die Seite am Zug IST matt — dafür braucht es `whiteToMoveHere`. Lichess bildet Matt auf
 * ±1000 Centipawns ab (≈ 97,5 %); hier bewusst 100/0, damit ein gespieltes Matt in der Kurve bis an
 * den Rand geht.
 */
export function winPercent(score: EvalScore | null | undefined, whiteToMoveHere = true): number | null {
  if (!score) return null;
  if (score.mate != null) {
    if (score.mate > 0) return 100;
    if (score.mate < 0) return 0;
    return whiteToMoveHere ? 0 : 100;
  }
  if (score.cp == null) return null;
  return 50 + 50 * (2 / (1 + Math.exp(WIN_MULTIPLIER * score.cp)) - 1);
}

/**
 * Genauigkeit EINES Zuges aus Sicht des Ziehenden (Gewinnchance vorher/nachher, beide aus SEINER
 * Sicht). Kein Verlust = 100 — die Formel selbst liefert dort 99,9999, und ein fehlerfreier Zug soll
 * nicht knapp unter voll stehen.
 */
export function moveAccuracy(winBefore: number, winAfter: number): number {
  if (winAfter >= winBefore) return 100;
  const raw = 103.1668 * Math.exp(-0.04354 * (winBefore - winAfter)) - 3.1669;
  return Math.min(100, Math.max(0, raw));
}

/** Zug-Klasse aus Sicht des Ziehenden. Der Engine-Bestzug ist immer „best", auch wenn die tiefere
 *  Rechnung der nächsten Stellung danach etwas verliert — er WAR der beste Zug, den sie kannte. */
export function classify(winBefore: number, winAfter: number, playedIsBest: boolean): MoveClass {
  const loss = winBefore - winAfter;
  if (playedIsBest || loss <= 0) return 'best';
  if (loss <= CLASS_LIMITS.excellent) return 'excellent';
  if (loss <= CLASS_LIMITS.good) return 'good';
  if (loss <= CLASS_LIMITS.inaccuracy) return 'inaccuracy';
  if (loss <= CLASS_LIMITS.mistake) return 'mistake';
  return 'blunder';
}

/** Fensterbreite der Volatilität nach Lichess: Halbzüge / 10, ganzzahlig, auf 2..8 begrenzt. */
export function windowSizeFor(plies: number): number {
  return Math.min(8, Math.max(2, Math.floor(plies / 10)));
}

/** Standardabweichung der Grundgesamtheit (wie lila `Maths.standardDeviation`), Lücken ausgelassen. */
function stdDev(values: (number | null)[]): number {
  const known = values.filter((v): v is number => v != null);
  if (known.length === 0) return 0;
  const mean = known.reduce((s, v) => s + v, 0) / known.length;
  return Math.sqrt(known.reduce((s, v) => s + (v - mean) ** 2, 0) / known.length);
}

/**
 * Gewicht je Zug = Volatilität der Gewinnchance um ihn herum, auf 0,5..12 begrenzt (lila
 * `AccuracyPercent.gameAccuracy`). Ein Fehler in einer ruhigen Stellung zählt damit weniger als einer
 * mitten im Gefecht. `series` hat n+1 Einträge (Stellung 0 = Start … n = nach dem letzten Zug),
 * zurück kommen n Gewichte.
 *
 * Die Fenster wie bei Lichess: die ersten (Breite − 2) Züge bekommen das ERSTE Fenster wiederholt,
 * danach gleitet es. Eine Lücke fällt aus ihrem Fenster heraus, statt als 0 % mitzurechnen.
 */
export function volatilityWeights(series: (number | null)[]): number[] {
  const n = series.length - 1;
  if (n <= 0) return [];
  const size = windowSizeFor(n);
  const windows: (number | null)[][] = [];
  const first = series.slice(0, size);
  for (let i = 0; i < Math.min(size, series.length) - 2; i++) windows.push(first);
  for (let k = 0; k + size <= series.length; k++) windows.push(series.slice(k, k + size));
  return windows.slice(0, n).map(w => Math.min(12, Math.max(0.5, stdDev(w))));
}

/**
 * Genauigkeit EINER Seite: Mittel aus volatilitäts-gewichtetem und harmonischem Mittel der
 * Zug-Genauigkeiten (lila). Das harmonische Mittel lässt einen einzelnen groben Fehler durchschlagen,
 * den das gewichtete Mittel über viele gute Züge verwässern würde. Ohne Zug: `null` — „keine Angabe"
 * ist etwas anderes als 0 %.
 */
export function sideAccuracy(entries: { accuracy: number; weight: number }[]): number | null {
  if (entries.length === 0) return null;
  const weightSum = entries.reduce((s, e) => s + e.weight, 0);
  const weighted = entries.reduce((s, e) => s + e.accuracy * e.weight, 0) / weightSum;
  const harmonic = entries.length / entries.reduce((s, e) => s + 1 / Math.max(1, e.accuracy), 0);
  return (weighted + harmonic) / 2;
}

/** Ein bewerteter Zug. `winBefore`/`winAfter` aus Sicht des ZIEHENDEN, `evalAfter` aus Weiß-Sicht. */
export interface ReviewedMove {
  ply: number;
  white: boolean;
  cls: MoveClass;
  accuracy: number;
  winBefore: number;
  winAfter: number;
  evalBefore: EvalScore;
  evalAfter: EvalScore;
}

export interface SideSummary {
  accuracy: number | null;
  counts: Record<MoveClass, number>;
}

export interface GameReview {
  /** Gewinnchance WEISS je Stellung: [0] = Start, [i+1] = nach Zug i; `null` = nicht gerechnet. */
  series: (number | null)[];
  /** Je Halbzug der Rückblick; `null`, wenn vorher oder nachher eine Bewertung fehlt. */
  moves: (ReviewedMove | null)[];
  white: SideSummary;
  black: SideSummary;
}

function emptyCounts(): Record<MoveClass, number> {
  return { best: 0, excellent: 0, good: 0, inaccuracy: 0, mistake: 0, blunder: 0 };
}

function playedScore(row: GameEvalPly): EvalScore | null {
  return scoreOf({ cp: row.playedCp, mate: row.playedMate });
}

function scoreOf(s: EvalScore | null | undefined): EvalScore | null {
  return s && (s.cp != null || s.mate != null) ? { cp: s.cp ?? null, mate: s.mate ?? null } : null;
}

/**
 * Der ganze Rückblick. `fens` wie im PGN-Viewer: `fens[0]` = Startstellung, `fens[i+1]` = nach Zug i
 * — die Zahl der Züge kommt von DORT (das ist die Partie, die der Nutzer sieht), Zeilen darüber
 * hinaus werden ignoriert.
 *
 * Bewertbar ist ein Zug nur, wenn die Stellung davor gerechnet ist (sonst weiß niemand, was der
 * beste Zug war) UND es eine Bewertung danach gibt. „Danach" ist bevorzugt die NÄCHSTE Stellung —
 * sie ist selbst gerechnet und damit tiefer als der Kandidat derselben Suche; fehlt sie, trägt die
 * Bewertung des gespielten Kandidaten.
 */
export function reviewGame(evals: GameEvals | null | undefined, fens: string[]): GameReview {
  const n = Math.max(0, fens.length - 1);
  const rows = new Map<number, GameEvalPly>();
  for (const p of evals?.plies ?? []) if (p.ply >= 0 && p.ply < n) rows.set(p.ply, p);

  const evalAt: (EvalScore | null)[] = [];
  for (let j = 0; j < n; j++) evalAt.push(scoreOf(rows.get(j)));
  evalAt.push(scoreOf(evals?.final));
  const series = evalAt.map((s, j) => winPercent(s, whiteToMove(fens[j])));

  const moves: (ReviewedMove | null)[] = [];
  for (let i = 0; i < n; i++) {
    const row = rows.get(i);
    const before = row ? evalAt[i] : null;
    const after = evalAt[i + 1] ?? (row ? playedScore(row) : null);
    const wb = series[i];
    const wa = after ? winPercent(after, whiteToMove(fens[i + 1])) : null;
    if (!row || !before || wb == null || !after || wa == null) { moves.push(null); continue; }

    const white = whiteToMove(fens[i]);
    const mb = white ? wb : 100 - wb;
    const ma = white ? wa : 100 - wa;
    const best = !!row.bestUci && row.bestUci.toLowerCase() === (row.playedUci ?? '').toLowerCase();
    moves.push({
      ply: i, white, cls: classify(mb, ma, best), accuracy: moveAccuracy(mb, ma),
      winBefore: mb, winAfter: ma, evalBefore: before, evalAfter: after,
    });
  }

  const weights = volatilityWeights(series);
  const summary = (white: boolean): SideSummary => {
    const counts = emptyCounts();
    const entries: { accuracy: number; weight: number }[] = [];
    moves.forEach((m, i) => {
      if (!m || m.white !== white) return;
      counts[m.cls]++;
      entries.push({ accuracy: m.accuracy, weight: weights[i] ?? 0.5 });
    });
    return { accuracy: sideAccuracy(entries), counts };
  };

  return { series, moves, white: summary(true), black: summary(false) };
}

/** Anzeige einer Bewertung aus Weiß-Sicht — dieselbe Form wie auf dem Analysebrett („+0.34", „#3"). */
export function formatEval(score: EvalScore | null | undefined): string {
  if (!score) return '';
  if (score.mate != null) return '#' + score.mate;
  if (score.cp == null) return '';
  const v = score.cp / 100;
  return (v > 0 ? '+' : '') + v.toFixed(2);
}
