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
});
