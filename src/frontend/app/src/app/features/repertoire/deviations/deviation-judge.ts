import { Observable, Subscription } from 'rxjs';
import { Chess } from 'chess.js';
import type { AnalysisLine, AnalysisState } from '../../analysis/analysis-engine.service';
import { whiteToMove } from '../../../shared/chess/fen-side';

/** Was das Urteil von der Engine des Analysebretts braucht (`AnalysisEngineService`). */
export interface JudgeEngine {
  analyze(fen: string): Promise<void>;
  stop(): void;
  setMultiPv(n: number): void;
  setDepth(d: number): void;
  readonly depthLimit: number;
  readonly analysis$: Observable<AnalysisState>;
}

/** Ein bester Zug fürs „besser wäre …". */
export interface BestMove { uci: string; san: string; evalText: string }

/** Urteil über die Antwort: `good` = höchstens `thresholdCp` schlechter als der beste Zug. `lossCp` aus Sicht des Ziehenden. */
export interface Verdict { good: boolean; lossCp: number; best: BestMove[] }

/** Vorgabe der Schwelle (Wunsch: „höchstens ~30 cp schlechter als der beste", konfigurierbar). */
export const DEFAULT_THRESHOLD_CP = 30;
export const THRESHOLD_OPTIONS = [10, 30, 50, 100] as const;

/** Tiefe/Zeit: die Stellung VOR der Antwort rechnet schon, während der Nutzer überlegt; die Stellung danach muss in ≤ 2 s stehen. */
export const BEFORE_DEPTH = 16;
export const AFTER_DEPTH = 12;
export const AFTER_TIMEOUT_MS = 1500;
export const BEFORE_TIMEOUT_MS = 2500;

/** Bewertung aus Sicht der Seite am Zug in `fen`, als Zentibauern (Matt = ±(100000 − Züge)). */
export function moverCp(line: AnalysisLine, fen: string): number {
  const white = whiteToMove(fen);
  const raw = line.scoreType === 'mate' ? Math.sign(line.score || 1) * (100000 - Math.abs(line.score)) : line.score;
  return white ? raw : -raw;
}

/**
 * Engine-Urteil des Abweichungs-Trainers (0.892.0): Stellung vor der Antwort mit MultiPV 3 ({@link prepare}, läuft während des
 * Überlegens), dann {@link judge}: steht die Antwort unter den besten Linien, zählt deren Wert, sonst wird die Stellung danach kurz
 * gerechnet (Seite wechselt → Vorzeichen). Die Tiefe der Engine wird danach wieder auf den alten Wert gesetzt (das Analysebrett und
 * die Nachbetrachtung teilen den Dienst). Ohne Angular; Uhr über `setTimeout` (Specs: fakeAsync).
 */
export class DeviationJudge {
  private before: { fen: string; lines: Promise<AnalysisLine[]> } | null = null;

  constructor(private readonly engine: JudgeEngine) {}

  /** Stellung vor der Antwort schon rechnen lassen. */
  prepare(fen: string): void {
    if (this.before?.fen === fen) return;
    this.before = { fen, lines: this.evaluate(fen, 3, BEFORE_DEPTH, BEFORE_TIMEOUT_MS) };
  }

  /** Urteil über den Zug `uci` in `fenBefore`. */
  async judge(fenBefore: string, uci: string, thresholdCp = DEFAULT_THRESHOLD_CP): Promise<Verdict> {
    this.prepare(fenBefore);
    const lines = (await this.before!.lines).filter(l => !!l.pvUci[0]);
    const best = lines.slice(0, 3).map(l => ({ uci: l.pvUci[0], san: sanOf(fenBefore, l.pvUci[0]), evalText: l.evalText }));
    if (!lines.length) return { good: true, lossCp: 0, best };   // keine Bewertung (Engine weg): nicht als Fehler werten
    const bestCp = moverCp(lines[0], fenBefore);
    const hit = lines.find(l => l.pvUci[0] === uci);
    let mine: number | null = hit ? moverCp(hit, fenBefore) : null;
    if (mine === null) {
      const after = afterFen(fenBefore, uci);
      const answer = after ? (await this.evaluate(after, 1, AFTER_DEPTH, AFTER_TIMEOUT_MS))[0] : undefined;
      // aus Sicht des Gegners gerechnet → umdrehen; ohne Antwort der Engine: nicht urteilen
      mine = answer && after ? -moverCp(answer, after) : bestCp;
    }
    const lossCp = Math.max(0, bestCp - mine);
    return { good: lossCp <= thresholdCp, lossCp, best };
  }

  /** Eine Stellung bis `depth` (oder `timeoutMs`) rechnen; liefert die zuletzt gemeldeten Linien. */
  evaluate(fen: string, multiPv: number, depth: number, timeoutMs: number): Promise<AnalysisLine[]> {
    const prevDepth = this.engine.depthLimit;
    return new Promise(resolve => {
      let last: AnalysisLine[] = [];
      let finished = false;
      // Vor `done` deklariert (Delta-Review 2026-11-09, F1-001): `analysis$` ist ein BehaviorSubject und meldet den
      // aktuellen Zustand SYNCHRON beim subscribe — passt der schon (dieselbe Stellung fertig gerechnet, z. B. „Nochmal“
      // mit demselben Zug), läuft `done` noch innerhalb von subscribe(); früher warf es dort an `const sub` (TDZ) und das
      // Urteil löste nie auf.
      let sub: Subscription | undefined;
      let timer: ReturnType<typeof setTimeout> | undefined;
      const done = () => {
        if (finished) return;
        finished = true;
        sub?.unsubscribe();
        if (timer !== undefined) clearTimeout(timer);
        this.engine.setDepth(prevDepth);
        resolve(last);
      };
      sub = this.engine.analysis$.subscribe(st => {
        if (st.fen !== fen) return;
        if (st.lines.length) last = st.lines;
        if (st.lines.length && (st.depth >= depth || !st.running)) done();
      });
      if (finished) { sub.unsubscribe(); return; }   // schon fertig gerechnet: nichts neu anstoßen
      timer = setTimeout(done, timeoutMs);
      this.engine.setMultiPv(multiPv);
      this.engine.setDepth(depth);
      this.engine.analyze(fen).catch(() => done());
    });
  }

  stop(): void { this.before = null; this.engine.stop(); }
}

function sanOf(fen: string, uci: string): string {
  try {
    const c = new Chess(fen);
    return c.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] }).san;
  } catch { return uci; }
}

function afterFen(fen: string, uci: string): string | null {
  try {
    const c = new Chess(fen);
    c.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] });
    return c.fen();
  } catch { return null; }
}
