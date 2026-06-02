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
  /** Reject der gerade laufenden Suche — damit ein Worker-Crash sie sofort beendet (statt 10 s Timeout). */
  private currentReject?: (reason?: unknown) => void;

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
        reject(reason);
      };

      const timeout = setTimeout(() => fail('Stockfish init timeout'), 15000);
      worker.onerror = () => fail('Stockfish worker error');

      const handler = (e: MessageEvent) => {
        if (typeof e.data === 'string' && e.data.includes('readyok')) {
          worker.removeEventListener('message', handler);
          clearTimeout(timeout);
          // Ab jetzt: dauerhafter Crash-Handler statt init-reject.
          worker.onerror = () => this.handleCrash();
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
    await this.init();
    const task = this.pending.then(() => this.runSearch(fen, depth));
    this.pending = task.catch(() => {});
    return task;
  }

  async getEval(fen: string, depth = 16): Promise<string> {
    const result = await this.getBestMove(fen, depth);
    return result.eval;
  }

  /** Worker abgestürzt → terminieren + zurücksetzen, laufende Suche abbrechen. Nächster Aufruf init neu. */
  private handleCrash(): void {
    try { this.worker?.terminate(); } catch { /* ignore */ }
    this.worker = undefined;
    this.initPromise = undefined;
    this.pending = Promise.resolve();
    const reject = this.currentReject;
    this.currentReject = undefined;
    if (reject) reject('Stockfish worker crashed');
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
        if (this.currentReject === reject) this.currentReject = undefined;
        fn();
      };

      const timeout = setTimeout(() => done(() => reject('Stockfish timeout')), 10000);
      // Damit ein Crash (handleCrash) diese Suche sofort beenden kann.
      this.currentReject = reject;

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
      this.currentReject = undefined;
    }
  }
}
