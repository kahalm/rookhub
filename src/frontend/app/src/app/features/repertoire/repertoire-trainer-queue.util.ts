import type { ParsedGame } from '../../shared/pgn-viewer/pgn-parser';
import type { TrainColor } from '../../shared/repertoire/repertoire-color.util';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { linesInChapter, userMoveCount } from './repertoire-trainer.util';
import { DEFAULT_LINE_ORDER, LineOrder } from './repertoire-line-order';
import { shuffle } from './repertoire-sr.util';

/**
 * Warteschlangen-Bausteine des Repertoire-Trainers (Zerlegung Z21): welche Linien übbar sind und in welcher
 * Reihenfolge sie kommen — nach der gewählten Reihenfolge ({@link orderLines}), gegen den gespeicherten Gegner eines
 * „Prep: …"-Repertoires ({@link storedOpponentLines}) bzw. in der ausdrücklichen Gegner-Sitzung ({@link opponentLines}).
 * Rein, ohne Angular.
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
  /**
   * Sitzung gegen den GESPEICHERTEN Gegner eines „Prep: …"-Repertoires (0.999.3); fehlt = keine solche Sitzung.
   * `rank` = Platz der Linie in seiner Reihenfolge (0 = am wahrscheinlichsten; eine Linie, die er nicht kennt,
   * bekommt {@link UNRANKED}); `null`, solange die Reihung fehlt (lädt noch, Abruf gescheitert).
   */
  opponent?: { rank: ((line: T) => number) | null };
  /** Mischen — Vorgabe {@link shuffle}; Tests nageln es fest. */
  shuffle?: (lines: readonly T[]) => T[];
}

/** Platz einer Linie, die der Gegner nicht kennt: hinter allen gereihten, untereinander in Repertoire-Reihenfolge. */
export const UNRANKED = Number.MAX_SAFE_INTEGER;

/** Stabil nach einem Zahlenschlüssel, kleinster zuerst (eigener Index als letzter Schlüssel, unabhängig von der Engine). */
function stableBy<T>(lines: readonly T[], key: (line: T) => number): T[] {
  return lines.map((line, at) => ({ line, at, key: key(line) }))
    .sort((a, b) => a.key - b.key || a.at - b.at)
    .map(x => x.line);
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
 * - `opponent` (0.999.3): in der Reihenfolge des gespeicherten Gegners, was er nicht kennt dahinter; OHNE
 *   Gegner-Sitzung gilt die Vorgabe.
 *
 * Gleichstand (gleiche Wiederkehr bzw. Häufigkeit): beim Abfragen zufällig, beim Lernen Repertoire-Reihenfolge.
 * In der Sitzung gegen den gespeicherten Gegner ({@link LineOrderRule.opponent}) entscheidet bei `leastTrained` und
 * `mostTrained` stattdessen SEINE Reihenfolge — untrainierte zuerst, darin die wahrscheinlichsten zuerst —, in beiden
 * Modi; fehlt die Reihung noch, die Repertoire-Reihenfolge. `random`, `repertoire` und `frequency` bleiben wie sonst.
 * Die Sortierung ist stabil.
 */
export function orderLines<T>(lines: readonly T[], rule: LineOrderRule<T>): T[] {
  const opponent = rule.opponent;
  let order: LineOrder = rule.order;
  if (order === 'frequency' && !rule.frequency) order = DEFAULT_LINE_ORDER;
  if (order === 'opponent' && !opponent) order = DEFAULT_LINE_ORDER;
  if (order === 'repertoire') return [...lines];
  const mix = rule.shuffle ?? shuffle;
  if (order === 'random') return mix(lines);
  const rank = opponent?.rank;
  const byOpponent = () => rank ? stableBy(lines, rank) : [...lines];
  if (order === 'opponent') return byOpponent();
  const frequency = rule.frequency;
  if (order === 'frequency' && frequency) return stableBy(rule.mode === 'quiz' ? mix(lines) : lines, l => -frequency(l));
  // Wenig/viel trainiert — kleinster Schlüssel zuerst: Wiederkehr aufsteigend bzw. (negiert) absteigend.
  const base = opponent ? byOpponent() : rule.mode === 'quiz' ? mix(lines) : lines;
  return stableBy(base, order === 'mostTrained' ? l => -rule.returnHours(l) : rule.returnHours);
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

/** Sitzung gegen den GESPEICHERTEN Gegner eines „Prep: …"-Repertoires (0.999.3): welche Linien, in welcher Reihenfolge. */
export interface StoredOpponentSession {
  /** Meine Farbe gegen ihn (nur diese Kapitel); null = alle. */
  color: TrainColor | null;
  /** Ausdrücklich gewählte Einstiegslinie (`?line=`): kommt IMMER und zuerst — auch pausiert oder nicht fällig. */
  start: string | null;
  isPaused: (lineKey: string) => boolean;
  /** Abfragen: nur Linien, die das erfüllen (fällig oder untrainiert). `null` = alle — Lernen und die ausdrückliche
   *  Wiederholung („Alle Linien üben", „Von vorn"). */
  keep: ((line: ParsedGame) => boolean) | null;
  /** Die gewählte Reihenfolge ({@link orderLines} mit dem Rang des Gegners); bekommt die Linien in Repertoire-Reihenfolge. */
  order: (lines: ParsedGame[]) => ParsedGame[];
}

/**
 * Die Linien der Sitzung gegen den gespeicherten Gegner: die Kapitel der Farbe (Kapitel-Filter gilt), ohne pausierte,
 * beim Abfragen nur fällige oder untrainierte (`keep`), in der gewählten Reihenfolge. Anders als die ausdrückliche
 * Gegner-Sitzung ({@link opponentLines}, `?opponent=` von der Spielerkarte) schneidet eine Einstiegslinie nichts ab:
 * sie steht vorn, danach kommen ALLE übrigen Linien der Regel.
 */
export function storedOpponentLines(set: TrainerLineSet, session: StoredOpponentSession): ParsedGame[] {
  let lines = playableLines(set);
  if (session.color) lines = lines.filter(l => set.colorOf(l) === session.color);
  const first = session.start ? lines.find(l => lineKeyOf(l) === session.start) ?? null : null;
  const keep = session.keep;
  const rest = lines.filter(l => l !== first && !session.isPaused(lineKeyOf(l)) && (!keep || keep(l)));
  return first ? [first, ...session.order(rest)] : session.order(rest);
}

/**
 * Die Warteschlange einer laufenden Sitzung fortschreiben (Reihung kam nach, Häufigkeiten kamen nach): alles bis
 * einschließlich der laufenden Linie `qIndex` bleibt, danach die Linien aus `lines` in ihrer Reihenfolge — ohne die
 * schon gespielten zu wiederholen und ohne etwas wegzulassen, das in der neuen Reihenfolge VOR der laufenden stünde
 * (das unterscheidet sie von {@link extendQueue}, die „ab der Einstiegslinie" schneidet).
 */
export function continueQueue(queue: ParsedGame[], qIndex: number, lines: ParsedGame[]): ParsedGame[] {
  const played = queue.slice(0, qIndex + 1);
  const seen = new Set(played);
  return [...played, ...lines.filter(l => !seen.has(l))];
}
