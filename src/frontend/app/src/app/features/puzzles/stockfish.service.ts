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
    const task = this.pending.then(async () => { await this.init(); return this.runSearch(fen, depth); });
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

      const timeout = setTimeout(() => done(() => {
        this.reportEngineEvent?.('search_timeout', `depth=${depth}`);
        reject('Stockfish timeout');
      }), 10000);
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
            lastEval = value > 0 ? `#${value}` : `#${value}`;
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
    if (this.worker) {
      try { this.worker.postMessage('quit'); } catch { /* ignore */ }
      try { this.worker.terminate(); } catch { /* ignore */ }
      this.worker = undefined;
      this.initPromise = undefined;
      this.pending = Promise.resolve();
      this.currentAbort = undefined;
    }
  }
}
