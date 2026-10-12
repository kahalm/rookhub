import { Subscription } from 'rxjs';
import { AnalysisEngineService, AnalysisLine } from './analysis-engine.service';
import { EngineChoice } from './analysis-engine-card.component';
import { EngineDisplayLine, toDisplayLines } from './engine-lines.util';
import { ExternalEngineInfo, ExternalEngineService } from './external-engine.service';

/** Vergleichsmodus: an/aus und die Wahl der zweiten Engine (je Gerät gemerkt). */
export const COMPARE_KEY = 'rookhub_analysis_compare';
export const COMPARE_ENGINE_KEY = 'rookhub_analysis_compare_engine';

/** Was der Vergleich vom Analysebrett braucht — alles als Abfrage, damit er immer den aktuellen Stand sieht. */
export interface AnalysisCompareHost {
  /** Die Haupt-Engine (DI-Instanz) — ihr Telemetrie-Hook bekommt auch die Ereignisse der Vergleichs-Engine. */
  readonly mainEngine: AnalysisEngineService;
  readonly externalEngines: ExternalEngineService;
  depthSetting(): number;
  linesCount(): number;
  /** Externe Engines im Live-Picker (ohne Hintergrund-Engines). */
  engines(): ExternalEngineInfo[];
  /** Alle wählbaren Engines (Browser + externe). */
  choices(): EngineChoice[];
  selectedEngineId(): string;
  currentFen(): string;
  /** Darf jetzt gerechnet werden? (Engine an und die Stellung hat legale Züge.) */
  canSearch(): boolean;
  /** View markieren (Angular 22 refresht nach Asynchronem nicht von selbst). */
  changed(): void;
}

/** Vergleichsmodus des Analysebretts: eine ZWEITE Engine rechnet dieselbe Stellung.
 *  Möglich, weil AnalysisEngineService keine DI-Abhängigkeiten hat und sich schlicht ein
 *  zweites Mal instanziieren lässt — jede Instanz hat eigenen Worker, eigenen Zustand und
 *  eigene Generationszählung, die beiden Suchen kommen sich also nicht ins Gehege. */
export class AnalysisCompare {
  on = false;
  /** 'wasm' oder Engine-ID der Vergleichs-Engine. */
  engineId = 'wasm';
  lines: EngineDisplayLine[] = [];
  depth = 0;
  nps = 0;
  /** True, wenn die VERGLEICHS-Engine auf die Browser-Engine zurückgefallen ist. Ohne diese
   *  Anzeige verglichen zwei Etiketten („RookHub PC") etwas, das in Wahrheit die Browser-Engine
   *  gerechnet hat — ein Vergleich, der genau das Gegenteil von dem zeigt, was draufsteht. */
  fallback = false;
  /** Die zweite Instanz hat aufgegeben (Worker-Absturz/Start gescheitert). Ohne diese Anzeige
   *  stünde dort für immer „Berechne…", obwohl nichts mehr rechnet. */
  crashed = false;
  /** Die laufende zweite Instanz (undefined = kein Vergleich). */
  engine?: AnalysisEngineService;
  private sub?: Subscription;
  private fallbackSub?: Subscription;
  private errorSub?: Subscription;

  /** Erzeugung der Vergleichs-Engine als Seam (in Tests ueberschreibbar) — analog zu
   *  createWorker() im Service. Ohne ihn lief in den Compare-Specs der ECHTE Service: auf dem
   *  Remote-Pfad in einen TypeError (der analyse-Spy liefert kein Observable), auf dem
   *  WASM-Pfad in einen echten 7-MB-Worker im Karma-Browser. Die Specs pruefte damit
   *  Vergleichszustand, den nie jemand angetrieben hatte. */
  createEngine: () => AnalysisEngineService = () => new AnalysisEngineService();

  constructor(private host: AnalysisCompareHost) {
    try {
      this.on = localStorage.getItem(COMPARE_KEY) === '1';
      this.engineId = localStorage.getItem(COMPARE_ENGINE_KEY) || 'wasm';
    } catch {}
  }

  /** Läuft der Vergleich WIRKLICH (Schalter an UND zweite Instanz vorhanden)? Das Template
   *  hängt daran statt an `on` — sonst stünde ein Vergleichsblock da, hinter dem gar
   *  keine Engine steckt (etwa abgemeldet, Engine-Liste nicht ladbar) und der ewig „Berechne…"
   *  zeigt. */
  get running(): boolean { return this.on && !!this.engine; }

  /** Vergleich ein/aus (nach dem Umschalten von `on`). Aus = zweite Instanz vollständig abräumen (Worker/Streams beenden). */
  onToggle(): void {
    try { localStorage.setItem(COMPARE_KEY, this.on ? '1' : '0'); } catch {}
    if (this.on) this.start();
    else this.stop();
  }

  onEngineSelect(): void {
    try { localStorage.setItem(COMPARE_ENGINE_KEY, this.engineId); } catch {}
    this.start();
  }

  /** Neue Stellung: alte Ergebnisse weg. `crashed` MUSS mit zurueck: sonst klebt die Absturzmeldung an einer Stellung,
   *  in der die Engine noch gar nicht gerechnet hat. Der Service setzt bei neuer FEN selbst crashStreak zurueck, die
   *  Suche kann also problemlos gelingen — die Karte behauptete trotzdem weiter, die Engine sei abgestuerzt. */
  clearResults(): void {
    this.lines = [];
    this.depth = 0;
    this.nps = 0;
    this.crashed = false;
  }

  /** Sorgt dafür, dass die Vergleichs-Engine eine ANDERE ist als die Haupt-Engine, und merkt
   *  sich die Korrektur. Muss an EINER Stelle passieren, die jeder Weg durchläuft — sonst
   *  entsteht die Selbstvergleichs-Kombination über den Haupt-Picker oder nach einem Neuladen
   *  doch wieder (zwei Instanzen rechnen dann dasselbe, im Browser-Fall zweimal 7 MB WASM). */
  private ensureDistinctEngine(): void {
    const selected = this.host.selectedEngineId();
    if (this.engineId !== selected) return;
    const other = this.host.choices().find(c => c.id !== selected);
    if (!other) return;
    this.engineId = other.id;
    try { localStorage.setItem(COMPARE_ENGINE_KEY, this.engineId); } catch {}
  }

  /** Baut die zweite Engine-Instanz auf (bzw. richtet sie neu aus) und startet ihre Suche. */
  start(): void {
    this.stop();
    if (!this.on) return;
    const engines = this.host.engines();
    // Gespeicherte Wahl kann veraltet sein (Engine abgemeldet, umbenannt): unbekannte ID auf
    // „Browser" zurücksetzen, statt sie stumm als null durchzureichen — das ergäbe eine
    // Browser-Suche unter leerem Etikett.
    if (this.engineId !== 'wasm' && !engines.some(e => e.id === this.engineId)) this.engineId = 'wasm';
    this.ensureDistinctEngine();
    // Blieb nur EINE Engine uebrig (keine externe registriert, Token abgelaufen, Liste leer),
    // konnte ensureDistinctEngine() nichts ausweichen lassen. Dann verglichen sich zwei
    // Instanzen derselben Engine — im Browser-Fall zwei 7-MB-WASM-Kerne, die sich denselben
    // Prozessorkern teilen und sich gegenseitig die Rechenleistung halbieren, fuer zwei
    // garantiert identische Linienlisten. Lieber ehrlich abschalten als das anzubieten.
    if (this.engineId === this.host.selectedEngineId()) {
      this.on = false;
      try { localStorage.setItem(COMPARE_KEY, '0'); } catch {}
      this.host.changed();
      return;
    }
    const engine = this.createEngine();
    // Die DI-Instanz bekommt ihren Telemetrie-Hook in app.component; diese hier wird von Hand
    // gebaut und haette gar keinen. Ausgerechnet der Vergleichsmodus verdoppelt aber den
    // WASM-Speicherdruck und ist damit die wahrscheinlichste Absturzquelle — seine Crashes
    // duerfen nicht die einzigen sein, die nirgends auftauchen.
    engine.reportEngineEvent = (kind, detail) => this.host.mainEngine.reportEngineEvent?.('compare_' + kind, detail);
    engine.setDepth(this.host.depthSetting());
    engine.setMultiPv(this.host.linesCount());
    const info = engines.find(e => e.id === this.engineId) ?? null;
    engine.setRemoteEngine(info, (id, work) => this.host.externalEngines.analyse(id, work));
    this.sub = engine.analysis$.subscribe(st => this.onUpdate(st.fen, st.depth, st.lines, st.nps));
    this.fallbackSub = engine.remoteFallback$.subscribe(f => { this.fallback = f; this.host.changed(); });
    this.errorSub = engine.engineFatalError$.subscribe(e => { this.crashed = e !== null; this.host.changed(); });
    this.engine = engine;
    if (this.host.canSearch()) engine.analyze(this.host.currentFen()).catch(() => {});
  }

  stop(): void {
    this.sub?.unsubscribe();
    this.sub = undefined;
    this.fallbackSub?.unsubscribe();
    this.fallbackSub = undefined;
    this.errorSub?.unsubscribe();
    this.errorSub = undefined;
    this.fallback = false;
    this.crashed = false;
    this.engine?.destroy();
    this.engine = undefined;
    this.lines = [];
    this.depth = 0;
    this.nps = 0;
  }

  /** Anzeigename der Vergleichs-Engine. Ist sie zurückgefallen, wird die TATSÄCHLICH rechnende
   *  Engine genannt — ein Vergleich mit falschem Etikett wäre schlimmer als gar keiner. */
  name(browserName: string): string {
    if (this.fallback) return browserName;
    return this.host.choices().find(c => c.id === this.engineId)?.name ?? '';
  }

  private onUpdate(fen: string, depth: number, lines: AnalysisLine[], nps: number): void {
    if (fen !== this.host.currentFen()) return;   // Antwort einer bereits verlassenen Stellung
    this.depth = depth;
    this.nps = nps;
    // Dieselbe Abbildung wie die Haupt-Engine — eine Nebeneinander-Ansicht, die dieselbe Bewertung links anders
    // einfaerbt als rechts, waere schlimmer als gar kein Vergleich.
    this.lines = toDisplayLines(fen, lines, 12);
    this.host.changed();
  }
}
