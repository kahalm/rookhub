import { Injectable, OnDestroy } from '@angular/core';

export interface StockfishResult {
  move: string;
  eval: string; // from white's perspective, e.g. "+1.5", "-0.3", "#3", "#-2"
}

@Injectable({ providedIn: 'root' })
export class StockfishService implements OnDestroy {
  private worker?: Worker;
  private initPromise?: Promise<void>;
  private pending: Promise<any> = Promise.resolve();
  /** Abbruch der gerade laufenden Suche (räumt Timeout + Listener auf und rejected) — für sofortigen Crash-Abbruch. */
  private currentAbort?: () => void;
  /** Läuft, solange ein abgebrochener Kern noch ausräumt (stop → bestmove); die nächste Suche wartet. */
  private draining?: Promise<void>;

  /** Optionaler Telemetrie-Hook (Crash/Hänger melden); von AppComponent an ClientLogService verdrahtet. */
  reportEngineEvent?: (kind: string, detail?: string) => void;

  /** Worker-Erzeugung als Seam (in Tests überschreibbar). */
  protected createWorker(): Worker {
    return new Worker('/assets/stockfish/stockfish-18-lite-single.js');
  }

  init(): Promise<void> {
    if (this.initPromise) return this.initPromise;

    this.initPromise = new Promise<void>((resolve, reject) => {
      let worker: Worker;
      try {
        worker = this.createWorker();
        this.worker = worker;
      } catch {
        this.initPromise = undefined;   // Retry beim nächsten Aufruf erlauben
        reject('Failed to create Stockfish worker');
        return;
      }

      const fail = (reason: string) => {
        clearTimeout(timeout);
        // init fehlgeschlagen → alles zurücksetzen, damit der nächste Aufruf neu versucht.
        try { worker.terminate(); } catch { /* ignore */ }
        if (this.worker === worker) this.worker = undefined;
        this.initPromise = undefined;
        this.reportEngineEvent?.('init_failed', reason);
        reject(reason);
      };

      const timeout = setTimeout(() => fail('Stockfish init timeout'), 15000);
      worker.onerror = () => fail('Stockfish worker error');

      const handler = (e: MessageEvent) => {
        if (typeof e.data === 'string' && e.data.includes('readyok')) {
          worker.removeEventListener('message', handler);
          clearTimeout(timeout);
          // Ab jetzt: dauerhafter Crash-Handler statt init-reject.
          worker.onerror = (e: ErrorEvent) => { this.reportEngineEvent?.('crash', e?.message || ''); this.handleCrash(); };
          resolve();
        }
      };
      worker.addEventListener('message', handler);
      worker.postMessage('uci');
      worker.postMessage('isready');
    });

    return this.initPromise;
  }

  async getBestMove(fen: string, depth = 16): Promise<StockfishResult> {
    // init() INNERHALB der Kette: stürzt eine vorherige Suche ab (Worker weg), initialisiert
    // die nächste gequeuete Suche einen frischen Worker, statt mit „not initialized" zu scheitern.
    const task = this.pending.then(async () => {
      // Ein per Timeout aufgegebener Kern rechnet noch — erst abwarten, dann neu anwerfen.
      if (this.draining) await this.draining;
      await this.init();
      return this.runSearch(fen, depth);
    });
    this.pending = task.catch(() => {});
    return task;
  }

  async getEval(fen: string, depth = 16): Promise<string> {
    const result = await this.getBestMove(fen, depth);
    return result.eval;
  }

  /** Worker abgestürzt → terminieren + zurücksetzen, laufende Suche abbrechen. Nächster Aufruf init neu. */
  private handleCrash(): void {
    const abort = this.currentAbort;
    this.currentAbort = undefined;
    try { this.worker?.terminate(); } catch { /* ignore */ }
    this.worker = undefined;
    this.initPromise = undefined;
    this.pending = Promise.resolve();
    abort?.();   // räumt Timeout + Listener der laufenden Suche auf und rejected sie
  }

  /**
   * Timeout-Pfad: der Aufrufer hat aufgegeben, die Suche läuft im Worker aber WEITER. FALLE:
   * schickt die nächste Suche jetzt `position`+`go` in den noch rechnenden lite-single-Kern,
   * platzt der asyncify-Abbau („RuntimeError: unreachable", vgl. Fix v0.152.1) — deshalb erst
   * `stop` senden und das quittierende `bestmove` abwarten. Bleibt es aus, wird der Kern hart
   * entsorgt (der nächste Aufruf initialisiert dann einen frischen Worker).
   */
  private drainSearch(worker: Worker): void {
    const drain = new Promise<void>(resolve => {
      const finish = () => {
        clearTimeout(guard);
        worker.removeEventListener('message', onMessage);
        if (this.draining === drain) this.draining = undefined;
        resolve();
      };
      const onMessage = (e: MessageEvent) => {
        if (typeof e.data === 'string' && e.data.startsWith('bestmove')) finish();
      };
      const guard = setTimeout(() => {
        this.reportEngineEvent?.('search_stop_timeout');
        try { worker.terminate(); } catch { /* ignore */ }
        if (this.worker === worker) { this.worker = undefined; this.initPromise = undefined; }
        finish();
      }, 2000);
      worker.addEventListener('message', onMessage);
      try { worker.postMessage('stop'); } catch { /* ignore */ }
    });
    this.draining = drain;
  }

  private runSearch(fen: string, depth: number): Promise<StockfishResult> {
    const sideToMove = fen.split(' ')[1];
    const worker = this.worker;
    if (!worker) return Promise.reject('Stockfish not initialized');

    return new Promise<StockfishResult>((resolve, reject) => {
      let lastEval = '0.0';

      const done = (fn: () => void) => {
        clearTimeout(timeout);
        worker.removeEventListener('message', handler);
        this.currentAbort = undefined;
        fn();
      };

      const timeout = setTimeout(() => {
        this.reportEngineEvent?.('search_timeout', `depth=${depth}`);
        done(() => reject('Stockfish timeout'));
        this.drainSearch(worker);
      }, 10000);
      // Damit ein Crash (handleCrash) diese Suche sofort sauber beenden kann (Timeout+Listener weg).
      this.currentAbort = () => done(() => reject('Stockfish worker crashed'));

      const handler = (e: MessageEvent) => {
        const line = e.data as string;
        if (typeof line !== 'string') return;

        const scoreMatch = line.match(/score (cp|mate) (-?\d+)/);
        if (scoreMatch) {
          let value = parseInt(scoreMatch[2], 10);
          if (sideToMove === 'b') value = -value;
          if (scoreMatch[1] === 'cp') {
            const v = value / 100;
            lastEval = (v >= 0 ? '+' : '') + v.toFixed(1);
          } else {
            lastEval = `#${value}`;
          }
        }

        if (line.startsWith('bestmove')) {
          const move = line.split(' ')[1];
          done(() => {
            if (move && move !== '(none)') resolve({ move, eval: lastEval });
            else reject('No move found');
          });
        }
      };
      worker.addEventListener('message', handler);
      worker.postMessage(`position fen ${fen}`);
      worker.postMessage(`go depth ${depth}`);
    });
  }

  ngOnDestroy(): void {
    this.destroy();
  }

  destroy(): void {
    // Erst den Wartenden erlösen: ohne currentAbort() hinge ein laufender Aufruf bis zum
    // 10-s-Suchtimeout, obwohl der Worker längst terminiert ist.
    const abort = this.currentAbort;
    this.currentAbort = undefined;
    if (this.worker) {
      try { this.worker.postMessage('quit'); } catch { /* ignore */ }
      try { this.worker.terminate(); } catch { /* ignore */ }
      this.worker = undefined;
      this.initPromise = undefined;
      this.pending = Promise.resolve();
    }
    this.draining = undefined;
    abort?.();
  }
}
