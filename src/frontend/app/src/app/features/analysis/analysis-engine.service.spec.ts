import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';
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

  it('gives up immediately on a repeat crash of the SAME position (no WASM re-instantiation thrash)', async () => {
    const eng = new TestEngine();
    let state: AnalysisState = { fen: '', depth: 0, lines: [], running: false };
    eng.analysis$.subscribe(s => state = s);

    await eng.analyze(FEN);            // Worker 1
    eng.last.crash(); await tick;      // 1. Crash → genau EIN sauberer Neustart
    expect(eng.workers.length).toBe(2);

    eng.last.crash(); await tick;      // 2. Crash auf DERSELBEN Stellung → aufgeben, KEIN Worker 3
    expect(eng.workers.length).toBe(2);
    expect(state.running).toBeFalse();
  });

  it('includes the crashing FEN in the crash telemetry (so it can be reproduced)', async () => {
    const eng = new TestEngine();
    const details: string[] = [];
    eng.reportEngineEvent = (_kind, detail) => details.push(detail ?? '');

    await eng.analyze(FEN);
    eng.last.crash();

    expect(details.some(d => d.includes(FEN))).toBeTrue();
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
  const FEN2 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';

  it('starts the first search directly with position + go', async () => {
    const eng = new TestEngine();
    await eng.analyze(FEN);
    const posted = eng.last.posted;
    const iPos = posted.findIndex(c => c.startsWith('position fen'));
    const iGo = posted.findIndex(c => c.startsWith('go depth'));
    expect(iPos).toBeGreaterThan(-1);
    expect(iGo).toBeGreaterThan(iPos);              // position vor go
  });

  it('defers the next search until the running one reports bestmove (no go mid-search → no WASM trap)', async () => {
    const eng = new TestEngine();
    await eng.analyze(FEN);                          // 1. Suche läuft (go gesendet)
    eng.last.emit('info depth 10 multipv 1 score cp 20 pv e2e4');
    const posted = eng.last.posted;

    await eng.analyze(FEN2);                         // Navigation, während die 1. Suche noch läuft
    expect(posted.filter(c => c.startsWith('go depth')).length).toBe(1);   // KEIN zweites go
    expect(posted.includes('stop')).toBeTrue();                            // stattdessen erst stop
    expect(posted.some(c => c.startsWith('position fen ' + FEN2))).toBeFalse();

    eng.last.emit('bestmove e2e4');                 // 1. Suche sauber beendet → Gate öffnet
    expect(posted.filter(c => c.startsWith('go depth')).length).toBe(2);   // jetzt erst das 2. go
    expect(posted.some(c => c.startsWith('position fen ' + FEN2))).toBeTrue();
    const iStop = posted.lastIndexOf('stop');
    const iPos2 = posted.indexOf('position fen ' + FEN2);
    expect(iPos2).toBeGreaterThan(iStop);           // 2. position erst NACH stop
  });

  it('coalesces rapid navigation to the latest position (only the last pending search runs)', async () => {
    const eng = new TestEngine();
    const FEN3 = '8/8/8/8/8/8/8/k1K5 w - - 0 1';
    await eng.analyze(FEN);
    eng.last.emit('info depth 8 multipv 1 score cp 10 pv e2e4');
    const posted = eng.last.posted;

    await eng.analyze(FEN2);                         // verworfen
    await eng.analyze(FEN3);                         // gewinnt
    expect(posted.filter(c => c.startsWith('go depth')).length).toBe(1);   // noch nichts Neues gestartet

    eng.last.emit('bestmove e2e4');                 // Gate öffnet → nur die ZULETZT gewünschte Stellung
    const positions = posted.filter(c => c.startsWith('position fen'));
    expect(positions[positions.length - 1]).toBe('position fen ' + FEN3);
    expect(posted.some(c => c.startsWith('position fen ' + FEN2))).toBeFalse();
    expect(posted.filter(c => c.startsWith('go depth')).length).toBe(2);
  });
});

describe('AnalysisEngineService external engine (remote)', () => {
  const ENGINE = { id: 'eei_a', name: 'SF Heim-PC', maxThreads: 4, maxHash: 256 };
  const line = (depth: number, cp: number, moves: string[]) =>
    ({ time: 1, depth, nodes: 10, pvs: [{ depth, cp, moves }] });

  it('analyses via the remote transport instead of the WASM worker', async () => {
    const eng = new TestEngine();
    let state: AnalysisState = { fen: '', depth: 0, lines: [], running: false };
    eng.analysis$.subscribe(s => state = s);
    const subjects: Subject<any>[] = [];
    let sentWork: any = null;

    eng.setRemoteEngine(ENGINE, (_id, work) => {
      sentWork = work;
      const s = new Subject<any>();
      subjects.push(s);
      return s.asObservable();
    });
    await eng.analyze(FEN);

    expect(eng.workers.length).toBe(0);          // kein WASM-Worker gestartet
    expect(sentWork.depth).toBe(22);
    expect(sentWork.threads).toBe(4);            // Engine-Maximum übernommen
    expect(sentWork.initialFen).toBe(FEN);

    subjects[0].next(line(14, 45, ['e2e4', 'e7e5']));
    expect(state.running).toBeTrue();
    expect(state.depth).toBe(14);
    expect(state.lines[0].evalText).toBe('+0.45');
    expect(state.lines[0].pvUci).toEqual(['e2e4', 'e7e5']);
  });

  it('falls back to WASM when the remote analysis fails BEFORE any data', async () => {
    const eng = new TestEngine();
    let state: AnalysisState = { fen: '', depth: 0, lines: [], running: false };
    eng.analysis$.subscribe(s => state = s);
    let fallback = false;
    eng.remoteFallback$.subscribe(f => fallback = f);
    const subjects: Subject<any>[] = [];

    eng.setRemoteEngine(ENGINE, () => { const s = new Subject<any>(); subjects.push(s); return s.asObservable(); });
    await eng.analyze(FEN);
    expect(eng.workers.length).toBe(0);

    subjects[0].error(new Error('offline'));
    await tick();

    expect(fallback).toBeTrue();
    expect(eng.workers.length).toBe(1);          // lokaler Worker übernimmt
    eng.last.emit('info depth 10 multipv 1 score cp 20 pv e2e4');
    expect(state.lines[0].pvUci).toEqual(['e2e4']);
  });

  it('keeps the results and does NOT fall back when the stream dies mid-search', async () => {
    const eng = new TestEngine();
    let state: AnalysisState = { fen: '', depth: 0, lines: [], running: false };
    eng.analysis$.subscribe(s => state = s);
    let fallback = false;
    eng.remoteFallback$.subscribe(f => fallback = f);
    const subjects: Subject<any>[] = [];

    eng.setRemoteEngine(ENGINE, () => { const s = new Subject<any>(); subjects.push(s); return s.asObservable(); });
    await eng.analyze(FEN);
    subjects[0].next(line(18, 60, ['d2d4']));
    subjects[0].error(new Error('stream aborted'));
    await tick();

    expect(fallback).toBeFalse();
    expect(eng.workers.length).toBe(0);          // kein WASM-Neustart
    expect(state.running).toBeFalse();           // Suche gilt als beendet
    expect(state.lines[0].pvUci).toEqual(['d2d4']);   // Ergebnis bleibt stehen
  });

  it('drops stale lines after switching position (generation guard)', async () => {
    const eng = new TestEngine();
    let state: AnalysisState = { fen: '', depth: 0, lines: [], running: false };
    eng.analysis$.subscribe(s => state = s);
    const subjects: Subject<any>[] = [];
    const FEN2 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';

    eng.setRemoteEngine(ENGINE, () => { const s = new Subject<any>(); subjects.push(s); return s.asObservable(); });
    await eng.analyze(FEN);
    await eng.analyze(FEN2);

    subjects[0].next(line(20, 99, ['a2a3']));   // Antwort der ALTEN Stellung
    expect(state.fen).toBe(FEN2);
    expect(state.lines.length).toBe(0);
  });

  it('maps mate scores and caps the pv list at the requested MultiPV', () => {
    const eng = new TestEngine();
    eng.setMultiPv(2);
    const mapped = eng.mapRemoteLine({
      time: 1, depth: 30, nodes: 5,
      pvs: [
        { depth: 30, mate: 3, moves: ['a1a8'] },
        { depth: 30, cp: -150, moves: ['b1b8'] },
        { depth: 30, cp: 10, moves: ['c1c8'] },
      ],
    });
    expect(mapped.length).toBe(2);
    expect(mapped[0].evalText).toBe('#3');
    expect(mapped[0].scoreType).toBe('mate');
    expect(mapped[1].evalText).toBe('-1.50');
    expect(mapped[1].multipv).toBe(2);
  });

  it('stops the remote search on stop() and destroy() (unsubscribes the stream)', async () => {
    const eng = new TestEngine();
    let unsubscribed = 0;
    eng.setRemoteEngine(ENGINE, () => new Observable(() => () => { unsubscribed++; }));

    await eng.analyze(FEN);
    eng.stop();
    expect(unsubscribed).toBe(1);

    await eng.analyze(FEN);
    eng.destroy();
    expect(unsubscribed).toBe(2);
  });

  it('setRemoteEngine(null) returns to the WASM worker', async () => {
    const eng = new TestEngine();
    eng.setRemoteEngine(ENGINE, () => new Subject<any>().asObservable());
    await eng.analyze(FEN);
    expect(eng.workers.length).toBe(0);

    eng.setRemoteEngine(null);
    await eng.analyze(FEN);
    expect(eng.workers.length).toBe(1);
  });
});

describe('AnalysisEngineService settings setters', () => {
  it('setMultiPv/setDepth update settings WITHOUT auto-triggering a new search', async () => {
    const eng = new TestEngine();
    eng.analysis$.subscribe();
    await eng.analyze(FEN);
    await tick();
    const gosBefore = eng.last.posted.filter(c => c.startsWith('go depth')).length;

    eng.setMultiPv(3);
    eng.setDepth(20);
    await tick();

    // Genau ein analyze() pro Änderung kommt vom Aufrufer (Component) — der Setter selbst feuert keins.
    expect(eng.last.posted.filter(c => c.startsWith('go depth')).length).toBe(gosBefore);
  });
});

// Regressionen aus dem adversarialen Review von v0.372.0: beim Umschalten auf die externe Engine
// darf der WASM-Kern weder weiterrechnen noch die fremde Suche mit seinen Altzeilen überschreiben.
describe('AnalysisEngineService remote/local isolation', () => {
  const ENGINE = { id: 'eei_a', name: 'SF', maxThreads: 2, maxHash: 64 };
  const remote = () => { const s = new Subject<any>(); return { s, t: () => s.asObservable() }; };

  it('stops the running WASM search when an external engine takes over', async () => {
    const eng = new TestEngine();
    await eng.analyze(FEN);                       // WASM sucht
    const worker = eng.last;
    expect(worker.posted.some((c: string) => c.startsWith('go depth'))).toBeTrue();

    const r = remote();
    eng.setRemoteEngine(ENGINE, r.t);
    expect(worker.posted.includes('stop')).toBeTrue();   // Kern rechnet nicht weiter
  });

  it('ignores stale WASM info lines once the external engine is analysing', async () => {
    const eng = new TestEngine();
    let state: AnalysisState = { fen: '', depth: 0, lines: [], running: false };
    eng.analysis$.subscribe(s => state = s);

    await eng.analyze(FEN);
    const worker = eng.last;
    const r = remote();
    eng.setRemoteEngine(ENGINE, r.t);
    await eng.analyze(FEN);                       // jetzt remote
    r.s.next({ time: 1, depth: 20, nodes: 5, pvs: [{ depth: 20, cp: 80, moves: ['d2d4'] }] });
    expect(state.lines[0].pvUci).toEqual(['d2d4']);

    // Nachzügler des lokalen Kerns — dürfen die Remote-Anzeige NICHT überschreiben.
    worker.emit('info depth 30 multipv 1 score cp -500 pv h2h4');
    expect(state.lines[0].pvUci).toEqual(['d2d4']);
    expect(state.running).toBeTrue();

    // ... und sein spätes bestmove darf die laufende Remote-Suche nicht für beendet erklären.
    worker.emit('bestmove h2h4');
    expect(state.running).toBeTrue();
  });

  it('drops WASM info lines of a position the user already left', async () => {
    const eng = new TestEngine();
    let state: AnalysisState = { fen: '', depth: 0, lines: [], running: false };
    eng.analysis$.subscribe(s => state = s);
    const FEN2 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';

    await eng.analyze(FEN);
    eng.last.emit('info depth 10 multipv 1 score cp 20 pv e2e4');
    await eng.analyze(FEN2);                      // Stellungswechsel (stop → bestmove → neues go)
    eng.last.emit('bestmove e2e4');               // startet die pending-Suche für FEN2

    eng.last.emit('info depth 12 multipv 1 score cp 15 pv d7d5');
    expect(state.fen).toBe(FEN2);
    expect(state.lines[0].pvUci).toEqual(['d7d5']);
  });

  it('keeps the remote engine when the list arrives while the WASM handshake is still running', async () => {
    // ngOnInit-Rennen: analyze() startet lokal, WÄHREND es auf `readyok` wartet schaltet die
    // eintreffende Engine-Liste auf remote. Die fortgesetzte lokale Suche darf sie nicht verdrängen.
    class SlowInitEngine extends TestEngine {
      override init(): Promise<void> {
        return new Promise<void>(resolve => setTimeout(() => super.init().then(resolve), 5));
      }
    }
    const eng = new SlowInitEngine();
    let state: AnalysisState = { fen: '', depth: 0, lines: [], running: false };
    eng.analysis$.subscribe(s => state = s);

    const pending = eng.analyze(FEN);             // lokal gestartet, hängt im init
    const r = remote();
    eng.setRemoteEngine(ENGINE, r.t);             // Liste kommt an → remote
    await pending;

    r.s.next({ time: 1, depth: 16, nodes: 9, pvs: [{ depth: 16, cp: 40, moves: ['c2c4'] }] });
    expect(state.lines[0].pvUci).toEqual(['c2c4']);
    expect(state.running).toBeTrue();
  });

  it('normalises king-takes-rook castling coming from the broker', () => {
    const eng = new TestEngine();
    (eng as any).currentFen = 'rnbqk2r/pppp1ppp/5n2/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4';
    const mapped = eng.mapRemoteLine({
      time: 1, depth: 18, nodes: 100,
      pvs: [{ depth: 18, cp: 25, moves: ['e1h1', 'e8h8'] }],
    });
    expect(mapped[0].pvUci).toEqual(['e1g1', 'e8g8']);
  });

  it('releases the remote transport on destroy (no reference to the destroyed page)', async () => {
    const eng = new TestEngine();
    const r = remote();
    eng.setRemoteEngine(ENGINE, r.t);
    eng.destroy();

    await eng.analyze(FEN);                       // muss wieder lokal laufen
    expect(eng.workers.length).toBe(1);
  });
});
