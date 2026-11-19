import type { ParsedGame } from '../../shared/pgn-viewer/pgn-parser';
import type { TrainColor } from '../../shared/repertoire/repertoire-color.util';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { linesInChapter, userMoveCount } from './repertoire-trainer.util';
import { DEFAULT_LINE_ORDER, LineOrder } from './repertoire-line-order';
import { shuffle } from './repertoire-sr.util';

/**
 * Warteschlangen-Bausteine des Repertoire-Trainers (Zerlegung Z21): welche Linien übbar sind und in welcher
 * Reihenfolge sie kommen — nach der gewählten Reihenfolge ({@link orderLines}) bzw. gegen einen Gegner. Rein, ohne Angular.
 */

/** Was {@link orderLines} zum Reihen braucht — alles von außen, damit die Regel ohne Trainer prüfbar bleibt. */
export interface LineOrderRule<T> {
  order: LineOrder;
  /** Abfragen oder Lernen — bestimmt nur den GLEICHSTAND: Abfragen zufällig, Lernen in Repertoire-Reihenfolge. */
  mode: 'quiz' | 'learn';
  /** Wiederkehr in Stunden bei richtiger Lösung (`hoursUntilReturnIfSolved`): kleiner = weniger trainiert. */
  returnHours: (line: T) => number;
  /** Häufigkeit 0…1, −1 = unbekannt; `null` = es gibt keine Häufigkeiten (offline, Abruf gescheitert, kein Token). */
  frequency: ((line: T) => number) | null;
  /** Mischen — Vorgabe {@link shuffle}; Tests nageln es fest. */
  shuffle?: (lines: readonly T[]) => T[];
}

/**
 * Die Linien einer Sitzung in der gewählten Reihenfolge (0.996.0); `lines` kommt in Repertoire-(PGN-)Reihenfolge und
 * bleibt unverändert.
 *
 * - `leastTrained` (Vorgabe): kürzeste Wiederkehr bei richtiger Lösung zuerst — Stufe 0 vor 1 vor 2 …
 * - `mostTrained`: die Umkehrung.
 * - `random`: gemischt — in beiden Modi.
 * - `repertoire`: wie im Repertoire.
 * - `frequency`: häufigste zuerst, Linien ohne Wert zuletzt; OHNE Häufigkeiten gilt die Vorgabe.
 *
 * Gleichstand (gleiche Wiederkehr bzw. Häufigkeit): beim Abfragen zufällig, beim Lernen Repertoire-Reihenfolge.
 * Die Sortierung ist stabil (eigener Index als letzter Schlüssel, unabhängig von der Engine).
 */
export function orderLines<T>(lines: readonly T[], rule: LineOrderRule<T>): T[] {
  const order: LineOrder = rule.order === 'frequency' && !rule.frequency ? DEFAULT_LINE_ORDER : rule.order;
  if (order === 'repertoire') return [...lines];
  const base = rule.mode === 'quiz' || order === 'random' ? (rule.shuffle ?? shuffle)(lines) : [...lines];
  if (order === 'random') return base;
  const frequency = rule.frequency;
  // Kleinster Schlüssel zuerst: Wiederkehr aufsteigend bzw. (negiert) absteigend, Häufigkeit absteigend.
  const key = order === 'frequency' && frequency ? (l: T) => -frequency(l)
    : order === 'mostTrained' ? (l: T) => -rule.returnHours(l)
    : rule.returnHours;
  return base.map((line, at) => ({ line, at, key: key(line) }))
    .sort((a, b) => a.key - b.key || a.at - b.at)
    .map(x => x.line);
}

/** Die Linien einer Sitzung mit ihrem Kapitel-Filter und der Trainingsfarbe je Linie. */
export interface TrainerLineSet {
  lines: ParsedGame[];
  chapter: string | null;
  colorOf: (line: ParsedGame) => TrainColor;
}

/** Reihenfolge nach einem Gegner: Farbe (`?color=`), Platz je Linien-Schlüssel (null = noch keine), Einstiegslinie. */
export interface OpponentOrder {
  color: TrainColor | null;
  rank: Map<string, number> | null;
  start: string | null;
  /** Pausierte Linien bleiben draußen — außer der ausdrücklich gewählten Einstiegslinie (`start`). */
  isPaused?: (lineKey: string) => boolean;
}

/** Stabiler Linien-Schlüssel (identisch zur Linienliste) aus der SAN-Zugfolge. */
export function lineKeyOf(line: ParsedGame): string {
  return lineKeyFromSans(line.moves.map(m => m.san));
}

/** Hat die Linie mindestens einen Zug ihrer Trainingsfarbe? Sonst gibt es an ihr nichts zu üben. */
export function hasUserMove(set: TrainerLineSet, line: ParsedGame): boolean {
  return userMoveCount(line, set.colorOf(line)) > 0;
}

/** Übbare Linien des Kapitels; mit `onlyKey` nur die eine Linie (`?line=`). */
export function playableLines(set: TrainerLineSet, onlyKey: string | null = null): ParsedGame[] {
  let filtered = linesInChapter(set.lines, set.chapter);
  if (onlyKey) filtered = filtered.filter(l => lineKeyOf(l) === onlyKey);
  return filtered.filter(l => hasUserMove(set, l));
}

/** Summe der Nutzerzüge einer Warteschlange (Fortschrittsbalken). */
export function countUserMoves(set: TrainerLineSet, queue: ParsedGame[]): number {
  return queue.reduce((sum, l) => sum + userMoveCount(l, set.colorOf(l)), 0);
}

/** In der Reihenfolge des Gegners; Linien, die er nicht kennt (Schlüssel nicht in der Reihung), dahinter wie gehabt. */
export function byRank(lines: ParsedGame[], rank: Map<string, number>): ParsedGame[] {
  const at = (l: ParsedGame) => rank.get(lineKeyOf(l)) ?? Number.MAX_SAFE_INTEGER;
  return [...lines].sort((a, b) => at(a) - at(b));
}

/** Die Linien der Gegner-Sitzung: alle der Farbe (Kapitel-Filter gilt), in seiner Reihenfolge — OHNE Fälligkeit und
 *  ohne Modus-Wechsel, aber ohne pausierte Linien; mit Einstieg nur die Einstiegslinie und die nach ihr. */
export function opponentLines(set: TrainerLineSet, opp: OpponentOrder): ParsedGame[] {
  let lines = playableLines(set);
  if (opp.color) lines = lines.filter(l => set.colorOf(l) === opp.color);
  const start = opp.start;
  const isPaused = opp.isPaused;
  if (isPaused) lines = lines.filter(l => { const k = lineKeyOf(l); return k === start || !isPaused(k); });
  if (!opp.rank) return start ? lines.filter(l => lineKeyOf(l) === start) : lines;
  const ordered = byRank(lines, opp.rank);
  if (!start) return ordered;
  const at = ordered.findIndex(l => lineKeyOf(l) === start);
  return at < 0 ? ordered : ordered.slice(at);
}

/** Die Reihung kam, während die Linie `qIndex` läuft: bis dahin bleibt alles, danach die weiteren Linien. */
export function extendQueue(queue: ParsedGame[], qIndex: number, lines: ParsedGame[]): ParsedGame[] {
  const current = queue[qIndex];
  const at = current ? lines.findIndex(l => l === current) : -1;
  return [...queue.slice(0, qIndex + 1), ...(at < 0 ? lines.filter(l => l !== current) : lines.slice(at + 1))];
}
