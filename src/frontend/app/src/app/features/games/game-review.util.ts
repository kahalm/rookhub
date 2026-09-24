import { Sacrifice, inCheck, isPromotion, sacrificedPiece } from './move-tactics.util';

/**
 * Partie-Rückblick aus RookHubs EIGENER Analyse: Gewinnchance je Stellung (Kurve), Genauigkeit je
 * Seite und Zug-Klassen — reine Funktionen, ohne Angular, einzeln mit Zahlen testbar.
 *
 * Quellen der Formeln (bewusst übernommen statt erfunden, damit die Zahlen mit dem vergleichbar sind,
 * was Spieler von dort kennen):
 * - Gewinnchance und Genauigkeit: Lichess, https://lichess.org/page/accuracy (Konstanten aus lila
 *   `WinPercent.scala`/`AccuracyPercent.scala`).
 * - Klassen-Bänder: chess.com-Hilfe „How are moves classified?" — Verlust in Erwartungspunkten
 *   (0,00 / 0,02 / 0,05 / 0,10 / 0,20).
 * - Brilliant/Great/Miss: die BEDINGUNGEN aus derselben chess.com-Hilfe, die ZAHLEN für Brilliant/Great aus
 *   WintrCat/freechess (`src/lib/analysis.ts`, `board.ts`) — chess.com legt die Schwellen nicht offen, und
 *   Miss gibt es bei freechess nicht. Siehe `specialClass`.
 *   Book gibt es nicht: der Client hat kein Eröffnungsbuch.
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
  /** Zweitbester Kandidat derselben Suche — für „Great" (Abstand) und „Brilliant" (ohnehin gewonnen?). */
  secondCp?: number | null;
  secondMate?: number | null;
  /** Alle Kandidaten dieser Suche (bester zuerst, Weiß-Sicht) — für die gleichwertigen Züge in
   *  „Eigene Fehler nachspielen". Fehlt bei Antworten älterer Server. */
  candidates?: GameEvalCandidate[];
}

/** Ein Kandidat der Engine: Zug (Standard-UCI) + Bewertung in Weiß-Sicht — `GameEvalCandidateDto`. */
export interface GameEvalCandidate extends EvalScore {
  uci: string;
  /** Variante der Engine ab diesem Zug (UCI roh vom Broker, Rochade ggf. als König-schlägt-Turm); fehlt bei
   *  Analysen von vor 0.521.0. */
  pv?: string[] | null;
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
  /**
   * Hochgerechnete Restdauer in Minuten, solange die Analyse läuft — der Server rechnet sie aus dem
   * Tempo der jüngsten Stellungen DIESER Partie (`GameEvals.EtaMinutes`). Fehlt, solange es noch kein
   * Tempo gibt (weniger als zwei Ergebnisse).
   */
  etaMinutes?: number | null;
}

export type MoveClass =
  'brilliant' | 'great' | 'best' | 'excellent' | 'good' | 'inaccuracy' | 'mistake' | 'miss' | 'blunder';

/** Reihenfolge der Anzeige (Zähler, Legende) — Miss steht vor Blunder, weil es einen ersetzen kann. */
export const MOVE_CLASSES: readonly MoveClass[] =
  ['brilliant', 'great', 'best', 'excellent', 'good', 'inaccuracy', 'mistake', 'miss', 'blunder'];

/**
 * Farbe je Klasse, chess.com-nah — die EINE Tabelle für Zähler, Abzeichen und die Punkte in der Kurve.
 * Excellent ist ein Stück heller als Best: bei chess.com sind beide gleich, in der Zählertabelle stünden
 * dann zwei gleiche grüne Kästchen nebeneinander.
 */
export const MOVE_CLASS_COLORS: Readonly<Record<MoveClass, string>> = {
  brilliant: '#26c2a3', great: '#5b8fd6', best: '#96bc4b', excellent: '#a6c666', good: '#96af8b',
  inaccuracy: '#f7c631', mistake: '#e58f2a', miss: '#ee6b55', blunder: '#ca3431',
};

/** Grundklassen, aus denen ein Opfer brillant werden kann — Best, Excellent und (seit 0.521.3) Good. */
export const BRILLIANT_BASES: ReadonlySet<MoveClass> = new Set<MoveClass>(['best', 'excellent', 'good']);

/** Miss: die Gewinnchance, die der Bestzug gebracht hätte, lag mindestens hier … */
export const MISS_BEST_WIN = 70;
/** … und die nach dem gespielten Zug höchstens hier. Beide Zahlen sind gesetzt, nicht übernommen: chess.com
 *  nennt keine, und freechess kennt die Klasse Miss nicht. */
export const MISS_AFTER_WIN = 60;
/** … aber mindestens hier: wer von +5 auf −5 fällt, hat nicht „verpasst", sondern gepatzt — die Nachricht
 *  „Katastrophe" darf das Etikett nicht verdecken. */
export const MISS_MIN_AFTER_WIN = 40;
/** Brilliant: ab dieser Bewertung des Zweitbesten war die Stellung ohnehin gewonnen — kein Opfer nötig. */
export const BRILLIANT_WINNING_ANYWAY_PAWNS = 7;
/** Brilliant: schlechter als das darf die Stellung nach dem Opfer nicht stehen. */
export const BRILLIANT_MIN_AFTER_PAWNS = -1;
/** Great: so weit muss der Bestzug vor dem zweitbesten Kandidaten liegen („der einzige gute Zug"). */
export const GREAT_MIN_GAP_PAWNS = 1.5;
/** Great: so viel Gewinnchance muss danach bleiben — der einzige Zug, der bloß langsamer verliert, ist kein starker. */
export const GREAT_MIN_AFTER_WIN = 45;

/** Matt in n als Vergleichszahl wie `GuessScoring.Pawns` am Server: ±(1000 − n) Bauern. */
const MATE_BASE_PAWNS = 1000;

/** Lichess-Konstante der Gewinnchance (lila `WinPercent`, PR #11148). */
const WIN_MULTIPLIER = -0.00368208;
/** Lichess kappt die Bewertung für die Gewinnchance bei ±1000 cp (seit 0.521.1 auch hier). */
const WIN_CP_CAP = 1000;
/**
 * lila `AccuracyPercent.fromWinPercents` rechnet auf jede Zug-Genauigkeit +1 („uncertainty bonus (due to
 * imperfect analysis)"). Fehlte bis 0.521.1 — gemeldet als „Genauigkeit fühlt sich extremst niedrig an"; an
 * der Partie MYXN3hXqz1X2hm7Cx6V47Q macht er 78,0 → 78,8 (Weiß) und 70,3 → 71,4 (Schwarz). Der Rest des
 * Abstands zu chess.com ist Methode: chess.com rechnet anders (CAPS, nicht offengelegt) und höher.
 */
export const ACCURACY_UNCERTAINTY_BONUS = 1;

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
  // Wie lila (`Centipawns.ceiled`): über ±1000 cp zählt nichts mehr — +15 und +30 sind gleich gewonnen.
  const cp = Math.max(-WIN_CP_CAP, Math.min(WIN_CP_CAP, score.cp));
  return 50 + 50 * (2 / (1 + Math.exp(WIN_MULTIPLIER * cp)) - 1);
}

/** Bis hierhin (in Bauern) steigt die Kurve linear, darüber steht sie am Rand. */
export const GRAPH_CAP_PAWNS = 10;

/**
 * Höhe der Bewertungskurve (0..100, 50 = ausgeglichen, 100 = Weiß am oberen Rand) — wie chess.com: die
 * BEWERTUNG linear, bei ±`GRAPH_CAP_PAWNS` gekappt, Matt am Rand. Nicht die Gewinnchance: die sättigt schon
 * bei +3 bei fast 80 % und machte aus jeder klar gewonnenen Phase einen Block am oberen Rand (gewünscht
 * 2026-09-24 mit einem chess.com-Schnappschuss derselben Partie daneben; dessen Punkte liegen genau auf dieser
 * Skala, wo beide Engines übereinstimmen: 0,00 auf der Mitte, +6,51 bei 82,5). Genauigkeit und Zug-Klassen
 * rechnen weiter mit `winPercent` — das ist nur die Zeichnung.
 */
export function graphHeight(score: EvalScore | null | undefined, whiteToMoveHere = true): number | null {
  if (!score) return null;
  if (score.mate != null) return winPercent(score, whiteToMoveHere);
  if (score.cp == null) return null;
  const pawns = Math.max(-GRAPH_CAP_PAWNS, Math.min(GRAPH_CAP_PAWNS, score.cp / 100));
  return 50 + 50 * pawns / GRAPH_CAP_PAWNS;
}

/**
 * Genauigkeit EINES Zuges aus Sicht des Ziehenden (Gewinnchance vorher/nachher, beide aus SEINER
 * Sicht). Kein Verlust = 100 — die Formel selbst liefert dort 99,9999, und ein fehlerfreier Zug soll
 * nicht knapp unter voll stehen.
 */
export function moveAccuracy(winBefore: number, winAfter: number): number {
  if (winAfter >= winBefore) return 100;
  const raw = 103.1668 * Math.exp(-0.04354 * (winBefore - winAfter)) - 3.1669 + ACCURACY_UNCERTAINTY_BONUS;
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
  /**
   * Die Klasse allein aus dem Bewertungsverlust — `cls` kann ein ETIKETT darüber sein (Miss, Brilliant,
   * Great). Wer nach „war das ein Fehler?" fragt, fragt hier: ein Miss IST ein Fehler, dem zusätzlich eine
   * Gelegenheit entgangen ist (so nutzt es der Fehler-Trainer, `mistakes.util.ts`).
   */
  base: MoveClass;
  accuracy: number;
  winBefore: number;
  winAfter: number;
  evalBefore: EvalScore;
  evalAfter: EvalScore;
  /** Nur bei Brilliant: die geopferte Figur — das Abzeichen nennt sie. */
  sacrifice?: Sacrifice;
  /** Abstand Bestzug − zweitbester Kandidat in Bauern, Sicht des Ziehenden; nur mit zweitem Kandidaten. */
  gapPawns?: number;
}

export interface SideSummary {
  accuracy: number | null;
  counts: Record<MoveClass, number>;
}

export interface GameReview {
  /** Gewinnchance WEISS je Stellung: [0] = Start, [i+1] = nach Zug i; `null` = nicht gerechnet. */
  series: (number | null)[];
  /** Höhe der Bewertungskurve je Stellung (0..100, 50 = ausgeglichen), wie `series` indiziert — siehe `graphHeight`. */
  curve: (number | null)[];
  /** Je Halbzug der Rückblick; `null`, wenn vorher oder nachher eine Bewertung fehlt. */
  moves: (ReviewedMove | null)[];
  white: SideSummary;
  black: SideSummary;
}

function emptyCounts(): Record<MoveClass, number> {
  return {
    brilliant: 0, great: 0, best: 0, excellent: 0, good: 0, inaccuracy: 0, mistake: 0, miss: 0, blunder: 0,
  };
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
 *
 * `ucis` (je Halbzug `von + nach + Umwandlung`) schaltet die Sonderklassen Brilliant/Great/Miss ein —
 * das Opfer steckt in der STELLUNG, und welche Figur wohin zog, sagt nur der Zug. Fehlt er (für die
 * ganze Partie oder einen Halbzug), bleibt es bei der Grundklasse, genau wie vor 0.514.0.
 */
export function reviewGame(evals: GameEvals | null | undefined, fens: string[], ucis?: readonly string[]): GameReview {
  const n = Math.max(0, fens.length - 1);
  const rows = new Map<number, GameEvalPly>();
  for (const p of evals?.plies ?? []) if (p.ply >= 0 && p.ply < n) rows.set(p.ply, p);

  const evalAt: (EvalScore | null)[] = [];
  for (let j = 0; j < n; j++) evalAt.push(scoreOf(rows.get(j)));
  evalAt.push(scoreOf(evals?.final));
  const series = evalAt.map((s, j) => winPercent(s, whiteToMove(fens[j])));
  const curve = evalAt.map((s, j) => graphHeight(s, whiteToMove(fens[j])));

  const moves: (ReviewedMove | null)[] = [];
  // Grundklasse je Halbzug: Miss und Great fragen, ob der GEGNER einen Fehler gemacht hat — das ist seine
  // Grundklasse, nicht sein Etikett (ein Miss war auch ein Fehler, den man bestrafen kann).
  const base: (MoveClass | null)[] = [];
  for (let i = 0; i < n; i++) {
    const row = rows.get(i);
    const before = row ? evalAt[i] : null;
    const after = evalAt[i + 1] ?? (row ? playedScore(row) : null);
    const wb = series[i];
    const wa = after ? winPercent(after, whiteToMove(fens[i + 1])) : null;
    if (!row || !before || wb == null || !after || wa == null) { moves.push(null); base.push(null); continue; }

    const white = whiteToMove(fens[i]);
    const mb = white ? wb : 100 - wb;
    const ma = white ? wa : 100 - wa;
    const best = !!row.bestUci && row.bestUci.toLowerCase() === (row.playedUci ?? '').toLowerCase();
    const cls = classify(mb, ma, best);
    base.push(cls);
    const move: ReviewedMove = {
      ply: i, white, cls, base: cls, accuracy: moveAccuracy(mb, ma),
      winBefore: mb, winAfter: ma, evalBefore: before, evalAfter: after,
    };
    const uci = ucis?.[i];
    if (uci) {
      Object.assign(move, specialClass({
        base: cls, prevBase: i > 0 ? base[i - 1] : null, white, winBefore: mb, winAfter: ma,
        row, before, after, fenBefore: fens[i], fenAfter: fens[i + 1], uci,
      }));
    }
    moves.push(move);
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

  return { series, curve, moves, white: summary(true), black: summary(false) };
}

interface SpecialInput {
  base: MoveClass;
  /** Grundklasse des vorigen Halbzugs (des Gegners); `null` = erster Zug oder nicht bewertbar. */
  prevBase: MoveClass | null;
  white: boolean;
  winBefore: number;
  winAfter: number;
  row: GameEvalPly;
  before: EvalScore;
  after: EvalScore;
  fenBefore: string;
  fenAfter: string;
  uci: string;
}

/**
 * Die drei Sonderklassen — ETIKETTEN über der Grundklasse; Genauigkeit und Gewinnchance bleiben, wie sie
 * sind. Reihenfolge und Schwellen (Konstanten oben):
 *
 * 1. **Miss** ersetzt Inaccuracy/Mistake/Blunder, wenn der Gegner davor einen Fehler oder groben Fehler
 *    gemacht hat, der Bestzug ≥ 70 % gebracht hätte und nach dem gespielten 40..60 % bleiben: nicht der
 *    Verlust ist die Nachricht, sondern die verpasste Strafe. Unter 40 % bleibt es der Fehler, der es ist.
 * 2. **Brilliant** ersetzt Best/Excellent (chess.com: „best or nearly best"), wenn dabei eine Figur
 *    GEOPFERT wird (`sacrificedPiece`), die Stellung nicht ohnehin gewonnen war (Zweitbester < +7 und kein
 *    Matt), sie danach nicht schlecht steht (≥ −1) und der Zug weder eine Umwandlung noch eine Antwort auf
 *    Schach ist (dort sind Opfer erzwungen, nicht gefunden).
 * 3. **Great** ersetzt Best, wenn er nicht schon brillant ist: der Gegner hat davor gepatzt, der Bestzug
 *    liegt ≥ 1,5 Bauern vor dem Zweitbesten (der EINZIGE Zug, der die Chance nutzt), der Zweitbeste gewinnt
 *    nicht ohnehin (dieselbe Grenze wie bei Brilliant — #2 gegen #4 ist kein „einziger guter Zug"), danach
 *    bleiben ≥ 45 %, und es hängt nichts — ein Zug mit Opfer ist Brilliants Sache.
 *
 * Fehlt eine Zutat (zweiter Kandidat, bewerteter Vorzug, lesbare Stellung), entfällt die Sonderklasse.
 */
function specialClass(m: SpecialInput): { cls: MoveClass; sacrifice?: Sacrifice; gapPawns?: number } {
  const opponentErred = m.prevBase === 'mistake' || m.prevBase === 'blunder';
  // Alles in CENTIPAWNS und ganzzahlig verglichen: „Abstand ≥ 1,5" als Differenz zweier Kommazahlen
  // landet in Gleitkomma knapp daneben (dieselbe Falle wie bei CLASS_LIMITS).
  const second = scoreOf({ cp: m.row.secondCp, mate: m.row.secondMate });
  const bestCp = moverCp(m.before, m.white, m.white);
  const secondCp = second ? moverCp(second, m.white, m.white) : null;
  const gapCp = bestCp != null && secondCp != null ? bestCp - secondCp : null;
  const out: { cls: MoveClass; sacrifice?: Sacrifice; gapPawns?: number } =
    gapCp != null ? { cls: m.base, gapPawns: gapCp / 100 } : { cls: m.base };

  if ((m.base === 'inaccuracy' || m.base === 'mistake' || m.base === 'blunder') && opponentErred
      && m.winBefore >= MISS_BEST_WIN && m.winAfter <= MISS_AFTER_WIN && m.winAfter >= MISS_MIN_AFTER_WIN) {
    return { ...out, cls: 'miss' };
  }
  // Brilliant auch bei „Good" (seit 0.521.3): chess.com verlangt „best or nearly best", rechnet die
  // Erwartungspunkte aber JE SPIELSTÄRKE — bei ~2000 Elo sind +3 und +3,8 praktisch gleich gewonnen, und ein
  // Zug, der bei uns (Lichess-Kurve ohne Elo) 4 Punkte Gewinnchance kostet, ist dort „nahezu best". Anlass:
  // 21.Ba6 der Partie MYXN3hXqz1X2hm7Cx6V47Q, bei chess.com brillant, bei uns „Good" (2026-09-24).
  if (!BRILLIANT_BASES.has(m.base)) return out;

  // Das Opfer kostet zwei Brett-Ladungen und wird höchstens einmal gesucht — und nur, wenn es noch zählt.
  let sacrifice: Sacrifice | null | undefined;
  const sacrificed = () => sacrifice !== undefined
    ? sacrifice : (sacrifice = sacrificedPiece(m.fenBefore, m.fenAfter, m.uci));

  // Fehlt der Zweitbeste, war es der einzige Kandidat — dann war nichts „ohnehin" gewonnen.
  const winningAnyway = second != null && secondCp != null
    && ((second.mate != null && secondCp > 0) || secondCp >= BRILLIANT_WINNING_ANYWAY_PAWNS * 100);
  const afterCp = moverCp(m.after, m.white, whiteToMove(m.fenAfter));
  if (!isPromotion(m.uci) && !winningAnyway && afterCp != null && afterCp >= BRILLIANT_MIN_AFTER_PAWNS * 100
      && !inCheck(m.fenBefore)) {
    const s = sacrificed();
    if (s) return { ...out, cls: 'brilliant', sacrifice: s };
  }

  if (m.base === 'best' && gapCp != null && gapCp >= GREAT_MIN_GAP_PAWNS * 100 && opponentErred
      && !winningAnyway && m.winAfter >= GREAT_MIN_AFTER_WIN && !sacrificed()) {
    return { ...out, cls: 'great' };
  }
  return out;
}

/**
 * Bewertung in Centipawns aus Sicht des Ziehenden. Matt in n = ±(1000 − n) Bauern wie
 * `GuessScoring.Pawns` — damit ist „Matt gegen kein Matt" ein riesiger Abstand und „#2 gegen #4" einer
 * von zwei Bauern. `mate 0` heißt: die Seite am Zug IST matt, dafür braucht es `whiteToMoveHere`.
 */
function moverCp(score: EvalScore, moverWhite: boolean, whiteToMoveHere: boolean): number | null {
  let white: number;
  if (score.mate != null) {
    const n = Math.min(Math.abs(score.mate), 999);
    const mateCp = (MATE_BASE_PAWNS - n) * 100;
    white = score.mate > 0 ? mateCp : score.mate < 0 ? -mateCp : whiteToMoveHere ? -mateCp : mateCp;
  } else if (score.cp != null) {
    white = score.cp;
  } else {
    return null;
  }
  // `0 - x` statt `-x`: sonst stünde bei Schwarz und 0,00 eine −0 im Ergebnis.
  return moverWhite ? white : 0 - white;
}

/** Anzeige einer Bewertung aus Weiß-Sicht — dieselbe Form wie auf dem Analysebrett („+0.34", „#3"). */
export function formatEval(score: EvalScore | null | undefined): string {
  if (!score) return '';
  if (score.mate != null) return '#' + score.mate;
  if (score.cp == null) return '';
  const v = score.cp / 100;
  return (v > 0 ? '+' : '') + v.toFixed(2);
}
