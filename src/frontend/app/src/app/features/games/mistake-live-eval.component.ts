import { ChangeDetectionStrategy, Component, InjectionToken, OnChanges, OnDestroy, inject, input, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { TranslatePipe } from '@ngx-translate/core';
import { AnalysisEngineService } from '../analysis/analysis-engine.service';
import { uciLineToSan } from '../analysis/engine-lines.util';
import { formatEval } from './game-review.util';

/** Kann dieser Browser Stockfish (WASM im Worker) überhaupt rechnen? Sonst bleibt der Hinweis ohne Live-Wert. */
export function browserEngineAvailable(): boolean {
  return typeof Worker !== 'undefined' && typeof WebAssembly === 'object';
}

/**
 * Woher die Engine der Live-Bewertung kommt — eine EIGENE Instanz des Analyse-Dienstes (wie die Live-Engine der
 * Partieseite, `LiveEngineSession`), die mit der Anzeige stirbt. `null` = keine Engine in diesem Browser. In Tests ersetzt.
 */
export const MISTAKE_LIVE_ENGINE = new InjectionToken<() => AnalysisEngineService | null>('MISTAKE_LIVE_ENGINE', {
  providedIn: 'root',
  factory: () => () => browserEngineAvailable() ? new AnalysisEngineService() : null,
});

/** Was die Zeile gerade zeigt: Bewertung (Weiß-Sicht, Format wie die übrigen Werte), Tiefe, erste Antwort. */
interface LiveEvalView { evalText: string; depth: number; reply: string; }

/**
 * „Schwache Analyse im Browser" (1.000.0, Wunsch an Vereinspartie 260): steht der Fehlversuch im Fehler-Trainer NICHT
 * unter den Kandidaten der Analyse (Zeile `triedBound`), rechnet die Browser-Engine die Stellung NACH diesem Zug und
 * zeigt die Bewertung mitlaufend — ausdrücklich als flache Browser-Zahl benannt, damit sie nicht als Wert der tiefen
 * Server-Analyse gelesen wird (der Widerspruch von 0.972.1).
 *
 * Begrenzt: Tiefe {@link DEPTH} und höchstens {@link MAX_MS}, eine Linie. Neue Stellung → neue Suche; die Komponente
 * hängt an `@if` der Phase `wrong` — „Nochmal", nächste Aufgabe oder Verlassen zerstören sie und mit ihr den Worker.
 * Ohne Engine (kein Worker/WASM, Start gescheitert) zeigt sie nichts, der Hinweis darüber bleibt stehen.
 */
@Component({
  selector: 'app-mistake-live-eval',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  template: `
    @if (!failed()) {
      @if (view(); as v) {
        <span class="live-eval">{{ 'games.mistakes.liveEval' | translate: { eval: v.evalText, depth: v.depth } }}@if (v.reply) {<span class="reply"> · {{ 'games.mistakes.liveEvalReply' | translate: { move: v.reply } }}</span>}</span>
      } @else {
        <span class="live-eval pending">{{ 'games.mistakes.liveEvalPending' | translate }}</span>
      }
    }
  `,
  styles: [`
    :host { display: block; }
    .live-eval { font-variant-numeric: tabular-nums; }
  `],
})
export class MistakeLiveEvalComponent implements OnChanges, OnDestroy {
  /** Tiefe der Suche — flach genug fürs Handy (eine Linie, Sekundenbruchteile bis wenige Sekunden). */
  static readonly DEPTH = 14;
  /** Spätestens dann hält die Suche an, auch wenn die Tiefe noch nicht erreicht ist (schwache Geräte). */
  static readonly MAX_MS = 10_000;

  /** Stellung NACH dem Fehlversuch. */
  readonly fen = input.required<string>();

  readonly view = signal<LiveEvalView | null>(null);
  readonly failed = signal(false);

  private readonly createEngine = inject(MISTAKE_LIVE_ENGINE);
  private engine: AnalysisEngineService | null = null;
  private subs: Subscription[] = [];
  private timer?: ReturnType<typeof setTimeout>;
  private analyzedFen = '';

  ngOnChanges(): void {
    const fen = this.fen();
    if (!fen || fen === this.analyzedFen || this.failed()) return;
    if (!this.engine && !this.open()) return;
    this.analyzedFen = fen;
    this.view.set(null);
    this.clearTimer();
    this.engine!.analyze(fen).catch(() => this.fail());
    this.timer = setTimeout(() => { this.timer = undefined; this.engine?.stop(); }, MistakeLiveEvalComponent.MAX_MS);
  }

  ngOnDestroy(): void { this.close(); }

  private open(): boolean {
    let engine: AnalysisEngineService | null = null;
    try { engine = this.createEngine(); } catch { engine = null; }
    if (!engine) { this.failed.set(true); return false; }
    this.engine = engine;
    engine.setMultiPv(1);
    engine.setDepth(MistakeLiveEvalComponent.DEPTH);
    this.subs.push(engine.analysis$.subscribe(s => {
      const best = s.lines[0];
      if (!s.fen || s.fen !== this.analyzedFen || !best) return;
      const score = best.scoreType === 'mate' ? { mate: best.score } : { cp: best.score };
      this.view.set({
        evalText: formatEval(score),
        depth: s.depth,
        reply: uciLineToSan(s.fen, best.pvUci.slice(0, 1), 1),
      });
      if (!s.running && s.depth >= MistakeLiveEvalComponent.DEPTH) this.clearTimer();
    }));
    this.subs.push(engine.engineFatalError$.subscribe(e => { if (e) this.fail(); }));
    return true;
  }

  private fail(): void {
    this.failed.set(true);
    this.close();
  }

  private close(): void {
    this.clearTimer();
    for (const s of this.subs) s.unsubscribe();
    this.subs = [];
    const engine = this.engine;
    this.engine = null;
    if (engine) {
      try { engine.stop(); engine.destroy(); } catch { /* Worker schon weg */ }
    }
  }

  private clearTimer(): void {
    if (this.timer !== undefined) { clearTimeout(this.timer); this.timer = undefined; }
  }
}
