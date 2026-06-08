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

  /** Gesetzt wenn die Engine nach zu vielen Crashes aufgibt; null = OK. */
  private fatalError$ = new BehaviorSubject<string | null>(null);
  readonly engineFatalError$: Observable<string | null> = this.fatalError$.asObservable();

  /** Aufeinanderfolgende Crashes ohne erfolgreiche Antwort — gegen Endlos-Recovery-Loops. */
  private crashStreak = 0;

  /** UCI-Sequencing: neue Suche erst nach `readyok` starten (nicht mitten in laufender Suche). */
  private pendingGoFen: string | null = null;
  private awaitingReady = false;

  /** Hänger-Watchdog: liefert die Engine nach `go` binnen `watchdogMs` keine Info-Line → Stall. */
  protected watchdogMs = 9000;
  private watchdog?: ReturnType<typeof setTimeout>;

  /** Optionaler Telemetrie-Hook (Crash/Hänger melden); von AppComponent an ClientLogService verdrahtet. */
  reportEngineEvent?: (kind: string, detail?: string) => void;

  get linesRequested(): number { return this.multiPv; }
  get depthLimit(): number { return this.depthCap; }

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
        try { worker.terminate(); } catch { /* ignore */ }
        if (this.worker === worker) this.worker = undefined;
        this.initPromise = undefined;
        this.reportEngineEvent?.('init_failed', reason);
        reject(reason);
      };
      const timeout = setTimeout(() => fail('Stockfish init timeout'), 15000);
      worker.onerror = () => fail('Stockfish worker error');
      const onReady = (e: MessageEvent) => {
        if (typeof e.data === 'string' && e.data.includes('readyok')) {
          worker.removeEventListener('message', onReady);
          clearTimeout(timeout);
          // Ab jetzt: dauerhafter Crash-Handler + Analyse-Listener.
          worker.onerror = (e: ErrorEvent) => { this.reportEngineEvent?.('crash', e?.message || ''); this.handleCrash(); };
          worker.addEventListener('message', (ev) => this.onMessage(ev));
          resolve();
        }
      };
      worker.addEventListener('message', onReady);
      this.send('uci');
      // Hash klein halten → verhindert unbegrenztes Wachsen des WASM-Heaps (OOM-Crashes).
      this.send('setoption name Hash value 16');
      this.send('setoption name MultiPV value ' + this.multiPv);
      this.send('isready');
    });
    return this.initPromise;
  }

  /** Worker abgestürzt → zurücksetzen und (falls eine Analyse lief) die aktuelle Stellung neu aufnehmen. */
  private handleCrash(): void {
    this.clearWatchdog();
    try { this.worker?.terminate(); } catch { /* ignore */ }
    this.worker = undefined;
    this.initPromise = undefined;
    this.partial = new Map();
    this.awaitingReady = false;
    this.pendingGoFen = null;
    const fen = this.currentFen;
    const wasRunning = this.state$.value.running;
    this.crashStreak++;
    if (fen && wasRunning && this.crashStreak <= 3) {
      // Nahtlos neu initialisieren + dieselbe Stellung erneut analysieren.
      this.analyze(fen).catch(() => this.state$.next({ fen, depth: 0, lines: [], running: false }));
    } else {
      // Zu viele Crashes hintereinander → aufgeben statt Endlos-Loop.
      this.reportEngineEvent?.('giveup', `streak=${this.crashStreak}`);
      this.fatalError$.next('crash');
      this.state$.next({ fen, depth: 0, lines: [], running: false });
    }
  }

  private armWatchdog(): void {
    this.clearWatchdog();
    if (this.watchdogMs <= 0) return;
    this.watchdog = setTimeout(() => {
      // Engine läuft (running), liefert aber keine Info-Line → als Hänger behandeln + neu starten.
      this.reportEngineEvent?.('stall', `no info ${this.watchdogMs}ms`);
      this.handleCrash();
    }, this.watchdogMs);
  }

  private clearWatchdog(): void {
    if (this.watchdog !== undefined) { clearTimeout(this.watchdog); this.watchdog = undefined; }
  }

  /** Startet (oder wechselt) die Analyse auf eine Stellung. */
  async analyze(fen: string): Promise<void> {
    await this.init();
    this.gen++;
    this.currentFen = fen;
    this.sideToMove = (fen.split(' ')[1] === 'b') ? 'b' : 'w';
    this.partial = new Map();
    this.clearWatchdog();
    this.state$.next({ fen, depth: 0, lines: [], running: true });
    // Sauberes Sequencing: stop → (isready/readyok) → position+go. So wird 'position' nie
    // mitten in eine laufende Suche geschickt (sonst verschluckt Stockfish sie → keine
    // Info-Lines → ewiges „calculating"). Nur EIN isready offen halten; bei schneller
    // Navigation gewinnt die zuletzt angeforderte Stellung.
    this.pendingGoFen = fen;
    this.send('setoption name MultiPV value ' + this.multiPv);
    this.send('stop');
    if (!this.awaitingReady) {
      this.awaitingReady = true;
      this.send('isready');
    }
    // Watchdog schon ab HIER scharf stellen — deckt auch die isready→readyok-Phase ab.
    // Bleibt readyok aus (Worker-Hänger ohne onerror), greift sonst nichts: awaitingReady
    // bliebe für immer true und keine spätere analyze() würde je wieder isready senden.
    this.armWatchdog();
  }

  stop(): void {
    this.clearWatchdog();
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

    if (line.startsWith('readyok')) {
      this.awaitingReady = false;
      const fen = this.pendingGoFen;
      if (fen !== null) {
        this.pendingGoFen = null;
        this.send('position fen ' + fen);
        this.send('go depth ' + this.depthCap);
        this.armWatchdog();   // ab jetzt Info-Lines erwarten
      }
      return;
    }

    const genAtSend = this.gen;

    if (line.startsWith('bestmove')) {
      // 'bestmove' eines gestoppten Suchlaufs ignorieren, wenn schon ein neuer ansteht.
      if (this.pendingGoFen !== null || this.awaitingReady) return;
      this.clearWatchdog();   // Suche regulär beendet
      const s = this.state$.value;
      if (s.running) this.state$.next({ ...s, running: false });
      return;
    }
    if (!line.startsWith('info ') || !line.includes(' pv ')) return;

    const parsed = this.parseInfo(line);
    if (!parsed) return;
    if (genAtSend !== this.gen) return;   // Stellung hat gewechselt
    if (parsed.multipv > this.multiPv) return;

    this.clearWatchdog();   // Engine antwortet → kein Hänger
    this.crashStreak = 0;   // Engine liefert wieder → Recovery-Zähler zurücksetzen
    this.fatalError$.next(null);
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
    this.awaitingReady = false;
    this.pendingGoFen = null;
    this.crashStreak = 0;
    this.fatalError$.next(null);
    this.clearWatchdog();
    this.state$.next(EMPTY);
  }
}
