import { TestBed } from '@angular/core/testing';
import { BehaviorSubject, of } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { AnalysisEngineService, AnalysisState } from '@rh/features/analysis/analysis-engine.service';
import { ExternalEngineService } from '@rh/features/analysis/external-engine.service';
import { LiveEngineSession } from '@rh/features/games/live-engine-session';
import { BoardArrow } from '@rh/shared/pgn-viewer/chess-board.component';
import { SCAN_ENGINE_KEY, ScanEngineComponent, readScanEngineChoice } from './scan-engine.component';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const AFTER_E4 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';

function fakeEngine() {
  const state$ = new BehaviorSubject<AnalysisState>({ fen: '', depth: 0, lines: [], running: false, nodes: 0, nps: 0 });
  const engine = {
    analysis$: state$.asObservable(),
    analyzed: [] as string[],
    remote: null as { id: string } | null, destroyed: 0,
    analyze(fen: string) { this.analyzed.push(fen); return Promise.resolve(); },
    setMultiPv() { /* */ }, setDepth() { /* */ },
    setRemoteEngine(info: { id: string } | null) { this.remote = info; },
    remoteFallback$: new BehaviorSubject<boolean>(false),
    stop() { /* */ }, destroy() { this.destroyed++; },
  };
  return { engine, state$ };
}

describe('ScanEngineComponent', () => {
  let fake: ReturnType<typeof fakeEngine>;
  let arrows: BoardArrow[][];

  function create(loggedIn = true) {
    fake = fakeEngine();
    arrows = [];
    TestBed.configureTestingModule({
      imports: [ScanEngineComponent],
      providers: [
        { provide: AuthService, useValue: { get isLoggedIn() { return loggedIn; } } },
        { provide: ExternalEngineService, useValue: {
          listEngines: () => of({ hasCredentials: false, tokenInvalid: false, backgroundEngineIds: ['rhe_bg'], engines: [
            { id: 'rhe_lc0', name: 'RookHub Spark Lc0', maxThreads: 1, maxHash: 1, source: 'rookhub', online: true },
            { id: 'rhe_bg', name: 'Hintergrund 1', maxThreads: 1, maxHash: 1, source: 'rookhub', online: true },
            { id: 'rhe_off', name: 'PC', maxThreads: 1, maxHash: 1, source: 'rookhub', online: false },
          ] }),
          analyse: () => of(),
        } },
      ],
    });
    // Kein echter Stockfish: die Sitzung bekommt die Attrappe.
    spyOn(ScanEngineComponent.prototype as unknown as { createSession(): LiveEngineSession }, 'createSession')
      .and.callFake(() => new LiveEngineSession(() => fake.engine as unknown as AnalysisEngineService, 20));
    const f = TestBed.createComponent(ScanEngineComponent);
    f.componentRef.setInput('fen', START);
    f.componentInstance.arrowsChange.subscribe(a => arrows.push(a));
    f.detectChanges();
    return f;
  }

  beforeEach(() => localStorage.removeItem(SCAN_ENGINE_KEY));
  afterEach(() => localStorage.removeItem(SCAN_ENGINE_KEY));

  it('ist standardmäßig aus und rechnet nichts', () => {
    const f = create();
    expect(f.componentInstance.on()).toBeFalse();
    expect(f.componentInstance.session()).toBeNull();
    expect(fake.engine.analyzed).toEqual([]);
  });

  it('bietet die Engines ohne Hintergrund-Engines an, offline gesperrt', () => {
    const f = create();
    const options = Array.from(f.nativeElement.querySelectorAll('option') as NodeListOf<HTMLOptionElement>);
    expect(options.map(o => o.value)).toEqual(['wasm', 'rhe_lc0', 'rhe_off']);
    expect(options[2].disabled).toBeTrue();
  });

  it('eingeschaltet rechnet sie die Cursor-Stellung, folgt ihr und zeigt den besten Zug als Pfeil', () => {
    const f = create();
    f.componentInstance.toggle(true);
    f.detectChanges();
    expect(fake.engine.analyzed).toEqual([START]);
    expect(fake.engine.remote).toBeNull();
    fake.state$.next({ fen: START, depth: 12, running: true, nodes: 1, nps: 1, lines: [
      { multipv: 1, depth: 12, scoreType: 'cp', score: 30, evalText: '+0.30', pvUci: ['g1f3', 'g8f6'] },
    ] });
    f.detectChanges();
    expect(f.nativeElement.querySelector('.se-san').textContent).toContain('1. Sf3 Sf6');
    expect(arrows[arrows.length - 1]).toEqual([{ from: 'g1', to: 'f3', brush: 'blue' }]);
    f.componentRef.setInput('fen', AFTER_E4);
    f.detectChanges();
    expect(fake.engine.analyzed).toEqual([START, AFTER_E4]);
    expect(readScanEngineChoice()).toEqual({ on: true, engine: 'wasm' });
  });

  it('Engine-Wahl wird gemerkt und beim nächsten Öffnen wieder benutzt', () => {
    localStorage.setItem(SCAN_ENGINE_KEY, JSON.stringify({ on: true, engine: 'rhe_lc0' }));
    const f = create();
    expect(f.componentInstance.on()).toBeTrue();
    expect(fake.engine.remote?.id).toBe('rhe_lc0');
    f.componentInstance.choose('wasm');
    expect(fake.engine.remote).toBeNull();
    expect(readScanEngineChoice()).toEqual({ on: true, engine: 'wasm' });
  });

  it('ausschalten beendet die Engine und nimmt den Pfeil weg', () => {
    localStorage.setItem(SCAN_ENGINE_KEY, JSON.stringify({ on: true, engine: 'wasm' }));
    const f = create();
    f.componentInstance.toggle(false);
    f.detectChanges();
    expect(fake.engine.destroyed).toBe(1);
    expect(arrows[arrows.length - 1]).toEqual([]);
    expect(readScanEngineChoice().on).toBeFalse();
  });

  it('ohne Anmeldung nur Stockfish im Browser', () => {
    const f = create(false);
    const options = Array.from(f.nativeElement.querySelectorAll('option') as NodeListOf<HTMLOptionElement>);
    expect(options.map(o => o.value)).toEqual(['wasm']);
  });
});
