import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { BehaviorSubject } from 'rxjs';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { AnalysisEngineService, AnalysisState } from '../analysis/analysis-engine.service';
import { MISTAKE_LIVE_ENGINE, MistakeLiveEvalComponent } from './mistake-live-eval.component';

/** Engine ohne Worker: merkt sich Aufträge, liefert Zustände auf Zuruf. */
export function fakeLiveEngine() {
  const state$ = new BehaviorSubject<AnalysisState>({ fen: '', depth: 0, lines: [], running: false, nodes: 0, nps: 0 });
  const fatal$ = new BehaviorSubject<string | null>(null);
  const engine = {
    analysis$: state$.asObservable(),
    engineFatalError$: fatal$.asObservable(),
    analyzed: [] as string[], multiPv: 0, depth: 0, stopped: 0, destroyed: 0,
    analyze(fen: string) { this.analyzed.push(fen); return Promise.resolve(); },
    setMultiPv(n: number) { this.multiPv = n; },
    setDepth(d: number) { this.depth = d; },
    stop() { this.stopped++; },
    destroy() { this.destroyed++; },
  };
  return { engine, state$, fatal$ };
}

describe('MistakeLiveEvalComponent', () => {
  // Nach 1.e4 e5 2.Qh5 — Schwarz am Zug.
  const NACH_QH5 = 'rnbqkbnr/pppp1ppp/8/4p2Q/4P3/8/PPPP1PPP/RNB1KBNR b KQkq - 1 2';

  function setup(factory: () => AnalysisEngineService | null) {
    TestBed.configureTestingModule({
      imports: [MistakeLiveEvalComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), { provide: MISTAKE_LIVE_ENGINE, useValue: factory }],
    });
    const tr = TestBed.inject(TranslateService);
    tr.setTranslation('en', { games: { mistakes: {
      liveEval: 'Weak analysis in the browser: {{eval}} (depth {{depth}})', liveEvalReply: 'reply {{move}}',
      liveEvalPending: 'Weak analysis in the browser is calculating …' } } });
    tr.use('en');
    const fixture = TestBed.createComponent(MistakeLiveEvalComponent);
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('rechnet die Stellung nach dem Zug, flach und mit einer Linie, und zeigt den Wert live in Weiß-Sicht samt Antwort', () => {
    const { engine, state$ } = fakeLiveEngine();
    const { fixture, el } = setup(() => engine as unknown as AnalysisEngineService);
    fixture.componentRef.setInput('fen', NACH_QH5);
    fixture.detectChanges();
    expect(engine.analyzed).toEqual([NACH_QH5]);
    expect(engine.multiPv).toBe(1);
    expect(engine.depth).toBe(MistakeLiveEvalComponent.DEPTH);
    expect(el.textContent).toContain('is calculating');

    state$.next({ fen: NACH_QH5, depth: 9, running: true, nodes: 1, nps: 1, lines: [
      { multipv: 1, depth: 9, scoreType: 'cp', score: 120, evalText: '+1.20', pvUci: ['b8c6', 'f1c4'] }] });
    fixture.detectChanges();
    expect(el.textContent!.replace(/\s+/g, ' ')).toContain('Weak analysis in the browser: +1.20 (depth 9) · reply 2... Nc6');

    state$.next({ fen: NACH_QH5, depth: 14, running: false, nodes: 1, nps: 1, lines: [
      { multipv: 1, depth: 14, scoreType: 'cp', score: -35, evalText: '-0.35', pvUci: ['g7g6'] }] });
    fixture.detectChanges();
    expect(el.textContent).toContain('-0.35 (depth 14)');
  });

  it('hält nach der Zeitgrenze an und beendet die Engine beim Verlassen', fakeAsync(() => {
    const { engine } = fakeLiveEngine();
    const { fixture } = setup(() => engine as unknown as AnalysisEngineService);
    fixture.componentRef.setInput('fen', NACH_QH5);
    fixture.detectChanges();
    tick(MistakeLiveEvalComponent.MAX_MS);
    expect(engine.stopped).toBe(1);
    fixture.destroy();
    expect(engine.destroyed).toBe(1);
  }));

  it('ohne Engine (z. B. kein WASM) oder bei Startfehler: keine Zeile', () => {
    const none = setup(() => null);
    none.fixture.componentRef.setInput('fen', NACH_QH5);
    none.fixture.detectChanges();
    expect(none.el.textContent!.trim()).toBe('');
    TestBed.resetTestingModule();

    const { engine, fatal$ } = fakeLiveEngine();
    const broken = setup(() => engine as unknown as AnalysisEngineService);
    broken.fixture.componentRef.setInput('fen', NACH_QH5);
    broken.fixture.detectChanges();
    fatal$.next('Stockfish init timeout');
    broken.fixture.detectChanges();
    expect(broken.el.textContent!.trim()).toBe('');
    expect(engine.destroyed).toBe(1);
  });
});
