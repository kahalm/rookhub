import { Subject } from 'rxjs';
import { AnalysisCompare, AnalysisCompareHost, COMPARE_ENGINE_KEY, COMPARE_KEY } from './analysis-compare';

const FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('AnalysisCompare', () => {
  let built: any[];
  let host: AnalysisCompareHost & { selected: string };

  function fakeEngine(): any {
    const e: any = {
      analysis$: new Subject(), remoteFallback$: new Subject(), engineFatalError$: new Subject(),
      setDepth: jasmine.createSpy('setDepth'), setMultiPv: jasmine.createSpy('setMultiPv'),
      setRemoteEngine: jasmine.createSpy('setRemoteEngine'),
      analyze: jasmine.createSpy('analyze').and.returnValue(Promise.resolve()),
      destroy: jasmine.createSpy('destroy'),
    };
    built.push(e);
    return e;
  }

  function make(): AnalysisCompare {
    const cmp = new AnalysisCompare(host);
    cmp.createEngine = fakeEngine;
    return cmp;
  }

  beforeEach(() => {
    localStorage.removeItem(COMPARE_KEY);
    localStorage.removeItem(COMPARE_ENGINE_KEY);
    built = [];
    host = {
      selected: 'wasm',
      mainEngine: { reportEngineEvent: jasmine.createSpy('report') } as any,
      externalEngines: { analyse: jasmine.createSpy('analyse') } as any,
      depthSetting: () => 22, linesCount: () => 3,
      engines: () => [{ id: 'eei_a', name: 'PC' } as any],
      choices: () => [{ id: 'wasm', name: 'Browser', tag: null }, { id: 'eei_a', name: 'PC', tag: null }],
      selectedEngineId: () => host.selected, currentFen: () => FEN, canSearch: () => true,
      changed: jasmine.createSpy('changed'),
    };
  });
  afterEach(() => {
    localStorage.removeItem(COMPARE_KEY);
    localStorage.removeItem(COMPARE_ENGINE_KEY);
  });

  it('builds a second engine on a different engine than the main one and searches the current position', () => {
    const cmp = make();
    cmp.on = true;
    cmp.onToggle();
    expect(cmp.engineId).toBe('eei_a');
    expect(cmp.running).toBeTrue();
    expect(built.length).toBe(1);
    expect(built[0].setDepth).toHaveBeenCalledWith(22);
    expect(built[0].setMultiPv).toHaveBeenCalledWith(3);
    expect(built[0].analyze).toHaveBeenCalledWith(FEN);
    expect(localStorage.getItem(COMPARE_KEY)).toBe('1');
  });

  it('switches itself off when only the main engine is left', () => {
    host.engines = () => [];
    host.choices = () => [{ id: 'wasm', name: 'Browser', tag: null }];
    const cmp = make();
    cmp.on = true;
    cmp.onToggle();
    expect(cmp.on).toBeFalse();
    expect(built.length).toBe(0);
    expect(localStorage.getItem(COMPARE_KEY)).toBe('0');
  });

  it('drops lines of a position already left and names the browser engine after a fallback', () => {
    const cmp = make();
    cmp.on = true;
    cmp.onToggle();
    built[0].analysis$.next({ fen: '8/8/8/8/8/8/8/K6k w - - 0 1', depth: 20, lines: [], nps: 1 });
    expect(cmp.depth).toBe(0);
    expect(cmp.name('Browser')).toBe('PC');
    built[0].remoteFallback$.next(true);
    expect(cmp.name('Browser')).toBe('Browser');
  });

  it('tears the second engine down when switched off', () => {
    const cmp = make();
    cmp.on = true;
    cmp.onToggle();
    built[0].engineFatalError$.next('crash');
    expect(cmp.crashed).toBeTrue();
    cmp.on = false;
    cmp.onToggle();
    expect(built[0].destroy).toHaveBeenCalled();
    expect(cmp.engine).toBeUndefined();
    expect(cmp.crashed).toBeFalse();
  });
});
