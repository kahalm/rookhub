import { TestBed } from '@angular/core/testing';
import { AnalysisEngineService, AnalysisState } from './analysis-engine.service';

const FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const tick = () => new Promise(r => setTimeout(r));

class FakeWorker {
  onerror: ((e: unknown) => void) | null = null;
  posted: string[] = [];
  terminated = false;
  private listeners: Array<(e: MessageEvent) => void> = [];
  postMessage(cmd: string) {
    this.posted.push(cmd);
    if (cmd === 'isready') this.emit('readyok');
  }
  addEventListener(_t: string, cb: (e: MessageEvent) => void) { this.listeners.push(cb); }
  removeEventListener(_t: string, cb: (e: MessageEvent) => void) { this.listeners = this.listeners.filter(l => l !== cb); }
  terminate() { this.terminated = true; }
  emit(data: string) { for (const l of [...this.listeners]) l({ data } as MessageEvent); }
  crash() { this.onerror?.({}); }
}

class TestEngine extends AnalysisEngineService {
  workers: FakeWorker[] = [];
  constructor(watchdogMs = 0) { super(); (this as any).watchdogMs = watchdogMs; }   // Watchdog standardmäßig aus
  protected override createWorker(): Worker {
    const w = new FakeWorker();
    this.workers.push(w);
    return w as unknown as Worker;
  }
  get last(): FakeWorker { return this.workers[this.workers.length - 1]; }
}

describe('AnalysisEngineService.parseInfo', () => {
  let svc: AnalysisEngineService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    svc = TestBed.inject(AnalysisEngineService);
  });

  it('parses a centipawn info line (white to move)', () => {
    const l = svc.parseInfo('info depth 20 seldepth 28 multipv 1 score cp 35 nodes 1 pv e2e4 e7e5 g1f3', 'w')!;
    expect(l).toBeTruthy();
    expect(l.multipv).toBe(1);
    expect(l.depth).toBe(20);
    expect(l.scoreType).toBe('cp');
    expect(l.score).toBe(35);
    expect(l.evalText).toBe('+0.35');
    expect(l.pvUci).toEqual(['e2e4', 'e7e5', 'g1f3']);
  });

  it('flips the score sign for black to move (→ white POV)', () => {
    const l = svc.parseInfo('info depth 18 multipv 2 score cp 40 pv d2d4', 'b')!;
    expect(l.multipv).toBe(2);
    expect(l.score).toBe(-40);
    expect(l.evalText).toBe('-0.40');
  });

  it('formats mate scores', () => {
    expect(svc.parseInfo('info depth 30 multipv 1 score mate 3 pv a1a8', 'w')!.evalText).toBe('#3');
    expect(svc.parseInfo('info depth 30 multipv 1 score mate 2 pv a1a8', 'b')!.evalText).toBe('#-2');
  });

  it('returns null without a pv', () => {
    expect(svc.parseInfo('info depth 5 score cp 10', 'w')).toBeNull();
  });
});

describe('AnalysisEngineService crash recovery', () => {
  it('recovers from a worker crash by re-analyzing the current position', async () => {
    const eng = new TestEngine();
    let state: AnalysisState = { fen: '', depth: 0, lines: [], running: false };
    eng.analysis$.subscribe(s => state = s);

    await eng.analyze(FEN);
    eng.last.emit('info depth 10 multipv 1 score cp 20 pv e2e4');
    expect(state.lines.length).toBe(1);

    const crashed = eng.last;
    crashed.crash();
    await tick;   // Re-Init + Re-Analyze (async)

    expect(crashed.terminated).toBeTrue();
    expect(eng.workers.length).toBe(2);            // frischer Worker
    eng.last.emit('info depth 12 multipv 1 score cp 25 pv d2d4');
    expect(state.running).toBeTrue();
    expect(state.lines[0].pvUci).toEqual(['d2d4']);
  });

  it('gives up after repeated crashes without progress (no infinite loop)', async () => {
    const eng = new TestEngine();
    let state: AnalysisState = { fen: '', depth: 0, lines: [], running: false };
    eng.analysis$.subscribe(s => state = s);

    await eng.analyze(FEN);
    for (let i = 0; i < 4; i++) { eng.last.crash(); await tick; }   // 4× crash ohne info

    expect(state.running).toBeFalse();
  });
});

describe('AnalysisEngineService stall watchdog', () => {
  it('reports a stall and recovers when no info arrives after go', async () => {
    const eng = new TestEngine(40);   // kurzer Watchdog
    const events: string[] = [];
    eng.reportEngineEvent = (kind) => events.push(kind);

    await eng.analyze(FEN);            // go gesendet (FakeWorker liefert KEINE info)
    expect(eng.workers.length).toBe(1);
    await new Promise(r => setTimeout(r, 80));   // Watchdog (40ms) feuert

    expect(events).toContain('stall');
    expect(eng.workers[0].terminated).toBeTrue();
    expect(eng.workers.length).toBeGreaterThanOrEqual(2);   // automatisch neu gestartet
    eng.destroy();                                          // Timer/Worker aufräumen
  });
});

describe('AnalysisEngineService UCI sequencing', () => {
  it('sends position/go only after stop + readyok (not mid-search)', async () => {
    const eng = new TestEngine();
    await eng.analyze(FEN);            // FakeWorker beantwortet isready automatisch mit readyok
    const posted = eng.last.posted;
    const lastStop = posted.lastIndexOf('stop');
    const lastReady = posted.lastIndexOf('isready');
    const iPos = posted.findIndex(c => c.startsWith('position fen'));
    const iGo = posted.findIndex(c => c.startsWith('go depth'));
    expect(lastReady).toBeGreaterThan(lastStop);   // erst stoppen, dann auf bereit warten
    expect(iPos).toBeGreaterThan(lastReady);        // Stellung erst NACH readyok
    expect(iGo).toBeGreaterThan(iPos);
  });
});
