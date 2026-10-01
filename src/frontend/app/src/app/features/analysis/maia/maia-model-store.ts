import { MAIA_MODEL } from './maia-model';

/** Warum das Modell nicht kam: `unavailable` = der Server hat es nicht (falsche Größe, meist die
 *  index.html des SPA-Fallbacks), `failed` = Netz weg oder HTTP-Fehler. */
export type MaiaModelErrorReason = 'unavailable' | 'failed';

export class MaiaModelError extends Error {
  constructor(readonly reason: MaiaModelErrorReason, message?: string) {
    super(message ?? `maia model ${reason}`);
    this.name = 'MaiaModelError';
  }
}

/** Was ein Modell ausmacht: Adresse, Version (Cache-Schlüssel) und erwartete Größe. */
export interface MaiaModelSpec {
  readonly url: string;
  readonly version: string;
  readonly bytes: number;
}

/** Der Ausschnitt, den der Engine-Dienst braucht (Seam für Specs). */
export interface MaiaModelSource {
  /** Kann dieser Browser das Modell behalten? `false` ohne Cache API (kein sicherer Kontext — Dev läuft über
   *  HTTP) oder wenn sie sich gesperrt zeigte (Privatmodus, Speicher voll). Belastbar erst nach `isCached()`. */
  readonly canStore: boolean;
  isCached(): Promise<boolean>;
  load(onProgress?: (percent: number) => void): Promise<ArrayBuffer>;
}

/** Name des Cache-API-Speichers — EIN Speicher, darin höchstens EIN Modell (ältere Versionen fliegen raus). */
export const MAIA_CACHE_NAME = 'rookhub-maia';

/** `caches`, oder `null`, wenn der Browser ihn nicht hat (HTTP ohne localhost = kein sicherer Kontext,
 *  Dev läuft so) oder schon der Zugriff wirft. */
function globalCaches(): CacheStorage | null {
  try { return globalThis.caches ?? null; } catch { return null; }
}

/**
 * Lädt das Maia-Modell (45 MB) und legt es in der Cache API ab — mit eigenem Code und Fortschritt,
 * bewusst NICHT über den Angular-Service-Worker: der ngsw puffert eine Datei komplett, ohne dass die
 * Seite einen Fortschritt sähe, und das Modell steht deshalb in keiner seiner Gruppen.
 *
 * Regeln, die nicht kippen dürfen:
 * 1. **Die GRÖSSE entscheidet, nicht der Status.** Eine fehlende Datei unter /assets/ beantwortet der
 *    SPA-Fallback mit 200 und der index.html. Passt die Größe nicht → `unavailable`, und NICHTS kommt
 *    in den Cache (sonst hielte jeder spätere Besuch die index.html für das Modell).
 * 2. **Ein gesperrter Speicher ist kein Fehler.** Privatmodus, voller Speicher, kein sicherer Kontext:
 *    dann wird geladen und nur nicht gemerkt — nach außen geht nichts davon.
 * 3. Der Fortschritt rechnet mit der ERWARTETEN Größe, nicht mit Content-Length: ein komprimierender
 *    Proxy davor meldet dort die übertragene, nicht die ausgepackte Länge.
 */
export class MaiaModelStore implements MaiaModelSource {
  /** Schlüssel im Cache: die Version hängt dran, damit ein neuer Pin einen neuen Eintrag bedeutet. */
  readonly cacheKey: string;
  /** Die Cache API hat sich als gesperrt erwiesen (Öffnen oder Ablegen scheiterte). */
  private storeBroken = false;

  constructor(
    private readonly fetchFn: (input: string, init?: RequestInit) => Promise<Response> =
      (input, init) => globalThis.fetch(input, init),
    // `null` = keine Cache API. Bewusst nicht `undefined`: das löste den Vorgabewert aus (der echte Speicher).
    private readonly cachesApi: CacheStorage | null = globalCaches(),
    private readonly model: MaiaModelSpec = MAIA_MODEL,
  ) {
    this.cacheKey = `${model.url}?v=${model.version}`;
  }

  get canStore(): boolean {
    return !!this.cachesApi && !this.storeBroken;
  }

  /** Liegt das Modell schon auf diesem Gerät? (Ohne Cache API immer `false` — dann fragt die Oberfläche.) */
  async isCached(): Promise<boolean> {
    const cache = await this.openCache();
    if (!cache) return false;
    try { return !!(await cache.match(this.cacheKey)); } catch { return false; }
  }

  /** Aus dem Cache, sonst vom Server (mit Fortschritt in ganzen Prozent) — und danach gemerkt. */
  async load(onProgress?: (percent: number) => void): Promise<ArrayBuffer> {
    const cache = await this.openCache();
    const hit = cache ? await this.fromCache(cache) : null;
    if (hit) return hit;

    const buffer = await this.download(onProgress);
    if (cache) await this.remember(cache, buffer);
    return buffer;
  }

  private async openCache(): Promise<Cache | null> {
    if (!this.cachesApi) return null;
    try { return await this.cachesApi.open(MAIA_CACHE_NAME); }
    catch { this.storeBroken = true; return null; }
  }

  /** Treffer mit RICHTIGER Größe, sonst `null` — ein Eintrag mit falscher Größe wird dabei gelöscht. */
  private async fromCache(cache: Cache): Promise<ArrayBuffer | null> {
    try {
      const res = await cache.match(this.cacheKey);
      if (!res) return null;
      const buffer = await res.arrayBuffer();
      if (buffer.byteLength === this.model.bytes) return buffer;
      await cache.delete(this.cacheKey);
    } catch { /* gesperrter/kaputter Speicher → wie kein Treffer */ }
    return null;
  }

  private async download(onProgress?: (percent: number) => void): Promise<ArrayBuffer> {
    const expected = this.model.bytes;
    let res: Response;
    try { res = await this.fetchFn(this.model.url); }
    catch { throw new MaiaModelError('failed'); }
    if (!res.ok) throw new MaiaModelError('failed', `maia model HTTP ${res.status}`);

    let last = -1;
    const report = (received: number) => {
      const percent = Math.min(100, Math.floor((received * 100) / expected));
      if (percent !== last) { last = percent; onProgress?.(percent); }
    };
    report(0);

    // Ohne lesbaren Strom (manche Umgebungen) am Stück — dann eben ohne Zwischenstände.
    const reader = res.body?.getReader();
    if (!reader) {
      let whole: ArrayBuffer;
      try { whole = await res.arrayBuffer(); } catch { throw new MaiaModelError('failed'); }
      if (whole.byteLength !== expected) throw new MaiaModelError('unavailable');
      report(expected);
      return whole;
    }

    // In EINEN vorab angelegten Puffer lesen: kein Zusammenkleben am Ende (das hieße kurzzeitig 90 MB).
    const out = new Uint8Array(expected);
    let received = 0;
    try {
      for (;;) {
        const { done, value } = await reader.read();
        if (done) break;
        if (received + value.byteLength > expected) {
          // Mehr als das Modell: falsche Datei. Nicht weiterlesen, sonst wandert sie ganz in den Speicher.
          reader.cancel().catch(() => { /* egal */ });
          throw new MaiaModelError('unavailable');
        }
        out.set(value, received);
        received += value.byteLength;
        report(received);
      }
    } catch (e) {
      if (e instanceof MaiaModelError) throw e;
      throw new MaiaModelError('failed');
    }
    if (received !== expected) throw new MaiaModelError('unavailable');
    return out.buffer;
  }

  /** Ablegen und ältere Einträge (andere Version) wegräumen. Scheitert das, bleibt es beim Laden. */
  private async remember(cache: Cache, buffer: ArrayBuffer): Promise<void> {
    try {
      // `new Response(buffer)` KOPIERT die Bytes — der Aufrufer darf den Puffer danach weitergeben
      // (der Engine-Dienst transferiert ihn an den Worker).
      await cache.put(this.cacheKey, new Response(buffer, {
        headers: { 'Content-Type': 'application/octet-stream' },
      }));
    } catch {
      // Quota, Privatmodus: geladen ist es trotzdem — nur behalten kann der Browser es nicht.
      this.storeBroken = true;
      return;
    }
    try {
      const mine = keyOf(this.cacheKey);
      for (const req of await cache.keys()) {
        if (keyOf(req.url) !== mine) await cache.delete(req);
      }
    } catch { /* Aufräumen ist Kür — das neue Modell liegt schon */ }
  }
}

/** Pfad + Abfrage einer (relativen oder absoluten) Adresse — die Cache API führt absolute Adressen. */
function keyOf(url: string): string {
  try {
    const u = new URL(url, globalThis.location?.href ?? 'http://localhost/');
    return u.pathname + u.search;
  } catch { return url; }
}
