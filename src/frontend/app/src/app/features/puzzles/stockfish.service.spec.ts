import { StockfishService } from './stockfish.service';

const FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const tick = () => new Promise(r => setTimeout(r));

/**
 * Minimaler Web-Worker-Stub: antwortet auf 'isready' mit 'readyok' und – wenn `autoReply`
 * gesetzt ist – auf 'go ...' mit der vorgegebenen info+bestmove (deterministisch, der
 * runSearch-Handler ist beim Posten von 'go' bereits registriert).
 */
class FakeWorker {
  onerror: ((e: unknown) => void) | null = null;
  posted: string[] = [];
  terminated = false;
  autoReply: { info: string; bestmove: string } | null = null;
  private listeners: Array<(e: MessageEvent) => void> = [];
  postMessage(cmd: string) {
    this.posted.push(cmd);
    if (cmd === 'isready') this.emit('readyok');
    else if (cmd.startsWith('go') && this.autoReply) {
      this.emit(this.autoReply.info);
      this.emit(this.autoReply.bestmove);
    }
  }
  addEventListener(_t: string, cb: (e: MessageEvent) => void) { this.listeners.push(cb); }
  removeEventListener(_t: string, cb: (e: MessageEvent) => void) { this.listeners = this.listeners.filter(l => l !== cb); }
  terminate() { this.terminated = true; }
  emit(data: string) { for (const l of [...this.listeners]) l({ data } as MessageEvent); }
  crash() { this.onerror?.({}); }
}

class TestSf extends StockfishService {
  workers: FakeWorker[] = [];
  autoReply: { info: string; bestmove: string } | null = null;
  protected override createWorker(): Worker {
    const w = new FakeWorker();
    w.autoReply = this.autoReply;
    this.workers.push(w);
    return w as unknown as Worker;
  }
  get last(): FakeWorker { return this.workers[this.workers.length - 1]; }
}

describe('StockfishService crash recovery', () => {
  it('resolves an eval from info + bestmove', async () => {
    const sf = new TestSf();
    sf.autoReply = { info: 'info depth 8 score cp 30 pv e2e4', bestmove: 'bestmove e2e4' };
    expect(await sf.getEval(FEN, 8)).toBe('+0.3');
  });

  it('re-initializes with a fresh worker after a crash', async () => {
    const sf = new TestSf();
    await sf.init();
    expect(sf.workers.length).toBe(1);
    sf.last.crash();                       // Worker stirbt → handleCrash setzt zurück
    sf.autoReply = { info: 'info depth 8 score cp -50 pv e2e4', bestmove: 'bestmove e2e4' };
    const evalStr = await sf.getEval(FEN, 8);   // muss neu initialisieren
    expect(sf.workers.length).toBe(2);
    expect(sf.workers[0].terminated).toBeTrue();
    expect(evalStr).toBe('-0.5');
  });

  it('aborts the in-flight search immediately when the worker crashes', async () => {
    const sf = new TestSf();                // kein autoReply → Suche bleibt offen
    const p = sf.getEval(FEN, 12);
    await tick;
    sf.last.crash();
    await expectAsync(p).toBeRejected();
  });

  it('bricht beim destroy() eine laufende Suche sofort ab (statt 10 s zu hängen)', async () => {
    const sf = new TestSf();                // kein autoReply → Suche bleibt offen
    const p = sf.getEval(FEN, 12);
    await micro();
    sf.destroy();
    await expectAsync(p).toBeRejected();
  });
});

/** Mehrere Microtask-Runden abarbeiten (init/pending-Kette ist promise-getrieben). */
const micro = async (rounds = 20) => { for (let i = 0; i < rounds; i++) await Promise.resolve(); };

describe('StockfishService Suchtimeout', () => {
  beforeEach(() => jasmine.clock().install());
  afterEach(() => jasmine.clock().uninstall());

  it('hält den Kern per stop an und lässt die nächste Suche erst nach dem bestmove los', async () => {
    const sf = new TestSf();                    // kein autoReply → Suche läuft in den Timeout
    const p = sf.getEval(FEN, 12);
    await micro();
    jasmine.clock().tick(10000);
    await expectAsync(p).toBeRejectedWith('Stockfish timeout');

    const worker = sf.last;
    expect(worker.posted).toContain('stop');
    expect(worker.terminated).toBeFalse();      // erst mal nur anhalten, nicht wegwerfen

    // Die nächste Suche darf KEIN 'go' in den noch rechnenden Kern schicken (Asyncify-Crash).
    worker.autoReply = { info: 'info depth 8 score cp 30 pv e2e4', bestmove: 'bestmove e2e4' };
    const next = sf.getEval(FEN, 8);
    await micro();
    expect(worker.posted.filter(c => c.startsWith('go')).length).toBe(1);

    worker.emit('bestmove e2e4');                // Quittung des gestoppten Laufs
    await micro();
    expect(await next).toBe('+0.3');
    expect(worker.posted.filter(c => c.startsWith('go')).length).toBe(2);
  });

  it('entsorgt den Kern, wenn er das stop nicht quittiert', async () => {
    const sf = new TestSf();
    const p = sf.getEval(FEN, 12);
    await micro();
    jasmine.clock().tick(10000);
    await expectAsync(p).toBeRejected();
    const dead = sf.last;

    jasmine.clock().tick(2000);                  // keine bestmove-Quittung → harte Entsorgung
    await micro();
    expect(dead.terminated).toBeTrue();

    const fresh = sf.getEval(FEN, 8);
    await micro();
    expect(sf.workers.length).toBe(2);           // nächste Suche bekommt einen frischen Worker
    sf.last.emit('bestmove e2e4');               // der frische Kern antwortet
    await expectAsync(fresh).toBeResolved();
  });
});
