import { LegalMove, legalMove, legalSans } from '../../shared/chess/legal-moves';
import { TrainingDrillItemDto, TrainingLesson, TrainingSegment, TrainingStatus, trainingGameLabel } from './training-videos.service';

/**
 * Reine Logik des „Nachspielens“ (Trainerstunden): welche Segmente Aufgaben sind, welcher Zug gesucht ist (nur wenn er
 * aus der FEN legal ist — Daten aus der Aufbereitung sind nicht immer stimmig), wie ein Versuch ausgeht und welcher
 * Ausschnitt des Videos „Erklärung hören“ spielt. Ohne Angular, damit die Spec die Regeln direkt prüft.
 */

/** Nach so vielen Fehlversuchen gilt die Aufgabe als falsch und die Lösung wird gezeigt. */
export const MAX_FAILED_ATTEMPTS = 3;
/** Ausschnitt ohne Ende in den Daten: so lange läuft „Erklärung hören“. */
export const DEFAULT_CLIP_SEC = 60;

export interface DrillItem {
  lessonId: number;
  date: string;
  /** Teil der Stunde (`private` | `general`). */
  part: string;
  hasVideo: boolean;
  gameKey: string | null;
  /** „Weiß – Schwarz“ (Stunde) bzw. der Name aus dem Fragen-Satz. */
  gameLabel: string | null;
  segment: TrainingSegment;
  /** Die Stellung am Brett: bei einer Aufgabe die Stellung VOR dem gesuchten Zug (`taskFen` des Servers), sonst `fen`. */
  fen: string;
  /** Die RookHub-Partie (Fragen-Satz), falls zugeordnet. */
  rookhubGameId: number | null;
  /** Der gesuchte Zug — `null` = nur ansehen (keine Aufgabe oder Zug aus der FEN nicht legal). */
  target: LegalMove | null;
}

/** Zug ohne Wertungszeichen, Schach-/Mattzeichen, Zugnummer; Rochade mit „O“. */
export function normalizeSan(san: string): string {
  return san.trim()
    .replace(/^\d+\s*\.(\.\.)?\s*/, '')
    .replace(/^…\s*/, '')
    .replace(/[!?]+$/, '')
    .replace(/[+#]+$/, '')
    .replace(/0-0-0/g, 'O-O-O').replace(/0-0/g, 'O-O')
    .trim();
}

/** Der legale Zug zu `san` in `fen` (Schreibweise tolerant: „Nf3+“, „Nf3!“, „12...Nf3“); `null` = nicht legal / FEN kaputt. */
export function resolveMove(fen: string | null | undefined, san: string | null | undefined): LegalMove | null {
  if (!fen || !san) return null;
  const sans = legalSans(fen);
  if (!sans) return null;
  const want = normalizeSan(san);
  if (!want) return null;
  const hit = sans.find(s => normalizeSan(s) === want);
  return hit ? legalMove(fen, hit) : null;
}

/** Ist die FEN lesbar (chess.js)? */
export function isValidFen(fen: string | null | undefined): fen is string {
  return !!fen && legalSans(fen) !== null;
}

/** Aufgabe = gespielter Partiezug mit Zug ODER eine Frage. */
export function isTaskCandidate(seg: TrainingSegment): boolean {
  return (seg.moveKind === 'played' && !!seg.move) || seg.question != null;
}

/** Gesuchter Zug (SAN): die Antwort der Frage, sonst der gespielte Zug. */
export function targetSan(seg: TrainingSegment): string | null {
  if (!isTaskCandidate(seg)) return null;
  return seg.question?.answer || seg.move || null;
}

/** Der gesuchte Zug als legaler Zug — `null`, wenn das Segment keine Aufgabe ist oder der Zug aus der FEN nicht geht.
 *  Hat der Server die Aufgabe bestimmt (`taskFen`/`taskMove`, auch über die Partie zurückgerechnet), gilt seine; sonst
 *  (ältere Antwort ohne die Felder) der Zug aus der Segment-FEN. */
export function drillTarget(seg: TrainingSegment): LegalMove | null {
  if (seg.taskFen !== undefined) return seg.taskFen && seg.taskMove ? resolveMove(seg.taskFen, seg.taskMove) : null;
  return resolveMove(seg.fen, targetSan(seg));
}

/** Stellung am Brett für ein Segment: die Aufgabenstellung, sonst die Segment-FEN. */
export function drillFen(seg: TrainingSegment): string {
  return (seg.taskFen && drillTarget(seg) ? seg.taskFen : seg.fen) ?? '';
}

function bySegmentTime(a: DrillItem, b: DrillItem): number {
  return a.segment.startSec - b.segment.startSec || a.segment.id - b.segment.id;
}

/** Nachspiel-Liste einer Stunde: nur Segmente mit lesbarer FEN, nach Zeit. */
export function drillItemsOfLesson(lesson: TrainingLesson, unknownName: string): DrillItem[] {
  const labels = new Map(lesson.games.map(g => [g.key, trainingGameLabel(g, unknownName)]));
  return lesson.segments
    .filter(s => isValidFen(s.fen))
    .map(s => ({
      lessonId: lesson.id, date: lesson.date, part: lesson.part ?? 'private', hasVideo: lesson.hasVideo, gameKey: s.gameKey,
      gameLabel: s.gameKey ? labels.get(s.gameKey) ?? null : null, segment: s, fen: drillFen(s),
      rookhubGameId: s.gameKey ? lesson.games.find(g => g.key === s.gameKey)?.rookhubGameId ?? null : null,
      target: drillTarget(s),
    }))
    .sort(bySegmentTime);
}

/** Fragen-Satz über alle Stunden (Server: neueste Stunde zuerst) — innerhalb einer Stunde nach Zeit. */
export function drillItemsOfQuestions(items: TrainingDrillItemDto[]): DrillItem[] {
  const order = new Map<number, number>();
  items.forEach(i => { if (!order.has(i.lessonId)) order.set(i.lessonId, order.size); });
  return items
    .filter(i => isValidFen(i.segment.fen))
    .map(i => ({
      lessonId: i.lessonId, date: i.date, part: i.part ?? 'private', hasVideo: i.hasVideo, gameKey: i.segment.gameKey ?? i.gameLabel,
      gameLabel: i.gameLabel, segment: i.segment, fen: drillFen(i.segment), rookhubGameId: i.rookhubGameId ?? null,
      target: drillTarget(i.segment),
    }))
    .sort((a, b) => order.get(a.lessonId)! - order.get(b.lessonId)! || bySegmentTime(a, b));
}

/** Kopf einer Partiegruppe vor `index`? (erste Stellung oder andere Partie/Stunde als die davor) */
export function startsGroup(items: readonly DrillItem[], index: number): boolean {
  if (index <= 0) return index === 0;
  const a = items[index - 1], b = items[index];
  return a.lessonId !== b.lessonId || a.gameKey !== b.gameKey;
}

/** Ausgang eines Zugversuchs: `solved`, `wrong` (zu viele Fehlversuche) oder `null` = weiter raten. */
export function attemptOutcome(correct: boolean, failedAttempts: number): TrainingStatus | null {
  if (correct) return 'solved';
  return failedAttempts >= MAX_FAILED_ATTEMPTS ? 'wrong' : null;
}

/** Ist der gezogene Zug der gesuchte? Vergleich über Felder + Umwandlung (die SAN schreibt jeder etwas anders). */
export function isTargetMove(target: LegalMove, move: { from: string; to: string; promotion?: string }): boolean {
  return target.from === move.from && target.to === move.to && (target.promotion ?? null) === (move.promotion ?? null);
}

/** Ausschnitt für „Erklärung hören“: Zitat, sonst das Segment, sonst 60 s ab Start; Ende immer nach dem Start. */
export function clipRange(seg: Pick<TrainingSegment, 'startSec' | 'endSec' | 'quoteStartSec' | 'quoteEndSec'>): { start: number; end: number } {
  const start = Math.max(0, seg.quoteStartSec ?? seg.startSec);
  const end = seg.quoteEndSec ?? seg.endSec ?? start + DEFAULT_CLIP_SEC;
  return { start, end: end > start ? end : start + DEFAULT_CLIP_SEC };
}

/**
 * Segment, das zur Videozeit `t` läuft: das zuletzt begonnene, dessen Bereich `t` enthält (Segmente dürfen sich
 * überlappen); ohne Ende reicht es bis zum Start des nächsten. `segments` nach `startSec` sortiert.
 */
export function activeSegmentAt<T extends Pick<TrainingSegment, 'id' | 'startSec' | 'endSec'>>(segments: readonly T[], t: number): T | null {
  let hit: T | null = null;
  for (let i = 0; i < segments.length; i++) {
    const s = segments[i];
    if (s.startSec > t) break;
    const end = s.endSec ?? (segments[i + 1]?.startSec ?? Number.POSITIVE_INFINITY);
    if (t < Math.max(end, s.startSec + 1)) hit = s;
  }
  return hit;
}

/** Zähler fürs Endbild. */
export function countResults(results: ReadonlyMap<number, TrainingStatus>): Record<TrainingStatus, number> {
  const c: Record<TrainingStatus, number> = { solved: 0, wrong: 0, skipped: 0 };
  results.forEach(s => { c[s]++; });
  return c;
}
