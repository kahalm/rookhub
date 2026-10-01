import { MAIA_CACHE_NAME, MaiaModelError, MaiaModelSpec, MaiaModelStore } from './maia-model-store';

// Ein kleines Test-Modell statt 45 MB — die Regeln hängen an der GRÖSSE aus der Beschreibung, nicht an der Zahl.
const MODEL: MaiaModelSpec = { url: '/assets/maia/test.onnx', version: 'abc12345', bytes: 1000 };
const KEY = '/assets/maia/test.onnx?v=abc12345';

/** Pfad + Abfrage — die Cache API führt absolute Adressen, die Specs fragen nach dem relativen Schlüssel. */
const pathOf = (url: string) => { const u = new URL(url, location.href); return u.pathname + u.search; };

/** Bytes 0,1,2,… (mod 251), damit ein vertauschtes Stück auffiele. */
function bytesOf(n: number): Uint8Array<ArrayBuffer> {
  const out = new Uint8Array(n);
  for (let i = 0; i < n; i++) out[i] = i % 251;
  return out;
}

/** Antwort mit einem STROM aus Stücken dieser Größen (wie ein echter Download). */
function streamed(total: number, chunk: number, status = 200): Response {
  const data = bytesOf(total);
  return new Response(new ReadableStream<Uint8Array>({
    start(c) {
      for (let i = 0; i < total; i += chunk) c.enqueue(data.slice(i, Math.min(total, i + chunk)));
      c.close();
    },
  }), { status });
}

class FakeCache {
  readonly entries = new Map<string, ArrayBuffer>();
  async match(req: string): Promise<Response | undefined> {
    const buf = this.entries.get(pathOf(req));
    return buf ? new Response(buf.slice(0)) : undefined;
  }
  async put(req: string, res: Response): Promise<void> { this.entries.set(pathOf(req), await res.arrayBuffer()); }
  async delete(req: string | Request): Promise<boolean> { return this.entries.delete(pathOf(typeof req === 'string' ? req : req.url)); }
  async keys(): Promise<Request[]> { return [...this.entries.keys()].map(k => new Request(k)); }
}

class FakeCaches {
  readonly cache = new FakeCache();
  opened: string[] = [];
  async open(name: string): Promise<FakeCache> { this.opened.push(name); return this.cache; }
  asStorage(): CacheStorage { return this as unknown as CacheStorage; }
}

describe('MaiaModelStore', () => {
  let caches: FakeCaches;
  let fetchCalls: string[];

  function store(respond: () => Promise<Response>, cachesApi: CacheStorage | null = caches.asStorage()): MaiaModelStore {
    return new MaiaModelStore(async url => { fetchCalls.push(url); return respond(); }, cachesApi, MODEL);
  }

  beforeEach(() => {
    caches = new FakeCaches();
    fetchCalls = [];
  });

  it('builds the cache key from url and version', () => {
    expect(store(async () => streamed(1000, 1000)).cacheKey).toBe(KEY);
    // Ohne Angabe gilt das echte Modell.
    expect(new MaiaModelStore(async () => new Response(), null).cacheKey)
      .toMatch(/^\/assets\/maia\/maia3_simplified\.onnx\?v=[0-9a-f]{8}$/);
  });

  it('serves a cache hit without touching the network', async () => {
    caches.cache.entries.set(KEY, bytesOf(1000).buffer);
    const s = store(async () => { throw new Error('no network expected'); });

    expect(await s.isCached()).toBeTrue();
    const buf = await s.load();
    expect(buf.byteLength).toBe(1000);
    expect(new Uint8Array(buf)[250]).toBe(250 % 251);
    expect(fetchCalls).toEqual([]);
    expect(caches.opened.every(n => n === MAIA_CACHE_NAME)).toBeTrue();
  });

  it('downloads on a miss, reports whole percents 0…100 only on change, then stores it', async () => {
    const s = store(async () => streamed(1000, 250));
    expect(await s.isCached()).toBeFalse();

    const progress: number[] = [];
    const buf = await s.load(p => progress.push(p));

    expect(fetchCalls).toEqual(['/assets/maia/test.onnx']);
    expect(progress).toEqual([0, 25, 50, 75, 100]);
    expect(buf.byteLength).toBe(1000);
    expect(new Uint8Array(buf)[999]).toBe(999 % 251);
    expect(caches.cache.entries.get(KEY)?.byteLength).toBe(1000);
    expect(await s.isCached()).toBeTrue();
    expect(s.canStore).toBeTrue();
  });

  it('does not repeat a percent that a small chunk did not change', async () => {
    const progress: number[] = [];
    await store(async () => streamed(1000, 3)).load(p => progress.push(p));
    expect(progress.length).toBe(101);                  // 0 … 100, jede Zahl genau einmal
    expect(progress[0]).toBe(0);
    expect(progress[100]).toBe(100);
  });

  it('calls a wrong size "unavailable" and stores nothing (SPA fallback answers 200 with index.html)', async () => {
    const s = store(async () => new Response('<!doctype html><html></html>', { status: 200 }));
    await expectAsync(s.load()).toBeRejectedWith(jasmine.objectContaining({ reason: 'unavailable' }));
    expect(caches.cache.entries.size).toBe(0);
  });

  it('stops reading as soon as the body is larger than the model', async () => {
    const s = store(async () => streamed(5000, 600));
    await expectAsync(s.load()).toBeRejectedWith(jasmine.objectContaining({ reason: 'unavailable' }));
    expect(caches.cache.entries.size).toBe(0);
  });

  it('calls HTTP 500 "failed"', async () => {
    const s = store(async () => new Response('boom', { status: 500 }));
    const err = await s.load().then(() => null, e => e);
    expect(err instanceof MaiaModelError).toBeTrue();
    expect(err.reason).toBe('failed');
  });

  it('calls a network error "failed"', async () => {
    const s = store(async () => { throw new TypeError('Failed to fetch'); });
    await expectAsync(s.load()).toBeRejectedWith(jasmine.objectContaining({ reason: 'failed' }));
  });

  it('still loads when the Cache API throws — just without remembering', async () => {
    const broken = { open: () => Promise.reject(new DOMException('denied', 'SecurityError')) } as unknown as CacheStorage;
    const s = store(async () => streamed(1000, 500), broken);
    expect(s.canStore).toBeTrue();                 // vorher weiß er es nicht
    expect(await s.isCached()).toBeFalse();
    expect(s.canStore).toBeFalse();                // gesperrt gezeigt → behalten geht nicht
    expect((await s.load()).byteLength).toBe(1000);
  });

  it('still loads without any Cache API (no secure context)', async () => {
    const s = store(async () => streamed(1000, 1000), null);
    expect(s.canStore).toBeFalse();
    expect(await s.isCached()).toBeFalse();
    expect((await s.load()).byteLength).toBe(1000);
  });

  it('still loads when storing fails (quota)', async () => {
    caches.cache.put = () => Promise.reject(new DOMException('full', 'QuotaExceededError'));
    const s = store(async () => streamed(1000, 1000));
    expect((await s.load()).byteLength).toBe(1000);
    expect(s.canStore).toBeFalse();
  });

  it('removes an older entry of another version after storing', async () => {
    caches.cache.entries.set('/assets/maia/test.onnx?v=old00000', bytesOf(1000).buffer);
    await store(async () => streamed(1000, 1000)).load();
    expect([...caches.cache.entries.keys()]).toEqual([KEY]);
  });

  it('drops a cached entry of the wrong size and fetches again', async () => {
    caches.cache.entries.set(KEY, bytesOf(10).buffer);
    const buf = await store(async () => streamed(1000, 1000)).load();
    expect(fetchCalls.length).toBe(1);
    expect(buf.byteLength).toBe(1000);
    expect(caches.cache.entries.get(KEY)?.byteLength).toBe(1000);
  });
});
