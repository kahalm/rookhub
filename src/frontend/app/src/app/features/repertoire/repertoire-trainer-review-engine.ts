import { Observable, Subscription } from 'rxjs';
import type { AnalysisLine, AnalysisState } from '../analysis/analysis-engine.service';
import { evalBar } from '../analysis/analysis-readout';
import { uciLineToSan } from '../analysis/engine-lines.util';
import { localStore, readRaw, writeRaw } from '../../core/local-json-store';

/** Was die Nachbetrachtung von der Engine des Analysebretts braucht (`AnalysisEngineService`). */
export interface ReviewEngineDeps {
  analyze(fen: string): Promise<void>;
  stop(): void;
  setMultiPv(n: number): void;
  readonly analysis$: Observable<AnalysisState>;
}

/** Schalter „Engine" der Nachbetrachtung — gemerkt je Gerät (Vorgabe aus). */
export const REVIEW_ENGINE_KEY = 'rookhub_rep_train_review_engine';

export function readReviewEngineOn(): boolean { return readRaw(localStore(), REVIEW_ENGINE_KEY) === '1'; }
export function saveReviewEngineOn(on: boolean): void { writeRaw(localStore(), REVIEW_ENGINE_KEY, on ? '1' : '0'); }

/**
 * Engine der Nachbetrachtung im Repertoire-Trainer (0.890.0, Wunsch: „durch die Züge klicken und die Engine zuschalten — wie im
 * Analysebrett"): dieselbe Engine wie das Analysebrett (Stockfish im Browser bzw. die dort gewählte externe), Bewertungsleiste und
 * beste Linie. {@link sync} bekommt die zu rechnende Stellung (oder `null` = aus) und startet/stoppt nur bei einem Wechsel. Ohne Angular.
 */
export class TrainerReviewEngine {
  private fen: string | null = null;
  private best: AnalysisLine | null = null;
  depth = 0;
  private sub: Subscription | null = null;

  constructor(private readonly engine: () => ReviewEngineDeps | null, private readonly changed: () => void) {}

  /** Läuft gerade (oder rechnet) eine Stellung? */
  get active(): boolean { return this.fen !== null; }

  /** Die zu rechnende Stellung setzen; `null` hält die Engine an. Gleiche Stellung → nichts. */
  sync(fen: string | null): void {
    if (fen === this.fen) return;
    const engine = this.engine();
    if (!engine) return;
    this.fen = fen;
    this.best = null;
    this.depth = 0;
    if (fen === null) { engine.stop(); return; }
    if (!this.sub) {
      this.sub = engine.analysis$.subscribe(st => {
        if (!this.fen || st.fen !== this.fen) return;   // Zeilen einer überholten Stellung
        this.best = st.lines[0] ?? null;
        this.depth = st.depth;
        this.changed();
      });
    }
    engine.setMultiPv(1);
    engine.analyze(fen).catch(() => undefined);   // Fehler zeigt der Dienst selbst (engineFatalError$)
  }

  get evalText(): string { return this.best ? evalBar(this.best, null).evalText : ''; }
  /** Weißer Anteil der Leiste (0–100); ohne Bewertung die Mitte. */
  get whiteHeight(): number { return evalBar(this.best, null).whiteHeight; }
  /** Beste Linie als SAN-Kette (höchstens 8 Halbzüge). */
  get bestLine(): string { return this.best && this.fen ? uciLineToSan(this.fen, this.best.pvUci, 8) : ''; }

  dispose(): void {
    this.sync(null);
    this.sub?.unsubscribe();
    this.sub = null;
  }
}
