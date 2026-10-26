import { Subscription } from 'rxjs';
import type { EngineChoice } from './analysis-engine-card.component';
import { EngineChoiceList, pickEngineAfterLoad } from './analysis-engine-choice';
import { AnalysisEngineService } from './analysis-engine.service';
import { AnalysisCompare } from './analysis-compare';
import { AnalysisSettings } from './analysis-settings';
import { ExternalEngineInfo, ExternalEngineService, ExternalEnginesResponse } from './external-engine.service';

/** Was die Engine-Wahl vom Analysebrett braucht (live gelesen). */
export interface AnalysisEnginePickerHost {
  engine: AnalysisEngineService;
  externalEngines: ExternalEngineService;
  settings: AnalysisSettings;
  compare(): AnalysisCompare;
  t(key: string): string;
  currentLang(): string | null;
  /** Ist die Haupt-Engine auf den Browser zurückgefallen? */
  remoteFallback(): boolean;
  /** Soll die Haupt-Engine jetzt rechnen (Engine an bzw. stilles Sparring, und es gibt Züge)? */
  mainShouldRun(): boolean;
  /** Haupt-Engine auf der aktuellen Stellung neu starten. */
  runMain(): void;
  changed(): void;
}

/**
 * Engine-Wahl des Analysebretts: die External Engines des Lichess-Kontos laden, die passende wählen (die eines
 * Auftrags, sonst die gemerkte), die Liste fürs Auswahlfeld und das Verdrahten der Wahl in den Engine-Service.
 * 'wasm' = Browser.
 */
export class AnalysisEnginePicker {
  /** External Engines des Lichess-Kontos (leer = kein Picker); Auswahl 'wasm' = Browser. */
  list: ExternalEngineInfo[] = [];
  selectedId = 'wasm';
  /** Alle als Hintergrund gewaehlten Engines — der Live-Picker blendet sie aus. */
  backgroundIds: string[] = [];
  /** Per `?engine=` gewünschte Engine (von der Auftragsseite) — gilt einmalig für diesen Aufruf. */
  requestedId: string | null = null;
  /** Mindestens eine externe Engine registriert (inkl. Hintergrund-Engine) → Aufträge sind möglich. */
  hasExternal = false;
  private sub?: Subscription;
  /** Alle wählbaren Engines (Browser + registrierte externe) — für beide Auswahlfelder, memoisiert. */
  private readonly choiceList = new EngineChoiceList();

  constructor(private readonly host: AnalysisEnginePickerHost) {}

  /** External Engines laden. Subscription festhalten: der Engine-Service ist ein App-weites Singleton — verlässt der
   *  Nutzer die Seite, WÄHREND die Liste noch unterwegs ist, würde die Antwort sonst eine Engine im längst
   *  zerstörten Zustand scharf schalten und eine Analyse starten, die niemand mehr sieht. */
  load(): void {
    this.sub = this.host.externalEngines.listEngines().subscribe({ next: r => this.onLoaded(r), error: () => {} });
  }

  destroy(): void { this.sub?.unsubscribe(); }

  /** Liste da: Picker füllen und die passende Engine wählen ({@link pickEngineAfterLoad}) — die eines Auftrags, sonst
   *  die zuletzt gewählte (die evtl. schon laufende WASM-Analyse der Startstellung wechselt dann auf die Remote-Suche). */
  private onLoaded(r: ExternalEnginesResponse): void {
    const pick = pickEngineAfterLoad(r, this.requestedId, this.host.settings.loadProvider());
    this.backgroundIds = pick.backgroundEngineIds;
    this.hasExternal = pick.hasExternalEngines;
    this.list = pick.list;
    if (pick.selected) {
      this.selectedId = pick.selected;
      this.apply();
    }
    if (pick.fromRequest) {
      this.requestedId = null;   // nur für diesen Aufruf, nicht als Dauerwahl merken
      this.host.changed();
      return;
    }
    // NICHT unbedingt: apply() oben startet den Vergleich bereits selbst,
    // wenn Haupt- und Vergleichswahl kollidieren. Ohne diese Bedingung wuerde die eben
    // gebaute Instanz Millisekunden spaeter wieder zerstoert und neu aufgebaut — im
    // Browser-Fall eine 7-MB-WASM-Instanziierung fuer nichts.
    const compare = this.host.compare();
    if (compare.on && !compare.engine) compare.start();
    this.host.changed();   // sonst erscheint der Picker erst beim nächsten DOM-Event
  }

  get choices(): EngineChoice[] {
    // ngx-translate 18: currentLang ist ein Signal, kein String
    return this.choiceList.get(this.host.currentLang(), this.list, () => this.host.t('analysis.engineBrowser'));
  }

  /** Anzeigename der Haupt-Engine — im Vergleichsmodus muss beschriftet sein, welche welche ist. */
  get mainName(): string {
    if (this.host.remoteFallback() && this.selectedId !== 'wasm') return this.host.t('analysis.engineBrowser');
    return this.choices.find(c => c.id === this.selectedId)?.name ?? '';
  }

  /** Auswahl geändert: merken und verdrahten. */
  select(): void {
    this.host.settings.saveProvider(this.selectedId);
    this.apply();
  }

  /** Verdrahtet die aktuelle Auswahl in den Engine-Service (Transport = HTTP-Proxy) und startet neu. */
  private apply(): void {
    const info = this.list.find(e => e.id === this.selectedId) ?? null;
    this.host.engine.setRemoteEngine(info, (id, work) => this.host.externalEngines.analyse(id, work));
    // Wandert die Haupt-Engine auf die, die gerade als Vergleich läuft, muss die Vergleichs-
    // seite ausweichen — sonst rechnen beide Instanzen dasselbe.
    // ensureDistinctEngine() NICHT hier aufrufen: compare.start() macht es als zweiten
    // Schritt ohnehin. Zwei Aufrufstellen fuer dieselbe Invariante lesen sich, als sicherten sie
    // Verschiedenes ab, und laden dazu ein, nur eine davon zu „reparieren".
    const compare = this.host.compare();
    if (compare.on && compare.engineId === this.selectedId) compare.start();
    if (this.host.mainShouldRun()) this.host.runMain();
  }
}
