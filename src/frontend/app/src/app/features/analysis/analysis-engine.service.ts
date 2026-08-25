import { Injectable, OnDestroy } from '@angular/core';
import { BehaviorSubject, Observable, Subscription } from 'rxjs';
import { EngineAnalyseLine, EngineAnalyseWork } from './external-engine.service';
import { normalizeCastlingUci } from './castling-uci.util';

/** Vom User gewählte External Engine (Lichess-Anbindung); Maxima kommen von der Registrierung. */
export interface RemoteEngine {
  id: string;
  name: string;
  maxThreads: number;
  maxHash: number;
}

/** Transport der Remote-Analyse — von der Component verdrahtet (hält den Service DI-frei testbar). */
export type RemoteAnalyseTransport = (engineId: string, work: EngineAnalyseWork) => Observable<EngineAnalyseLine>;

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
  /** Bisher durchsuchte Stellungen der laufenden Suche; 0 = noch nichts gemeldet. */
  nodes: number;
  /** Knoten pro Sekunde (Rechengeschwindigkeit); 0 = noch nicht messbar. */
  nps: number;
}

const EMPTY: AnalysisState = { fen: '', depth: 0, lines: [], running: false, nodes: 0, nps: 0 };

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
  /** Generation, für die das aktuell laufende `go` abgesetzt wurde. MUSS beim Absetzen festgehalten
   *  werden, nicht im Handler gelesen: `gen` beim Eintreffen einer Zeile mit `gen` zu vergleichen
   *  ist immer wahr (beides derselbe synchrone Aufruf) — der Vergleich lief deshalb ins Leere und
   *  Zeilen einer ÜBERHOLTEN Suche (Stellungswechsel, Umschalten auf die externe Engine) landeten
   *  weiter in `state$`. */
  private searchGen = -1;
  private currentFen = '';
  private sideToMove: 'w' | 'b' = 'w';
  private partial = new Map<number, AnalysisLine>();

  /** Zuletzt gemeldete Suchleistung der laufenden Suche (0 = noch nichts). Wird bei jeder neuen
   *  Suche zurückgesetzt, damit die Anzeige nicht den Wert der Vorstellung weiterträgt. */
  private lastNodes = 0;
  private lastNps = 0;

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

  /** Handle des Init-Timeouts. MUSS als Feld liegen, damit destroy() ihn löschen kann: der
   *  Timer lief sonst nach dem Zerstören weiter, terminierte einen längst entsorgten Worker
   *  und lehnte ein Promise ab, das niemand mehr auffängt. Fällt erst mit dem Vergleichsmodus
   *  ins Gewicht, wo Instanzen laufend entstehen und vergehen. */
  private initTimeout?: ReturnType<typeof setTimeout>;
  /** Abbruch des laufenden init() — nur gesetzt, solange der Handshake läuft. destroy()
   *  MUSS ihn aufrufen: sonst bleibt ein `await this.init()` fuer immer stehen. */
  private initFail?: (reason: string) => void;

  /** Hänger-Watchdog: liefert die Engine nach `go` binnen `watchdogMs` keine Info-Line → Stall. */
  protected watchdogMs = 9000;
  private watchdog?: ReturnType<typeof setTimeout>;

  /** Optionaler Telemetrie-Hook (Crash/Hänger melden); von AppComponent an ClientLogService verdrahtet. */
  reportEngineEvent?: (kind: string, detail?: string) => void;

  // ---- External Engine (Lichess-Anbindung): Analyse läuft remote statt im WASM-Worker ----

  private remoteEngine: RemoteEngine | null = null;
  private remoteTransport?: RemoteAnalyseTransport;
  private remoteSub?: Subscription;
  /** Wartet auf die ERSTE Zeile einer Remote-Suche; bleibt sie aus, gilt die Engine als offline. */
  private remoteFirstLineGuard?: ReturnType<typeof setTimeout>;
  /** Eine Session-ID pro Service-Lebenszeit — Provider halten die Engine je Session warm. */
  private readonly remoteSessionId = 'rh-' + Math.random().toString(36).slice(2, 12);
  /** True sobald die Remote-Analyse VOR der ersten Datenzeile scheiterte → Rest der Sitzung lokal. */
  private remoteFallbackSubject = new BehaviorSubject<boolean>(false);
  readonly remoteFallback$: Observable<boolean> = this.remoteFallbackSubject.asObservable();

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
        this.reportEngineEvent?.('init_failed', 'worker constructor threw');
        this.fatalError$.next('Failed to create Stockfish worker');
        reject('Failed to create Stockfish worker');
        return;
      }
      const fail = (reason: string) => {
        clearTimeout(timeout);
        this.initTimeout = undefined;
        this.initFail = undefined;
        try { worker.terminate(); } catch { /* ignore */ }
        if (this.worker === worker) this.worker = undefined;
        this.initPromise = undefined;
        this.reportEngineEvent?.('init_failed', reason);
        // MUSS raus: analyze() bricht bei fehlgeschlagenem init() ab, BEVOR es
        // state$.next({running:true}) sendet. Ohne diese Meldung kaeme also nie eine Zeile,
        // die „Berechne…" widerlegt — die Karte behauptete dauerhaft, sie rechne.
        this.fatalError$.next(reason);
        reject(reason);
      };
      this.initFail = fail;
      const timeout = setTimeout(() => fail('Stockfish init timeout'), 15000);
      this.initTimeout = timeout;
      worker.onerror = () => fail('Stockfish worker error');
      const onReady = (e: MessageEvent) => {
        if (typeof e.data === 'string' && e.data.includes('readyok')) {
          worker.removeEventListener('message', onReady);
          clearTimeout(timeout);
          this.initTimeout = undefined;
          this.initFail = undefined;
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
      this.analyze(fen).catch(() => this.state$.next({ fen, depth: 0, lines: [], running: false, nodes: 0, nps: 0 }));
    } else {
      // Wiederholter Crash auf derselben Stellung ODER zu viele Crashes hintereinander → aufgeben.
      this.reportEngineEvent?.('giveup', sameCrash ? `repeat-crash @ ${fen}` : `streak=${this.crashStreak}`);
      this.fatalError$.next('crash');
      this.state$.next({ fen, depth: 0, lines: [], running: false, nodes: 0, nps: 0 });
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

  /** Wählt die External Engine (null = Browser-WASM). Der Transport wird mitgereicht, damit der
   *  Service keine HTTP-Abhängigkeit braucht; ein Wechsel setzt den Fallback-Zustand zurück.
   *  Das erneute analyze() stößt der Aufrufer an (wie bei setMultiPv/setDepth). */
  setRemoteEngine(engine: RemoteEngine | null, transport?: RemoteAnalyseTransport): void {
    this.remoteEngine = engine;
    if (transport) this.remoteTransport = transport;
    this.remoteFallbackSubject.next(false);
    this.stopRemote();
    if (engine) this.stopLocalSearch();   // der WASM-Kern soll nicht weiterrechnen
  }

  /** Läuft im WASM-Worker noch eine Suche, sie beenden (und nichts Neues nachschieben). Ohne das
   *  rechnet der Kern nach dem Umschalten auf die externe Engine bis zur Zieltiefe weiter —
   *  verbrannte CPU/Akku, und sein spätes `bestmove` fiele in eine fremde Suche. */
  private stopLocalSearch(): void {
    this.pendingGoFen = null;
    this.clearWatchdog();
    if (this.searching) this.send('stop');
  }

  /** True, solange die externe Engine die Analyse führt (kein Fallback aktiv). */
  private get remoteActive(): boolean {
    return !!this.remoteEngine && !!this.remoteTransport && !this.remoteFallbackSubject.value;
  }

  /** Startet (oder wechselt) die Analyse auf eine Stellung. */
  async analyze(fen: string): Promise<void> {
    if (this.remoteActive) {
      this.analyzeRemote(fen, this.remoteEngine!, this.remoteTransport!);
      return;
    }
    await this.init();
    // Nach dem await erneut prüfen: der WASM-Handshake dauert (Worker-Start + `readyok`), und
    // genau in dieser Zeit kann die Engine-Liste eintreffen und auf die externe Engine umschalten
    // (ngOnInit-Reihenfolge). Ohne diese zweite Prüfung liefe die fortgesetzte lokale Suche der
    // gerade gestarteten Remote-Suche in die Parade und überschriebe sie.
    if (this.remoteActive) {
      this.analyzeRemote(fen, this.remoteEngine!, this.remoteTransport!);
      return;
    }
    // Neue, vom Nutzer angesteuerte Stellung → Crash-Budget zurücksetzen. Der Recovery-Retry aus
    // handleCrash ruft analyze() mit DERSELBEN FEN auf und setzt deshalb bewusst nichts zurück.
    if (fen !== this.currentFen) { this.crashStreak = 0; this.lastCrashFen = null; }
    this.gen++;
    this.currentFen = fen;
    this.sideToMove = (fen.split(' ')[1] === 'b') ? 'b' : 'w';
    this.partial = new Map();
    this.lastNodes = 0;
    this.lastNps = 0;
    this.clearWatchdog();
    this.state$.next({ fen, depth: 0, lines: [], running: true, nodes: 0, nps: 0 });
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
    this.searchGen = this.gen;   // Zeilen DIESER Suche gehören zu dieser Generation
    this.searching = true;
    this.armWatchdog();   // ab jetzt Info-Lines erwarten
  }

  /** Remote-Suche (samt Erste-Zeile-Wächter) abbrechen; der Abbruch stoppt via Broker den Provider. */
  private stopRemote(): void {
    if (this.remoteFirstLineGuard !== undefined) { clearTimeout(this.remoteFirstLineGuard); this.remoteFirstLineGuard = undefined; }
    this.remoteSub?.unsubscribe();
    this.remoteSub = undefined;
  }

  /** Analyse über die External Engine: der ndjson-Stream des Brokers ersetzt die Worker-info-Zeilen.
   *  Scheitert die Suche VOR der ersten Zeile (Engine/Provider offline, auch: gar keine Antwort
   *  binnen Frist), fällt der Service für den Rest der Sitzung still auf WASM zurück. Ein Abriss
   *  MITTEN im Stream gilt dagegen als beendete Suche — was da ist, bleibt stehen. */
  private analyzeRemote(fen: string, engine: RemoteEngine, transport: RemoteAnalyseTransport): void {
    this.gen++;
    const gen = this.gen;
    this.currentFen = fen;
    this.sideToMove = (fen.split(' ')[1] === 'b') ? 'b' : 'w';
    this.partial = new Map();
    this.lastNodes = 0;
    this.lastNps = 0;
    this.clearWatchdog();
    this.stopRemote();
    this.state$.next({ fen, depth: 0, lines: [], running: true, nodes: 0, nps: 0 });

    let gotData = false;
    const failBeforeData = () => {
      if (gen !== this.gen || gotData) return;
      this.stopRemote();
      this.reportEngineEvent?.('remote_failed', engine.id);
      this.remoteFallbackSubject.next(true);
      this.analyze(fen).catch(() => this.state$.next({ fen, depth: 0, lines: [], running: false, nodes: 0, nps: 0 }));
    };
    this.remoteFirstLineGuard = setTimeout(failBeforeData, 12000);

    this.remoteSub = transport(engine.id, {
      sessionId: this.remoteSessionId,
      initialFen: fen,
      moves: [],
      multiPv: this.multiPv,
      depth: this.depthCap,
      threads: engine.maxThreads,
      // Hash gedeckelt: die volle Registrierungs-Grenze (bis 1 TiB erlaubt) muss der Provider-
      // Rechner nicht für jede Brett-Analyse allozieren.
      hash: Math.min(engine.maxHash, 1024),
    }).subscribe({
      next: line => {
        if (gen !== this.gen) return;
        gotData = true;
        if (this.remoteFirstLineGuard !== undefined) { clearTimeout(this.remoteFirstLineGuard); this.remoteFirstLineGuard = undefined; }
        this.remoteFallbackSubject.next(false);
        // Der Broker liefert `nodes` + verstrichene `time` (ms), aber keine Rate — also selbst
        // rechnen. `time` ist in den ersten Zeilen oft 0: dann den letzten Wert behalten,
        // statt durch null zu teilen (ergäbe Infinity in der Anzeige).
        if (typeof line.nodes === 'number') this.lastNodes = line.nodes;
        if (line.time > 0 && line.nodes > 0) this.lastNps = Math.round(line.nodes * 1000 / line.time);
        this.state$.next({ fen, depth: line.depth ?? 0, lines: this.mapRemoteLine(line),
                           running: true, nodes: this.lastNodes, nps: this.lastNps });
      },
      complete: () => {
        if (gen !== this.gen) return;
        // Stream sauber beendet, aber KEINE einzige Zeile gebracht: dann hat die Engine nicht
        // wirklich geantwortet. Sofort umschalten, statt bis zum Ablauf des Wächters eine
        // leere „läuft"-Anzeige stehen zu lassen.
        if (!gotData) { failBeforeData(); return; }
        const s = this.state$.value;
        if (s.running) this.state$.next({ ...s, running: false });
      },
      error: () => {
        if (gen !== this.gen) return;
        if (gotData) {
          const s = this.state$.value;
          if (s.running) this.state$.next({ ...s, running: false });
          return;
        }
        failBeforeData();
      },
    });
  }

  /** ndjson-Zeile des Brokers → AnalysisLines. cp/mate kommen laut Spez. bereits aus Weiß-Sicht;
   *  Formatierung identisch zu parseInfo, damit die Anzeige beim Umschalten nicht springt. */
  mapRemoteLine(l: EngineAnalyseLine): AnalysisLine[] {
    return (l.pvs ?? []).slice(0, this.multiPv).map((pv, i) => {
      const isMate = pv.mate !== undefined && pv.mate !== null;
      const score = isMate ? pv.mate! : (pv.cp ?? 0);
      let evalText: string;
      if (isMate) {
        evalText = '#' + score;
      } else {
        const v = score / 100;
        evalText = (v > 0 ? '+' : '') + v.toFixed(2);
      }
      return {
        multipv: i + 1,
        depth: pv.depth ?? l.depth ?? 0,
        scoreType: isMate ? 'mate' as const : 'cp' as const,
        score,
        evalText,
        // Der Broker liefert Rochaden als König-schlägt-Turm (`e1h1`); `pvUci` muss aber die
        // Standardform tragen, sonst bricht der SAN-Nachbau der Anzeige an der Rochade ab.
        pvUci: normalizeCastlingUci(this.currentFen, pv.moves ?? []),
      };
    });
  }

  stop(): void {
    this.stopRemote();
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
    // Obergrenze 50 — muss mindestens so hoch sein wie der größte Wert in DEPTH_OPTIONS
    // (analysis.component.ts), sonst würde eine angebotene Tiefe still gekappt. Der Server
    // lässt bis 60 zu (EngineController.MaxDepth).
    this.depthCap = Math.max(6, Math.min(50, Math.round(d)));
  }

  /** Worker-Nachricht parsen (info / bestmove). Generation-geschützt gegen Altzeilen. */
  private onMessage(e: MessageEvent): void {
    const line = e.data;
    if (typeof line !== 'string') return;

    // readyok wird im analyze-Pfad nicht mehr als Gate genutzt (nur init() wartet darauf, mit
    // eigenem Listener). Hier daher nur abfangen, damit es nicht als info-Zeile fehlinterpretiert wird.
    if (line.startsWith('readyok')) return;

    // Gehört diese Zeile noch zur aktuellen Suche? `searchGen` wurde beim Absetzen des `go`
    // festgehalten; eine überholte Suche (Stellungswechsel, Wechsel auf die externe Engine)
    // darf `state$` nicht mehr anfassen — ihre UCI-Sequenz (siehe bestmove) läuft aber weiter.
    const stale = this.searchGen !== this.gen;

    if (line.startsWith('bestmove')) {
      // Die laufende Suche ist beendet (regulär ODER durch `stop`). Erst JETZT ist es sicher,
      // das nächste `go` abzusetzen — steht eine Stellung an, jetzt starten.
      this.searching = false;
      if (this.pendingGoFen !== null) {
        this.launchPending();
        return;
      }
      this.clearWatchdog();   // Suche regulär beendet, nichts steht an
      // Das `bestmove` einer überholten Suche beendet NUR den Worker-Zustand: würde es hier
      // `running: false` setzen, erklärte es die inzwischen laufende (ggf. externe) Suche für fertig.
      if (stale) return;
      const s = this.state$.value;
      if (s.running) this.state$.next({ ...s, running: false });
      return;
    }
    // Leistungswerte stehen in denselben info-Zeilen (Stockfish liefert `nodes` und `nps`
    // mit). Vor dem pv-Filter auslesen, damit auch Zeilen ohne pv die Anzeige aktuell halten.
    if (!stale && line.startsWith('info ')) {
      const nodesMatch = line.match(/\bnodes (\d+)/);
      if (nodesMatch) this.lastNodes = parseInt(nodesMatch[1], 10);
      const npsMatch = line.match(/\bnps (\d+)/);
      if (npsMatch) this.lastNps = parseInt(npsMatch[1], 10);
    }
    if (!line.startsWith('info ') || !line.includes(' pv ')) return;

    const parsed = this.parseInfo(line);
    if (!parsed) return;
    if (stale) return;                    // Stellung hat gewechselt / externe Engine hat übernommen
    if (parsed.multipv > this.multiPv) return;

    this.clearWatchdog();   // Engine antwortet → kein Hänger
    this.crashStreak = 0;   // Engine liefert wieder → Recovery-Zähler zurücksetzen
    this.lastCrashFen = null;
    this.fatalError$.next(null);
    this.partial.set(parsed.multipv, parsed);
    const lines = [...this.partial.values()].sort((a, b) => a.multipv - b.multipv);
    const depth = Math.max(...lines.map(l => l.depth), 0);
    this.state$.next({ fen: this.currentFen, depth, lines, running: true,
                       nodes: this.lastNodes, nps: this.lastNps });
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
    if (this.initTimeout !== undefined) { clearTimeout(this.initTimeout); this.initTimeout = undefined; }
    // Laufenden Handshake aufloesen, BEVOR der Worker stirbt. Sonst kann `readyok` nie mehr
    // kommen, das Promise wird nie erfuellt, und das `await this.init()` in analyze() haengt
    // samt seiner Closure (und der darin gefangenen Component) fuer immer im Speicher.
    // Frueher rettete das der 15-s-Timer — den raeumt die Zeile darueber jetzt weg.
    this.initFail?.('Engine destroyed');
    if (this.worker) {
      try { this.send('stop'); this.send('quit'); } catch {}
      this.worker.terminate();
      this.worker = undefined;
      this.initPromise = undefined;
    }
    this.searching = false;
    this.pendingGoFen = null;
    this.searchGen = -1;
    this.crashStreak = 0;
    this.fatalError$.next(null);
    this.clearWatchdog();
    // Remote-Auswahl mit aufräumen: der Service ist ein Root-Singleton — ohne Reset würde ein
    // späterer Besuch mit einer VERALTETEN Engine-Auswahl remote analysieren, bevor die
    // Component ihre (ggf. geänderte) Auswahl neu verdrahtet hat. Der Transport MUSS mit weg:
    // er ist eine Closure über die zerstörte Component (und damit deren View) — bliebe er
    // hängen, hielte das App-weite Singleton die tote Analyse-Seite am Leben.
    this.stopRemote();
    this.remoteEngine = null;
    this.remoteTransport = undefined;
    this.remoteFallbackSubject.next(false);
    this.state$.next(EMPTY);
  }
}
