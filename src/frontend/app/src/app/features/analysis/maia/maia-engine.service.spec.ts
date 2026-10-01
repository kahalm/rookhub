import { MaiaEngineService, MaiaStatus } from './maia-engine.service';
import { MaiaModelError, MaiaModelSource } from './maia-model-store';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const AFTER_E4 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';
const FOOLS_MATE = 'rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3';

const flush = () => new Promise<void>(r => setTimeout(r));

/** Logits mit einem einzigen hohen Wert auf `index` (alles andere 0). */
function logitsWith(entries: Record<number, number>): ArrayBuffer {
  const out = new Float32Array(4352);
  for (const [i, v] of Object.entries(entries)) out[Number(i)] = v;
  return out.buffer;
}

/** Ein Worker zum Anfassen: merkt sich, was hineinging, und antwortet nur, wenn der Test es sagt. */
class FakeWorker {
  onmessage: ((e: MessageEvent) => void) | null = null;
  onerror: ((e: unknown) => void) | null = null;
  posted: { msg: any; transfer?: Transferable[] }[] = [];
  terminated = false;
  postMessage(msg: any, transfer?: Transferable[]) { this.posted.push({ msg, transfer }); }
  terminate() { this.terminated = true; }
  reply(data: unknown) { this.onmessage?.({ data } as MessageEvent); }
  crash() { this.onerror?.({}); }
  infers() { return this.posted.filter(p => p.msg.type === 'infer').map(p => p.msg); }
}

/** Ein Modell-Speicher zum Anfassen: `cached` und das Ergebnis von load() stellt der Test ein. */
class FakeStore implements MaiaModelSource {
  cached = true;
  canStore = true;
  isCachedCalls = 0;
  loadCalls = 0;
  progressSteps: number[] = [];
  failWith: MaiaModelError | null = null;
  model = new ArrayBuffer(16);
  async isCached() { this.isCachedCalls++; return this.cached; }
  async load(onProgress?: (p: number) => void) {
    this.loadCalls++;
    for (const p of this.progressSteps) { onProgress?.(p); await flush(); }
    if (this.failWith) throw this.failWith;
    return this.model;
  }
}

class TestMaia extends MaiaEngineService {
  workers: FakeWorker[] = [];
  readonly fakeStore = new FakeStore();
  throwOnCreate = false;
  dice = 0;
  constructor() {
    super();
    this.store = this.fakeStore;
    this.rng = () => this.dice;
  }
  setInitTimeout(ms: number) { this.initTimeoutMs = ms; }
  protected override createWorker(): Worker {
    if (this.throwOnCreate) throw new Error('no workers here');
    const w = new FakeWorker();
    this.workers.push(w);
    return w as unknown as Worker;
  }
  get last(): FakeWorker { return this.workers[this.workers.length - 1]; }
}

/** Bis zur Sitzung im Worker: prepare() → Init → ready. */
async function readyEngine(): Promise<TestMaia> {
  const maia = new TestMaia();
  const ready = maia.prepare();
  await flush();
  maia.last.reply({ type: 'ready' });
  expect(await ready).toBeTrue();
  return maia;
}

describe('MaiaEngineService', () => {
  it('loads from the cache: idle → loading → ready, the model is transferred to the worker', async () => {
    const maia = new TestMaia();
    expect(maia.status()).toBe('idle');

    const ready = maia.prepare();
    await flush();
    expect(maia.status()).toBe('loading');
    const init = maia.last.posted[0];
    expect(init.msg.type).toBe('init');
    expect(init.msg.model).toBe(maia.fakeStore.model);
    expect(init.transfer).toEqual([maia.fakeStore.model]);   // transferiert, nicht kopiert

    maia.last.reply({ type: 'ready' });
    expect(await ready).toBeTrue();
    expect(maia.status()).toBe('ready');
    expect(maia.error()).toBeNull();

    // Schon bereit: nichts Neues.
    expect(await maia.prepare()).toBeTrue();
    expect(maia.workers.length).toBe(1);
    expect(maia.fakeStore.loadCalls).toBe(1);
  });

  it('reports missing without loading, then downloads with progress on request', async () => {
    const maia = new TestMaia();
    maia.fakeStore.cached = false;
    maia.fakeStore.progressSteps = [0, 40, 100];

    expect(await maia.prepare()).toBeFalse();
    expect(maia.status()).toBe('missing');
    expect(maia.fakeStore.loadCalls).toBe(0);
    expect(maia.workers.length).toBe(0);

    const seen: [MaiaStatus, number][] = [];
    const done = maia.download();
    seen.push([maia.status(), maia.progress()]);
    for (let i = 0; i < 4; i++) { await flush(); seen.push([maia.status(), maia.progress()]); }
    expect(seen).toContain(['downloading', 0]);
    expect(seen).toContain(['downloading', 40]);
    expect(maia.status()).toBe('loading');

    maia.last.reply({ type: 'ready' });
    expect(await done).toBeTrue();
    expect(maia.status()).toBe('ready');
    expect(maia.progress()).toBe(100);
  });

  it('shares ONE running promise between concurrent prepare()/download() calls', async () => {
    const maia = new TestMaia();
    const a = maia.prepare();
    const b = maia.prepare();
    expect(b).toBe(a);
    const c = maia.download();
    await flush();
    maia.last.reply({ type: 'ready' });
    expect(await a).toBeTrue();
    expect(await c).toBeTrue();
    expect(maia.fakeStore.isCachedCalls).toBe(1);
    expect(maia.fakeStore.loadCalls).toBe(1);
    expect(maia.workers.length).toBe(1);
  });

  it('download() during a prepare() that ends in missing goes on to download', async () => {
    const maia = new TestMaia();
    maia.fakeStore.cached = false;
    const p = maia.prepare();
    const d = maia.download();
    expect(await p).toBeFalse();
    await flush(); await flush();
    expect(maia.fakeStore.loadCalls).toBe(1);
    maia.last.reply({ type: 'ready' });
    expect(await d).toBeTrue();
    expect(maia.status()).toBe('ready');
  });

  it('plays the highest legal logit when the dice say 0, sending the elo along', async () => {
    const maia = await readyEngine();
    maia.dice = 0;
    const move = maia.chooseMove(START, 1400);
    const req = maia.last.infers()[0];
    expect(req.elo).toBe(1400);
    expect(req.tokens.byteLength).toBe(768 * 4);
    // a1a1 (Index 0) ist nicht legal — darf trotz höchstem Wert nicht gewinnen.
    maia.last.reply({ type: 'result', id: req.id, logits: logitsWith({ 0: 50, 796: 4, 731: 3 }) });
    expect(await move).toBe('e2e4');
  });

  it('mirrors back for black to move: a logit on index 796 means e7e5', async () => {
    const maia = await readyEngine();
    const move = maia.chooseMove(AFTER_E4, 1600);
    const req = maia.last.infers()[0];
    maia.last.reply({ type: 'result', id: req.id, logits: logitsWith({ 796: 6 }) });
    expect(await move).toBe('e7e5');
  });

  it('assigns two parallel answers by id, not by order', async () => {
    const maia = await readyEngine();
    const white = maia.chooseMove(START, 1200);
    const black = maia.chooseMove(AFTER_E4, 2000);
    const [first, second] = maia.last.infers();
    expect(first.id).not.toBe(second.id);

    maia.last.reply({ type: 'result', id: second.id, logits: logitsWith({ 731: 6 }) });   // d7d5 (gespiegelt d2d4)
    maia.last.reply({ type: 'result', id: first.id, logits: logitsWith({ 405: 6 }) });    // g1f3
    expect(await white).toBe('g1f3');
    expect(await black).toBe('d7d5');
  });

  it('answers null without asking the worker when there is no legal move', async () => {
    const maia = await readyEngine();
    expect(await maia.chooseMove(FOOLS_MATE, 1600)).toBeNull();
    expect(maia.last.infers().length).toBe(0);
  });

  it('rejects chooseMove before ready', async () => {
    const maia = new TestMaia();
    await expectAsync(maia.chooseMove(START, 1600)).toBeRejected();
  });

  it('rejects an unreadable FEN without touching the session', async () => {
    const maia = await readyEngine();
    await expectAsync(maia.chooseMove('garbage', 1600)).toBeRejected();
    expect(maia.status()).toBe('ready');
  });

  it('a worker error ends the session: status error (failed), open requests rejected, worker dropped', async () => {
    const maia = await readyEngine();
    const pending = maia.chooseMove(START, 1600);
    const worker = maia.last;
    worker.crash();
    await expectAsync(pending).toBeRejected();
    expect(maia.status()).toBe('error');
    expect(maia.error()).toBe('failed');
    expect(worker.terminated).toBeTrue();
    await expectAsync(maia.chooseMove(START, 1600)).toBeRejected();
  });

  it('an error without id (session did not come up) fails the prepare', async () => {
    const maia = new TestMaia();
    const ready = maia.prepare();
    await flush();
    maia.last.reply({ type: 'error', message: 'wasm missing' });
    expect(await ready).toBeFalse();
    expect(maia.status()).toBe('error');
    expect(maia.error()).toBe('failed');
    expect(maia.last.terminated).toBeTrue();
  });

  it('an error WITH id rejects only that request, the session stays ready', async () => {
    const maia = await readyEngine();
    const bad = maia.chooseMove(START, 1600);
    const good = maia.chooseMove(START, 1600);
    const [r1, r2] = maia.last.infers();
    maia.last.reply({ type: 'error', id: r1.id, message: 'boom' });
    maia.last.reply({ type: 'result', id: r2.id, logits: logitsWith({ 796: 5 }) });
    await expectAsync(bad).toBeRejected();
    expect(await good).toBe('e2e4');
    expect(maia.status()).toBe('ready');
  });

  it('a throwing Worker constructor ends in error (failed)', async () => {
    const maia = new TestMaia();
    maia.throwOnCreate = true;
    expect(await maia.prepare()).toBeFalse();
    expect(maia.status()).toBe('error');
    expect(maia.error()).toBe('failed');
  });

  it('passes "unavailable" through from the model store', async () => {
    const maia = new TestMaia();
    maia.fakeStore.cached = false;
    maia.fakeStore.failWith = new MaiaModelError('unavailable');
    expect(await maia.download()).toBeFalse();
    expect(maia.status()).toBe('error');
    expect(maia.error()).toBe('unavailable');
    expect(maia.workers.length).toBe(0);
  });

  it('release() ends the worker, rejects open requests and goes back to idle', async () => {
    const maia = await readyEngine();
    const pending = maia.chooseMove(START, 1600);
    const worker = maia.last;
    maia.release();
    await expectAsync(pending).toBeRejected();
    expect(worker.terminated).toBeTrue();
    expect(maia.status()).toBe('idle');
    // Eine späte Antwort des alten Workers bleibt ohne Wirkung.
    worker.reply({ type: 'ready' });
    expect(maia.status()).toBe('idle');

    // Wieder hochfahren: aus dem Cache, neuer Worker.
    const again = maia.prepare();
    await flush();
    expect(maia.workers.length).toBe(2);
    maia.last.reply({ type: 'ready' });
    expect(await again).toBeTrue();
  });

  it('release() during loading resolves the prepare with false and starts no worker', async () => {
    const maia = new TestMaia();
    maia.fakeStore.progressSteps = [10, 20];   // load() braucht ein paar Takte
    const ready = maia.prepare();
    await flush();
    maia.release();
    expect(await ready).toBeFalse();
    await flush(); await flush();
    expect(maia.workers.length).toBe(0);
    expect(maia.status()).toBe('idle');
  });
  it('reports whether the browser can keep the model (no Cache API on Dev over HTTP)', async () => {
    const maia = new TestMaia();
    maia.fakeStore.cached = false;
    maia.fakeStore.canStore = false;
    expect(await maia.prepare()).toBeFalse();
    expect(maia.status()).toBe('missing');
    expect(maia.canStore()).toBeFalse();

    const other = new TestMaia();
    other.fakeStore.cached = false;
    await other.prepare();
    expect(other.canStore()).toBeTrue();
  });

  it('a worker that never answers init fails the session after initTimeoutMs', async () => {
    const maia = new TestMaia();
    maia.setInitTimeout(20);
    const ready = maia.prepare();
    await flush();
    expect(maia.status()).toBe('loading');
    expect(await ready).toBeFalse();                  // die Frist löst das Init mit false auf
    expect(maia.status()).toBe('error');
    expect(maia.error()).toBe('failed');
    expect(maia.last.terminated).toBeTrue();
  });

  it('ready before the deadline: no error afterwards', async () => {
    const maia = new TestMaia();
    maia.setInitTimeout(20);
    const ready = maia.prepare();
    await flush();
    maia.last.reply({ type: 'ready' });
    expect(await ready).toBeTrue();
    await new Promise(r => setTimeout(r, 50));
    expect(maia.status()).toBe('ready');
    expect(maia.last.terminated).toBeFalse();
  });

  it('release() before the deadline: the old timer does not hit the next worker', async () => {
    const maia = new TestMaia();
    maia.setInitTimeout(30);
    const first = maia.prepare();
    await flush();
    maia.release();
    expect(await first).toBeFalse();
    // Gleich wieder hochfahren — mit großzügiger Frist; der Timer des ersten Workers darf hier nichts mehr tun.
    maia.setInitTimeout(10_000);
    const second = maia.prepare();
    await flush();
    await new Promise(r => setTimeout(r, 60));
    expect(maia.status()).toBe('loading');
    maia.last.reply({ type: 'ready' });
    expect(await second).toBeTrue();
    expect(maia.status()).toBe('ready');
    maia.release();                                    // räumt auch die lange Frist ab
  });
});
