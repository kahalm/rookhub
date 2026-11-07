import { Chess } from 'chess.js';
import type { Key } from 'chessground/types';
import type { Deviation } from '../../../shared/player-card/training-lines';
import { OpponentPositions, positionKey } from '../../../shared/player-card/opponent-positions';
import type { TrainerClock } from '../repertoire-trainer-state';
import { browserClock } from '../repertoire-trainer-state';
import type { BestMove, Verdict } from './deviation-judge';

/**
 * Eine Linie des Abweichungs-Trainers: `path` spielt der Nutzer wie im Trainer (seine Züge werden abgefragt, die des Gegners
 * kommen von selbst), dann spielt der Gegner `oppSan` und der Nutzer sucht die beste Antwort. Eine Verzweigung (anderer Gegnerzug
 * nach einer Antwort) springt ohne Abfrage an ihre Stelle (`auto` = so viele Halbzüge von `path` werden nur aufgestellt).
 */
export interface DeviationLine {
  path: string[];
  oppSan: string;
  /** Partien mit `oppSan` an dieser Stelle (Anzeige „7×"). */
  games: number;
  /** So viele Halbzüge von `path` werden ohne Abfrage aufgestellt (Verzweigung: alle). */
  auto: number;
  /** Die Abweichung, aus der die Linie stammt (Repertoire-Züge für „statt …"). */
  deviation: Deviation;
}

export type DeviationPhase = 'LOADING' | 'EMPTY' | 'PATH' | 'OPP' | 'ASK' | 'JUDGING' | 'GOOD' | 'BAD' | 'END' | 'DONE';

/** Was die Sitzung nach außen braucht: Engine-Urteil (Vorbereitung + Urteil) und Change Detection. */
export interface DeviationHooks {
  changed(): void;
  prepare(fen: string): void;
  judge(fenBefore: string, uci: string): Promise<Verdict>;
  stopEngine(): void;
}

export const DEV_OPP_DELAY_MS = 350;
/** Ein guter Zug bleibt kurz stehen, dann geht es weiter (Tippen/Leertaste sofort). */
export const DEV_GOOD_HOLD_MS = 1200;

/** Erste Linien: je Abweichung eine, in der Reihenfolge des Servers. */
export function linesOf(devs: readonly Deviation[]): DeviationLine[] {
  return devs.map(d => ({ path: [...d.path], oppSan: d.san, games: d.games, auto: 0, deviation: d }));
}

/**
 * Zustandsmaschine des Abweichungs-Trainers (0.892.0, Wunsch: „klick den baum normal durch und bei der abweichung fragt er mich nach
 * der besten antwort … wenn es züge vom gegner dagegen gibt diese weitermachen (mehrere → mehrere lines) … wenn keine mehr: gegen
 * maia"). `PATH` (Nutzer spielt den Weg, Gegnerzüge über `OPP`) → `ASK` (Abweichung gespielt, beste Antwort gesucht) → `JUDGING`
 * → `GOOD`/`BAD` (Nochmal, Lösung zeigen) → nach `GOOD` seine Fortsetzung aus `positions` (häufigste weiter, die anderen als neue
 * Linien hinten an) oder `END` (keine Partien mehr: Nachbetrachtung + Maia) → nächste Linie bzw. `DONE`. Kein SR. Ohne Angular.
 */
export class DeviationSession {
  phase: DeviationPhase = 'LOADING';
  queue: DeviationLine[] = [];
  index = 0;
  /** Gespielte Halbzüge (SAN) der laufenden Linie ab der Grundstellung. */
  played: string[] = [];
  /** Stellungen nach jedem Halbzug (`fens[0]` = Grundstellung). */
  fens: string[] = [];
  fen = new Chess().fen();
  lastMove?: [Key, Key];
  /** PATH: der erwartete Zug, wenn der Nutzer etwas anderes gezogen hat (Hinweis „in deinem Repertoire: …"). */
  pathHint: string | null = null;
  /** Stellung vor der gesuchten Antwort (ASK/JUDGING/BAD). */
  askFen = '';
  /** Der Zug des Gegners, auf den geantwortet wird (Anzeige). */
  askSan = '';
  askGames = 0;
  verdict: Verdict | null = null;
  /** Der Zug des Nutzers, über den geurteilt wurde. */
  answerSan = '';
  /** Zähler der Sitzung. */
  good = 0;
  bad = 0;
  private positions: OpponentPositions = {};
  private timer: unknown = null;
  private chess = new Chess();
  /** Generation: ein spätes Urteil einer verlassenen Stellung zählt nicht. */
  private epoch = 0;

  constructor(private readonly hooks: DeviationHooks, private userColor: 'w' | 'b',
              private readonly clock: TrainerClock = browserClock) {}

  get current(): DeviationLine | undefined { return this.queue[this.index]; }
  get color(): 'w' | 'b' { return this.userColor; }
  get userToMove(): boolean { return this.chess.turn() === this.userColor; }
  /** Das Brett nimmt Züge an. */
  get playable(): boolean { return (this.phase === 'PATH' || this.phase === 'ASK') && this.userToMove; }

  /** `color` = die Farbe, für die der Server Repertoire und Abweichungen gerechnet hat (Hotfix 0.893.1: maßgeblich ist SEINE
   *  Antwort, nicht die Adresse — fällt er auf die andere Farbe zurück, müssen Brett, Abfrage und Gegnerzüge mitgehen). */
  start(lines: DeviationLine[], positions: OpponentPositions, color?: 'w' | 'b' | null): void {
    if (color === 'w' || color === 'b') this.userColor = color;
    this.queue = lines;
    this.positions = positions ?? {};
    this.index = 0;
    this.good = 0;
    this.bad = 0;
    if (!lines.length) { this.phase = 'EMPTY'; this.hooks.changed(); return; }
    this.startLine();
  }

  startLine(): void {
    this.clearTimer();
    this.epoch++;
    const line = this.current;
    if (!line) { this.phase = 'DONE'; this.hooks.stopEngine(); this.hooks.changed(); return; }
    this.chess = new Chess();
    this.played = [];
    this.fens = [this.chess.fen()];
    this.lastMove = undefined;
    this.pathHint = null;
    this.verdict = null;
    for (let i = 0; i < line.auto && i < line.path.length; i++) this.apply(line.path[i]);
    this.sync();
    this.advance();
  }

  /** Den Weg weiterspielen: Gegnerzüge von selbst, beim Nutzer warten; am Ende des Wegs spielt der Gegner seinen Zug. */
  private advance(): void {
    const line = this.current;
    if (!line) return;
    const at = this.played.length;
    if (at < line.path.length) {
      if (this.userToMove) { this.phase = 'PATH'; this.hooks.changed(); return; }
      this.oppMove(line.path[at], () => this.advance());
      return;
    }
    this.oppMove(line.oppSan, () => this.ask(line.oppSan, line.games));
  }

  private oppMove(san: string, then: () => void): void {
    this.phase = 'OPP';
    this.hooks.changed();
    const epoch = this.epoch;
    this.timer = this.clock.setTimeout(() => {
      this.timer = null;
      if (epoch !== this.epoch) return;
      if (!this.apply(san)) { this.end(); return; }   // unlesbarer Zug: hier ist die Linie zu Ende
      this.sync();
      then();
    }, DEV_OPP_DELAY_MS);
  }

  private ask(san: string, games: number): void {
    this.askFen = this.chess.fen();
    this.askSan = san;
    this.askGames = games;
    this.verdict = null;
    this.answerSan = '';
    this.phase = 'ASK';
    this.hooks.prepare(this.askFen);
    this.hooks.changed();
  }

  /** Zug vom Brett. */
  onMove(orig: string, dest: string, promotion?: string): void {
    if (!this.playable) return;
    const line = this.current;
    if (!line) return;
    if (this.phase === 'PATH') {
      const expected = line.path[this.played.length];
      let san: string, after: string;
      try {
        const c = new Chess(this.chess.fen());
        san = c.move({ from: orig, to: dest, promotion: promotion ?? 'q' }).san;
        after = c.fen();
      } catch { return; }
      // Stellungsvergleich statt SAN-Vergleich (Hotfix 0.893.1): der Pfad kommt aus dem Repertoire (Server-SAN), „Bb5" und „Bb5+"
      // sind derselbe Zug.
      if (positionKey(after) !== this.keyAfter(expected)) { this.pathHint = expected; this.hooks.changed(); return; }
      this.pathHint = null;
      this.apply(san);
      this.sync();
      this.advance();
      return;
    }
    // ASK: Antwort spielen und urteilen lassen
    let mv;
    try { mv = this.chess.move({ from: orig, to: dest, promotion: promotion ?? 'q' }); } catch { return; }
    this.played.push(mv.san);
    this.fens.push(this.chess.fen());
    this.lastMove = [mv.from as Key, mv.to as Key];
    this.answerSan = mv.san;
    this.sync();
    this.phase = 'JUDGING';
    this.hooks.changed();
    const epoch = this.epoch;
    const uci = mv.from + mv.to + (mv.promotion ?? '');
    this.hooks.judge(this.askFen, uci).then(v => {
      if (epoch !== this.epoch || this.phase !== 'JUDGING') return;
      this.verdict = v;
      if (v.good) { this.good++; this.phase = 'GOOD'; this.timer = this.clock.setTimeout(() => { this.timer = null; this.afterGood(); }, DEV_GOOD_HOLD_MS); }
      else { this.bad++; this.phase = 'BAD'; }
      this.hooks.changed();
    });
  }

  /** „Nochmal": Antwort zurücknehmen, dieselbe Frage. */
  retry(): void {
    if (this.phase !== 'BAD') return;
    this.undoLast();
    this.ask(this.askSan, this.askGames);
  }

  /** „Lösung zeigen": den besten Zug spielen und weiter wie nach einer guten Antwort. */
  showSolution(): void {
    if (this.phase !== 'BAD' || !this.verdict?.best.length) return;
    const best: BestMove = this.verdict.best[0];
    this.undoLast();
    this.apply(best.san);
    this.answerSan = best.san;
    this.sync();
    this.afterGood();
  }

  /** Tippen/Leertaste: einen guten Zug nicht abwarten bzw. am Linienende weiter. */
  tap(): boolean {
    if (this.phase === 'GOOD') { this.clearTimer(); this.afterGood(); return true; }
    if (this.phase === 'END') { this.next(); return true; }
    return false;
  }

  /** Nach einer guten Antwort: seine Züge in der Stellung danach (häufigste weiter, die anderen als eigene Linien), sonst Ende. */
  private afterGood(): void {
    const line = this.current;
    if (!line) return;
    const st = this.positions[positionKey(this.chess.fen())];
    const moves = Object.entries(st?.m ?? {}).sort((a, b) => b[1] - a[1]);
    if (!moves.length) { this.end(); return; }
    const prefix = [...this.played];
    // die weiteren als neue Linien hinter die laufende (Verzweigung: der Weg wird nur aufgestellt)
    const branches = moves.slice(1).map(([san, n]) => ({ path: prefix, oppSan: san, games: n, auto: prefix.length, deviation: line.deviation }));
    this.queue.splice(this.index + 1, 0, ...branches);
    const [san, n] = moves[0];
    this.oppMove(san, () => this.ask(san, n));
  }

  private end(): void {
    this.clearTimer();
    this.phase = 'END';
    this.hooks.stopEngine();
    this.hooks.changed();
  }

  /** Nächste Linie. */
  next(): void {
    this.index++;
    this.startLine();
  }

  /** Linie überspringen. */
  skip(): void {
    if (this.phase === 'DONE' || this.phase === 'EMPTY' || this.phase === 'LOADING') return;
    this.hooks.stopEngine();
    this.next();
  }

  dispose(): void { this.clearTimer(); this.epoch++; }

  private apply(san: string): boolean {
    try {
      const mv = this.chess.move(san);
      this.played.push(mv.san);
      this.fens.push(this.chess.fen());
      this.lastMove = [mv.from as Key, mv.to as Key];
      return true;
    } catch { return false; }
  }

  /** Stellungs-Schlüssel nach `san` in der laufenden Stellung (`null` = unlesbar). */
  private keyAfter(san: string | undefined): string | null {
    if (!san) return null;
    try { const c = new Chess(this.chess.fen()); c.move(san); return positionKey(c.fen()); } catch { return null; }
  }

  private undoLast(): void {
    this.chess.undo();
    this.played.pop();
    this.fens.pop();
    const h = this.chess.history({ verbose: true });
    const prev = h[h.length - 1];
    this.lastMove = prev ? [prev.from as Key, prev.to as Key] : undefined;
    this.sync();
  }

  private sync(): void { this.fen = this.chess.fen(); }

  private clearTimer(): void {
    if (this.timer !== null) { this.clock.clearTimeout(this.timer); this.timer = null; }
  }
}
