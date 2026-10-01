import { Injectable, OnDestroy, Signal, signal } from '@angular/core';
import { encodePosition, MaiaEncoding, pickMove, policyFromLogits } from './maia-encoding';
import { MaiaModelError, MaiaModelErrorReason, MaiaModelSource, MaiaModelStore } from './maia-model-store';

/** `missing` = das Modell liegt noch nicht auf diesem Gerät (die Oberfläche fragt, bevor 46 MB laden). */
export type MaiaStatus = 'idle' | 'missing' | 'downloading' | 'loading' | 'ready' | 'error';

/** Was aus dem Worker zurückkommt (siehe maia-worker.js). */
type WorkerReply =
  | { type: 'ready' }
  | { type: 'result'; id: number; logits: ArrayBuffer }
  | { type: 'error'; id?: number; message: string };

interface PendingInference {
  resolve: (logits: Float32Array) => void;
  reject: (reason: unknown) => void;
}

/**
 * Maia-3 (menschenähnliche Engine) im Browser: lädt das Modell (`MaiaModelStore`), hält EINEN Worker
 * mit der ONNX-Sitzung und liefert je Stellung einen gewürfelten Zug in der gewählten Stärke.
 *
 * Wie `AnalysisEngineService` ohne DI-Abhängigkeiten, damit Specs ihn mit `new` bauen und über die
 * Seams (`createWorker`, `store`, `rng`) füttern können.
 *
 * Regeln, die nicht kippen dürfen:
 * 1. **Zustand ausschließlich in Signalen** — Angular 22 zeichnet nach einer asynchronen Antwort sonst
 *    nicht neu, und der Fortschritt bliebe bei 0 stehen.
 * 2. **Gleichzeitige `prepare()`/`download()` teilen sich EIN laufendes Promise** — ein Doppelklick
 *    lädt nicht zweimal 45 MB und baut keine zweite Sitzung.
 * 3. **Das Modell wird an den Worker TRANSFERIERT**, nicht kopiert: ein zweites 45-MB-Exemplar im
 *    Hauptthread bliebe sonst bis zur nächsten Speicherbereinigung liegen.
 * 4. **Antworten werden über die laufende `id` zugeordnet**, nie über die Reihenfolge — zwei schnell
 *    hintereinander gestellte Anfragen dürfen sich nicht vertauschen.
 * 5. **Ein Fehler der SITZUNG** (Worker stirbt, Init scheitert oder bleibt länger als `initTimeoutMs` stumm)
 *    beendet alles: Status `error`, offene
 *    Anfragen abgelehnt, Worker verworfen. Ein Fehler EINER Anfrage lehnt nur diese ab.
 */
@Injectable({ providedIn: 'root' })
export class MaiaEngineService implements OnDestroy {
  private readonly statusSig = signal<MaiaStatus>('idle');
  private readonly progressSig = signal(0);
  private readonly errorSig = signal<MaiaModelErrorReason | null>(null);
  private readonly canStoreSig = signal(true);

  readonly status: Signal<MaiaStatus> = this.statusSig.asReadonly();
  /** 0..100, nur während `downloading` von Bedeutung. */
  readonly progress: Signal<number> = this.progressSig.asReadonly();
  readonly error: Signal<MaiaModelErrorReason | null> = this.errorSig.asReadonly();
  /** Kann dieser Browser das Modell behalten? Ohne Cache API (Dev über HTTP = kein sicherer Kontext) oder bei
   *  gesperrtem Speicher nicht — dann darf die Oberfläche nicht „bleibt auf diesem Gerät" versprechen. Belastbar
   *  nach dem ersten `prepare()` (vorher `true`); `missing` kommt immer erst danach. */
  readonly canStore: Signal<boolean> = this.canStoreSig.asReadonly();

  /** Woher das Modell kommt (Seam für Specs). */
  protected store: MaiaModelSource = new MaiaModelStore();
  /** Würfel der Zugwahl (Seam für Specs: ein fester Wert macht die Wahl vorhersagbar). */
  protected rng: () => number = Math.random;
  /** So lange darf der Worker nach `init` für `ready` brauchen (gemessen ~1,2 s) — danach gilt die Sitzung als
   *  gescheitert. Ohne Frist hinge die Karte für immer bei „Modell wird vorbereitet …", wenn der Worker stumm
   *  bleibt (Feld statt Konstante: Specs drehen es klein). */
  protected initTimeoutMs = 60_000;

  private worker?: Worker;
  private pending = new Map<number, PendingInference>();
  private nextId = 1;
  /** Das EINE laufende prepare()/download() (Regel 2). */
  private running?: Promise<boolean>;
  /** Löst das laufende Init auf — gesetzt, solange die Sitzung im Worker entsteht. */
  private initDone?: (ok: boolean) => void;
  /** Frist des laufenden Init. Wird bei `ready`, bei jedem Fehler und in `release()` gelöscht — kein Timer darf
   *  einen NEUEREN Worker treffen. */
  private initTimer?: ReturnType<typeof setTimeout>;
  /** Jedes `release()` zählt hoch: ein Laden, das danach zurückkommt, startet keinen Worker mehr. */
  private generation = 0;

  /** Worker-Erzeugung als Seam (in Tests überschreibbar). */
  protected createWorker(): Worker {
    return new Worker('/assets/maia/maia-worker.js');
  }

  /**
   * Bereit machen, OHNE ungefragt zu laden: `ready` → true; Modell im Cache → laden → true; nicht im
   * Cache → Status `missing`, false (dann fragt die Oberfläche und ruft `download()`).
   */
  prepare(): Promise<boolean> {
    if (this.isReady()) return Promise.resolve(true);
    return this.running ??= this.track(this.runPrepare());
  }

  /** Holen (mit Fortschritt), in den Worker, `ready`. Läuft schon ein prepare(), wird dessen Ergebnis
   *  abgewartet — und nur, wenn es mit `missing` endete, danach geladen. */
  download(): Promise<boolean> {
    if (this.isReady()) return Promise.resolve(true);
    const running = this.running;
    if (running) return running.then(ok => (ok || this.statusSig() !== 'missing') ? ok : this.download());
    return this.running = this.track(this.runDownload());
  }

  /**
   * Ein Zug für Maia in dieser Stellung, gewürfelt aus der Verteilung des Modells (Stärke `elo`).
   * UCI in echter Brett-Sicht; `null` = kein legaler Zug. Ohne `ready` → abgelehnt.
   */
  chooseMove(fen: string, elo: number): Promise<string | null> {
    const worker = this.worker;
    if (this.statusSig() !== 'ready' || !worker) return Promise.reject(new Error('Maia is not ready'));
    let enc: MaiaEncoding;
    try { enc = encodePosition(fen); } catch (e) { return Promise.reject(e); }
    if (!enc.legal.length) return Promise.resolve(null);

    const id = this.nextId++;
    return new Promise<Float32Array>((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      try {
        // Die Tokens (3 KB) gehen ebenfalls als Transfer — `enc.legal`/`enc.indices` bleiben hier.
        worker.postMessage({ type: 'infer', id, tokens: enc.tokens.buffer, elo }, [enc.tokens.buffer]);
      } catch (e) {
        this.pending.delete(id);
        reject(e);
      }
    }).then(logits => pickMove(policyFromLogits(logits, enc), this.rng));
  }

  /** Worker beenden (gibt ~150 MB frei), offene Anfragen ablehnen, Status `idle`. Beim nächsten
   *  prepare() kommt das Modell aus dem Cache. */
  release(): void {
    this.generation++;
    this.running = undefined;
    this.dropWorker(new Error('Maia released'));
    this.statusSig.set('idle');
    this.progressSig.set(0);
    this.errorSig.set(null);
  }

  ngOnDestroy(): void { this.release(); }

  private isReady(): boolean {
    return this.statusSig() === 'ready' && !!this.worker;
  }

  /** Das laufende Promise vergessen, sobald es fertig ist — aber nur, wenn es noch DAS laufende ist. */
  private track(run: Promise<boolean>): Promise<boolean> {
    const tracked: Promise<boolean> = run.finally(() => {
      if (this.running === tracked) this.running = undefined;
    });
    return tracked;
  }

  private async runPrepare(): Promise<boolean> {
    const gen = this.generation;
    this.errorSig.set(null);
    let cached: boolean;
    try { cached = await this.store.isCached(); } catch { cached = false; }
    this.canStoreSig.set(this.store.canStore);   // erst jetzt weiß der Speicher, ob er gesperrt ist
    if (gen !== this.generation) return false;
    if (!cached) {
      this.statusSig.set('missing');
      return false;
    }
    this.statusSig.set('loading');
    return this.loadAndStart(gen);
  }

  private runDownload(): Promise<boolean> {
    this.errorSig.set(null);
    this.progressSig.set(0);
    this.statusSig.set('downloading');
    return this.loadAndStart(this.generation);
  }

  private async loadAndStart(gen: number): Promise<boolean> {
    let model: ArrayBuffer;
    try {
      model = await this.store.load(percent => {
        if (gen !== this.generation) return;
        // Auch aus prepare() heraus möglich: ein kaputter Cache-Eintrag wird neu geholt.
        this.statusSig.set('downloading');
        this.progressSig.set(percent);
      });
    } catch (e) {
      this.canStoreSig.set(this.store.canStore);
      if (gen !== this.generation) return false;
      this.fail(e instanceof MaiaModelError ? e.reason : 'failed');
      return false;
    }
    this.canStoreSig.set(this.store.canStore);   // Ablegen kann gescheitert sein (Quota)
    if (gen !== this.generation) return false;
    this.statusSig.set('loading');
    return this.startWorker(model);
  }

  private startWorker(model: ArrayBuffer): Promise<boolean> {
    return new Promise<boolean>(resolve => {
      let worker: Worker;
      try {
        worker = this.createWorker();
      } catch {
        this.fail('failed');
        resolve(false);
        return;
      }
      this.worker = worker;
      this.initDone = resolve;
      worker.onmessage = (e: MessageEvent) => this.onMessage(worker, e.data as WorkerReply);
      worker.onerror = () => { if (this.worker === worker) this.fail('failed'); };
      try {
        worker.postMessage({ type: 'init', model }, [model]);   // Regel 3: transferiert
      } catch {
        this.fail('failed');
        return;
      }
      // Bleibt `ready` aus, ist die Sitzung gescheitert — aber nur für DIESEN Worker (settleInit räumt die
      // Frist bei ready/Fehler/release ab; die Prüfung hier ist die zweite Sicherung).
      this.initTimer = setTimeout(() => {
        this.initTimer = undefined;
        if (this.worker === worker && this.initDone) this.fail('failed');
      }, this.initTimeoutMs);
    });
  }

  private onMessage(worker: Worker, msg: WorkerReply): void {
    if (worker !== this.worker || !msg) return;   // Nachzügler eines verworfenen Workers
    switch (msg.type) {
      case 'ready':
        this.statusSig.set('ready');
        this.settleInit(true);
        return;
      case 'result': {
        const req = this.pending.get(msg.id);
        if (!req) return;
        this.pending.delete(msg.id);
        req.resolve(new Float32Array(msg.logits));
        return;
      }
      case 'error': {
        // Ohne id ist die Sitzung selbst nicht zustande gekommen → alles aus (Regel 5).
        if (typeof msg.id !== 'number') { this.fail('failed'); return; }
        const req = this.pending.get(msg.id);
        if (!req) return;
        this.pending.delete(msg.id);
        req.reject(new Error(msg.message || 'Maia inference failed'));
        return;
      }
    }
  }

  private fail(reason: MaiaModelErrorReason): void {
    this.dropWorker(new Error(`Maia ${reason}`));
    this.statusSig.set('error');
    this.errorSig.set(reason);
  }

  private dropWorker(reason: Error): void {
    const worker = this.worker;
    this.worker = undefined;
    if (worker) {
      worker.onmessage = null;
      worker.onerror = null;
      try { worker.terminate(); } catch { /* schon weg */ }
    }
    const open = [...this.pending.values()];
    this.pending.clear();
    for (const req of open) req.reject(reason);
    this.settleInit(false);
  }

  private settleInit(ok: boolean): void {
    if (this.initTimer !== undefined) { clearTimeout(this.initTimer); this.initTimer = undefined; }
    const done = this.initDone;
    this.initDone = undefined;
    done?.(ok);
  }
}
