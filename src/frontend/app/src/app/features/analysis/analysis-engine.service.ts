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

  /** FEN des letzten Crashes. Crasht DIESELBE Stellung erneut → deterministisch kaputt → nicht
   *  weiter neu instanziieren (ein Neustart lädt ~7 MB WASM, v. a. mobil ein Memory-Thrash). */
  private lastCrashFen: string | null = null;

  /** UCI-Sequencing: die nächste zu suchende Stellung. Wird erst als `position`+`go` rausgeschickt,
   *  wenn eine evtl. laufende Suche ihr `bestmove` gemeldet hat (siehe `searching`). */
  private pendingGoFen: string | null = null;

  /** True solange ein `go` läuft und noch kein (terminales) `bestmove` zurückkam. Ein neues `go` in
   *  den asyncify-Abbau der laufenden Suche zu schieben crasht den lite-single-WASM-Kern mit
   *  „RuntimeError: unreachable" (reproduziert: crasht nach ~7 Stellungswechseln). Deshalb bei
   *  Navigation erst `stop`, dann das `bestmove` der alten Suche abwarten, DANN das nächste `go`. */
  private searching = false;

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
          // FEN ins Crash-Log: ohne die konkrete Stellung ist „RuntimeError: unreachable" nicht
          // reproduzierbar (alle Crashes sähen identisch aus). So lässt sich der Auslöser nachstellen.
          worker.onerror = (e: ErrorEvent) => { this.reportEngineEvent?.('crash', `${e?.message || 'worker error'} @ ${this.currentFen}`); this.handleCrash(); };
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
    this.searching = false;
    this.pendingGoFen = null;
    const fen = this.currentFen;
    const wasRunning = this.state$.value.running;
    this.crashStreak++;
    // Crasht DIESELBE Stellung erneut, ist sie deterministisch kaputt: ein weiterer Neustart
    // würde nur wieder ~7 MB WASM instanziieren (Memory-Thrash) und dieselbe Stellung erneut
    // zum Absturz bringen → sofort aufgeben statt zu thrashen (und die Logs zu vervielfachen).
    const sameCrash = !!fen && fen === this.lastCrashFen;
    this.lastCrashFen = fen || null;
    if (fen && wasRunning && !sameCrash && this.crashStreak <= 3) {
      // Erster Crash auf dieser Stellung (oder Stellung hat gewechselt) → einmal sauber neu starten.
      this.analyze(fen).catch(() => this.state$.next({ fen, depth: 0, lines: [], running: false }));
    } else {
      // Wiederholter Crash auf derselben Stellung ODER zu viele Crashes hintereinander → aufgeben.
      this.reportEngineEvent?.('giveup', sameCrash ? `repeat-crash @ ${fen}` : `streak=${this.crashStreak}`);
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
    // Neue, vom Nutzer angesteuerte Stellung → Crash-Budget zurücksetzen. Der Recovery-Retry aus
    // handleCrash ruft analyze() mit DERSELBEN FEN auf und setzt deshalb bewusst nichts zurück.
    if (fen !== this.currentFen) { this.crashStreak = 0; this.lastCrashFen = null; }
    this.gen++;
    this.currentFen = fen;
    this.sideToMove = (fen.split(' ')[1] === 'b') ? 'b' : 'w';
    this.partial = new Map();
    this.clearWatchdog();
    this.state$.next({ fen, depth: 0, lines: [], running: true });
    // Sequencing gegen den asyncify-Crash: das `go` für die neue Stellung erst absetzen, wenn
    // eine evtl. laufende Suche WIRKLICH beendet ist (ihr `bestmove` zurückkam) — NICHT bloß nach
    // `readyok` (das beantwortet die Engine auch mitten in der Suche sofort, also keine echte
    // Barriere). Läuft noch eine Suche → nur `stop`; das `bestmove` startet dann die pending-Suche.
    this.pendingGoFen = fen;
    if (this.searching) {
      this.send('stop');
      this.armWatchdog();   // greift, falls auf das stoppende `bestmove` ein Hänger folgt
    } else {
      this.launchPending();
    }
  }

  /** Schickt die in `pendingGoFen` vorgemerkte Stellung als `position`+`go` (nur wenn keine Suche
   *  mehr läuft — vom Aufrufer sicherzustellen). Setzt `searching` und stellt den Watchdog scharf. */
  private launchPending(): void {
    const fen = this.pendingGoFen;
    if (fen === null || !this.worker) return;
    this.pendingGoFen = null;
    this.send('setoption name MultiPV value ' + this.multiPv);
    this.send('position fen ' + fen);
    this.send('go depth ' + this.depthCap);
    this.searching = true;
    this.armWatchdog();   // ab jetzt Info-Lines erwarten
  }

  stop(): void {
    this.clearWatchdog();
    this.send('stop');
    const s = this.state$.value;
    if (s.running) this.state$.next({ ...s, running: false });
  }

  // Reine Setter: das erneute analyze() stößt der Aufrufer an (analysis.component.onLinesChange/
  // onDepthChange). Früher triggerten die Setter zusätzlich selbst analyze() → pro Änderung zwei
  // gen++/running-Emissionen für dieselbe FEN. Jetzt genau ein analyze() pro Änderung.
  setMultiPv(n: number): void {
    this.multiPv = Math.max(1, Math.min(5, Math.round(n)));
  }

  setDepth(d: number): void {
    this.depthCap = Math.max(6, Math.min(40, Math.round(d)));
  }

  /** Worker-Nachricht parsen (info / bestmove). Generation-geschützt gegen Altzeilen. */
  private onMessage(e: MessageEvent): void {
    const line = e.data;
    if (typeof line !== 'string') return;

    // readyok wird im analyze-Pfad nicht mehr als Gate genutzt (nur init() wartet darauf, mit
    // eigenem Listener). Hier daher nur abfangen, damit es nicht als info-Zeile fehlinterpretiert wird.
    if (line.startsWith('readyok')) return;

    const genAtSend = this.gen;

    if (line.startsWith('bestmove')) {
      // Die laufende Suche ist beendet (regulär ODER durch `stop`). Erst JETZT ist es sicher,
      // das nächste `go` abzusetzen — steht eine Stellung an, jetzt starten.
      this.searching = false;
      if (this.pendingGoFen !== null) {
        this.launchPending();
        return;
      }
      this.clearWatchdog();   // Suche regulär beendet, nichts steht an
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
    this.lastCrashFen = null;
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
    this.searching = false;
    this.pendingGoFen = null;
    this.crashStreak = 0;
    this.fatalError$.next(null);
    this.clearWatchdog();
    this.state$.next(EMPTY);
  }
}
