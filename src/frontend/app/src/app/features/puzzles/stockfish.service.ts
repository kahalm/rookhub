import { Injectable, OnDestroy } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class StockfishService implements OnDestroy {
  private worker?: Worker;
  private ready = false;

  init(): Promise<void> {
    if (this.worker) return Promise.resolve();

    return new Promise<void>((resolve) => {
      this.worker = new Worker('/assets/stockfish/stockfish-18-lite-single.js');
      const handler = (e: MessageEvent) => {
        if (typeof e.data === 'string' && e.data.includes('readyok')) {
          this.worker!.removeEventListener('message', handler);
          this.ready = true;
          resolve();
        }
      };
      this.worker.addEventListener('message', handler);
      this.send('uci');
      this.send('isready');
    });
  }

  getBestMove(fen: string, depth = 12): Promise<string> {
    if (!this.worker || !this.ready) return Promise.reject('Stockfish not ready');

    return new Promise<string>((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.worker!.removeEventListener('message', handler);
        reject('Stockfish timeout');
      }, 10000);

      const handler = (e: MessageEvent) => {
        const line = e.data as string;
        if (typeof line === 'string' && line.startsWith('bestmove')) {
          clearTimeout(timeout);
          this.worker!.removeEventListener('message', handler);
          const move = line.split(' ')[1];
          if (move && move !== '(none)') {
            resolve(move);
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
      this.ready = false;
    }
  }
}
