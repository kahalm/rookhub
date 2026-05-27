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

  init(): Promise<void> {
    if (this.initPromise) return this.initPromise;

    this.initPromise = new Promise<void>((resolve, reject) => {
      try {
        this.worker = new Worker('/assets/stockfish/stockfish-18-lite-single.js');
      } catch {
        reject('Failed to create Stockfish worker');
        return;
      }

      const timeout = setTimeout(() => reject('Stockfish init timeout'), 15000);

      this.worker.onerror = () => {
        clearTimeout(timeout);
        reject('Stockfish worker error');
      };

      const handler = (e: MessageEvent) => {
        if (typeof e.data === 'string' && e.data.includes('readyok')) {
          this.worker!.removeEventListener('message', handler);
          clearTimeout(timeout);
          resolve();
        }
      };
      this.worker.addEventListener('message', handler);
      this.send('uci');
      this.send('isready');
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

  private runSearch(fen: string, depth: number): Promise<StockfishResult> {
    const sideToMove = fen.split(' ')[1];

    return new Promise<StockfishResult>((resolve, reject) => {
      let lastEval = '0.0';

      const timeout = setTimeout(() => {
        this.worker!.removeEventListener('message', handler);
        reject('Stockfish timeout');
      }, 10000);

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
          clearTimeout(timeout);
          this.worker!.removeEventListener('message', handler);
          const move = line.split(' ')[1];
          if (move && move !== '(none)') {
            resolve({ move, eval: lastEval });
          } else {
            reject('No move found');
          }
        }
      };
      this.worker!.addEventListener('message', handler);
      this.send(`position fen ${fen}`);
      this.send(`go depth ${depth}`);
    });
  }

  private send(cmd: string): void {
    this.worker?.postMessage(cmd);
  }

  ngOnDestroy(): void {
    this.destroy();
  }

  destroy(): void {
    if (this.worker) {
      this.send('quit');
      this.worker.terminate();
      this.worker = undefined;
      this.initPromise = undefined;
      this.pending = Promise.resolve();
    }
  }
}
