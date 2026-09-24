import { BehaviorSubject } from 'rxjs';
import { AnalysisEngineService, AnalysisState } from '../analysis/analysis-engine.service';
import { LIVE_LINES, LiveEngineSession } from './live-engine-session';

/** Eine Engine ohne Worker: merkt sich, was sie rechnen soll, und liefert Zustände auf Zuruf. */
function fakeEngine() {
  const state$ = new BehaviorSubject<AnalysisState>({ fen: '', depth: 0, lines: [], running: false, nodes: 0, nps: 0 });
  const engine = {
    analysis$: state$.asObservable(),
    analyzed: [] as string[],
    multiPv: 0, depth: 0, remote: null as unknown, stopped: 0, destroyed: 0,
    analyze(fen: string) { this.analyzed.push(fen); return Promise.resolve(); },
    setMultiPv(n: number) { this.multiPv = n; },
    setDepth(d: number) { this.depth = d; },
    setRemoteEngine(info: unknown) { this.remote = info; },
    stop() { this.stopped++; },
    destroy() { this.destroyed++; },
  };
  return { engine, state$ };
}

describe('LiveEngineSession', () => {
  const start = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  const afterE4 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';
  const afterE4E5 = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2';

  function setup() {
    const { engine, state$ } = fakeEngine();
    const session = new LiveEngineSession(() => engine as unknown as AnalysisEngineService, 24);
    return { session, engine, state$ };
  }

  it('rechnet die Stellung der Partie — mit drei Linien und der Tiefe vom Analysebrett', () => {
    const { session, engine } = setup();
    session.sync(-1, start);
    session.sync(-1, start);   // keine Änderung → kein zweiter Auftrag
    expect(engine.analyzed).toEqual([start]);
    expect(engine.multiPv).toBe(LIVE_LINES);
    expect(engine.depth).toBe(24);
  });

  it('zeigt die Linien der gerechneten Stellung in SAN und den besten Zug als blauen Pfeil', () => {
    const { session, state$ } = setup();
    session.sync(-1, start);
    state$.next({ fen: start, depth: 18, running: true, nodes: 1, nps: 1, lines: [
      { multipv: 1, depth: 18, scoreType: 'cp', score: 30, evalText: '+0.30', pvUci: ['e2e4', 'e7e5'] },
      { multipv: 2, depth: 18, scoreType: 'cp', score: -10, evalText: '-0.10', pvUci: ['a2a3'] },
    ] });
    expect(session.depth()).toBe(18);
    expect(session.lines().map(l => l.san)).toEqual(['1. e4 e5', '1. a3']);
    expect(session.lines().map(l => l.positive)).toEqual([true, false]);
    expect(session.arrows()).toEqual([{ from: 'e2', to: 'e4', brush: 'blue' }]);

    // Zeilen einer überholten Stellung zählen nicht.
    state$.next({ fen: afterE4, depth: 30, running: true, nodes: 1, nps: 1, lines: [] });
    expect(session.depth()).toBe(18);
  });

  it('eigene Züge bilden eine Nebenvariante; Zug zurück und zurück zur Partie', () => {
    const { session, engine } = setup();
    session.sync(-1, start);
    session.play({ from: 'e2', to: 'e4', san: 'e4', fen: afterE4 }, start);
    session.play({ from: 'e7', to: 'e5', san: 'e5', fen: afterE4E5 }, start);
    expect(session.fen(start)).toBe(afterE4E5);
    expect(session.variationSan()).toBe('1. e4 e5');
    expect(session.lastMove()).toEqual(['e7', 'e5']);
    expect(engine.analyzed).toEqual([start, afterE4, afterE4E5]);

    session.undo(start);
    expect(session.fen(start)).toBe(afterE4);
    session.reset(start);
    expect(session.variation()).toEqual([]);
    expect(session.fen(start)).toBe(start);
    expect(engine.analyzed[engine.analyzed.length - 1]).toBe(start);
  });

  it('wer in der Partie blättert, verlässt die Nebenvariante', () => {
    const { session, engine } = setup();
    session.sync(-1, start);
    session.play({ from: 'd2', to: 'd4', san: 'd4', fen: 'rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR b KQkq - 0 1' }, start);
    session.sync(0, afterE4);   // die Partie steht jetzt nach 1.e4
    expect(session.variation()).toEqual([]);
    expect(engine.analyzed[engine.analyzed.length - 1]).toBe(afterE4);
  });

  it('externe Engine: rechnet die laufende Stellung neu und nennt ihren Namen; destroy beendet die Engine', () => {
    const { session, engine } = setup();
    session.sync(-1, start);
    session.useRemote({ id: 'eei_x', name: 'Cloud', maxThreads: 8, maxHash: 1024 }, () => { throw new Error('unbenutzt'); });
    expect(session.engineName()).toBe('Cloud');
    expect(engine.analyzed).toEqual([start, start]);
    session.destroy();
    expect(engine.destroyed).toBe(1);
  });
});
