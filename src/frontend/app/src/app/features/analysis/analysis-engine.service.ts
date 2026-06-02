import { Injectable, OnDestroy } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';

/** Eine Computer-Line (Principal Variation) aus der MultiPV-Analyse. */
export interface AnalysisLine {
  /** 1-basiert: 1 = beste Line. */
  multipv: number;
  depth: number;
  scoreType: 'cp' | 'mate';
  /** Aus Sicht von Weiß (cp = Centipawns, mate = Züge bis Matt; negativ = für Schwarz). */
  score: number;
  /** Formatiert, z. B. "+1.5" / "-0.3" / "#3" / "#-2". */
  evalText: string;
  /** Hauptvariante in UCI (z. B. ["e2e4","e7e5",...]). */
  pvUci: string[];
}

export interface AnalysisState {
  fen: string;
  depth: number;
  lines: AnalysisLine[];   // nach multipv sortiert (beste zuerst)
  running: boolean;
}

const EMPTY: AnalysisState = { fen: '', depth: 0, lines: [], running: false };

/**
 * MultiPV-Analyse mit lokalem Stockfish-WASM (eigener Worker, getrennt vom Puzzle-Solver).
 * Läuft kontinuierlich auf der aktuellen Stellung; die Lines aktualisieren sich mit
 * steigender Tiefe (wie Lichess). Eval immer aus Sicht von Weiß.
 */
@Injectable({ providedIn: 'root' })
export class AnalysisEngineService implements OnDestroy {
  private worker?: Worker;
  private initPromise?: Promise<void>;

  private multiPv = 3;
  private depthCap = 22;

  /** Generation: bei jeder neuen Stellung erhöht; alte info-Zeilen werden verworfen. */
  private gen = 0;
  private currentFen = '';
  private sideToMove: 'w' | 'b' = 'w';
  private partial = new Map<number, AnalysisLine>();

  private state$ = new BehaviorSubject<AnalysisState>(EMPTY);
  readonly analysis$: Observable<AnalysisState> = this.state$.asObservable();

  get linesRequested(): number { return this.multiPv; }
  get depthLimit(): number { return this.depthCap; }

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
      this.worker.onerror = () => { clearTimeout(timeout); reject('Stockfish worker error'); };
      const onReady = (e: MessageEvent) => {
        if (typeof e.data === 'string' && e.data.includes('readyok')) {
          this.worker!.removeEventListener('message', onReady);
          clearTimeout(timeout);
          // Dauerhaften Listener für die laufende Analyse anhängen.
          this.worker!.addEventListener('message', (ev) => this.onMessage(ev));
          resolve();
        }
      };
      this.worker.addEventListener('message', onReady);
      this.send('uci');
      this.send('setoption name MultiPV value ' + this.multiPv);
      this.send('isready');
    });
    return this.initPromise;
  }

  /** Startet (oder wechselt) die Analyse auf eine Stellung. */
  async analyze(fen: string): Promise<void> {
    await this.init();
    this.gen++;
    this.currentFen = fen;
    this.sideToMove = (fen.split(' ')[1] === 'b') ? 'b' : 'w';
    this.partial = new Map();
    this.state$.next({ fen, depth: 0, lines: [], running: true });
    this.send('stop');
    this.send('setoption name MultiPV value ' + this.multiPv);
    this.send('position fen ' + fen);
    this.send('go depth ' + this.depthCap);
  }

  stop(): void {
    this.send('stop');
    const s = this.state$.value;
    if (s.running) this.state$.next({ ...s, running: false });
  }

  setMultiPv(n: number): void {
    this.multiPv = Math.max(1, Math.min(5, Math.round(n)));
    if (this.currentFen && this.state$.value.running) this.analyze(this.currentFen);
  }

  setDepth(d: number): void {
    this.depthCap = Math.max(6, Math.min(40, Math.round(d)));
    if (this.currentFen && this.state$.value.running) this.analyze(this.currentFen);
  }

  /** Worker-Nachricht parsen (info / bestmove). Generation-geschützt gegen Altzeilen. */
  private onMessage(e: MessageEvent): void {
    const line = e.data;
    if (typeof line !== 'string') return;
    const genAtSend = this.gen;

    if (line.startsWith('bestmove')) {
      const s = this.state$.value;
      if (s.running) this.state$.next({ ...s, running: false });
      return;
    }
    if (!line.startsWith('info ') || !line.includes(' pv ')) return;

    const parsed = this.parseInfo(line);
    if (!parsed) return;
    if (genAtSend !== this.gen) return;   // Stellung hat gewechselt
    if (parsed.multipv > this.multiPv) return;

    this.partial.set(parsed.multipv, parsed);
    const lines = [...this.partial.values()].sort((a, b) => a.multipv - b.multipv);
    const depth = Math.max(...lines.map(l => l.depth), 0);
    this.state$.next({ fen: this.currentFen, depth, lines, running: true });
  }

  /** Parst eine `info ... multipv k ... score cp|mate V ... pv m1 m2`-Zeile. */
  parseInfo(line: string, sideToMove: 'w' | 'b' = this.sideToMove): AnalysisLine | null {
    const depthM = line.match(/\bdepth (\d+)/);
    const mpvM = line.match(/\bmultipv (\d+)/);
    const scoreM = line.match(/\bscore (cp|mate) (-?\d+)/);
    const pvM = line.match(/\bpv (.+)$/);
    if (!depthM || !scoreM || !pvM) return null;

    const multipv = mpvM ? parseInt(mpvM[1], 10) : 1;
    let value = parseInt(scoreM[2], 10);
    if (sideToMove === 'b') value = -value;   // → Sicht von Weiß
    const scoreType = scoreM[1] as 'cp' | 'mate';

    let evalText: string;
    if (scoreType === 'cp') {
      const v = value / 100;
      evalText = (v > 0 ? '+' : '') + v.toFixed(2);
    } else {
      evalText = '#' + value;
    }

    return {
      multipv,
      depth: parseInt(depthM[1], 10),
      scoreType,
      score: value,
      evalText,
      pvUci: pvM[1].trim().split(/\s+/),
    };
  }

  private send(cmd: string): void { this.worker?.postMessage(cmd); }

  ngOnDestroy(): void { this.destroy(); }

  destroy(): void {
    if (this.worker) {
      try { this.send('stop'); this.send('quit'); } catch {}
      this.worker.terminate();
      this.worker = undefined;
      this.initPromise = undefined;
    }
    this.state$.next(EMPTY);
  }
}
