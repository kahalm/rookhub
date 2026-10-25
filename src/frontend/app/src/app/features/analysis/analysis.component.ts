import { Component, HostListener, OnDestroy, OnInit, ChangeDetectionStrategy, ChangeDetectorRef, Inject, LOCALE_ID } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { Color, Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import { Subscription, interval } from 'rxjs';
import { EngineDisplayLine, formatElapsed as formatElapsedUtil, formatKiloNodes, formatKiloNps, toDisplayLines as toDisplayLinesUtil, uciLineToSan as uciLineToSanUtil } from './engine-lines.util';
import { AnalysisBoardComponent } from './analysis-board.component';
import { AnalysisEngineCardComponent, EngineChoice, arrowBrush } from './analysis-engine-card.component';
import { PositionSetupComponent } from './position-setup.component';
import { AnalysisEngineService, AnalysisLine, RemoteInterruption } from './analysis-engine.service';
import { ExternalEngineService, ExternalEngineInfo, ExternalEnginesResponse, engineTagKey } from './external-engine.service';
import { mediaSignal } from '../../shared/layout/wide-media';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { SnackbarService } from '../../core/snackbar.service';
import { PositionRepertoiresComponent } from '../repertoire/position-repertoires.component';
import { OpeningExplorerComponent } from './opening-explorer.component';
import { AuthService } from '../../core/auth.service';
import { AnalysisSettings, DEPTH_OPTIONS, EVAL_SETTLE_DEPTH } from './analysis-settings';
import { AnalysisRouteParams, readAnalysisParams } from './analysis-route';
import { PositionMenuComponent } from './position-menu.component';
import { MarkPositionButtonComponent } from '../../shared/mark-position/mark-position-button.component';
import { MarkOrigin } from '../../shared/mark-position/marked-positions.service';
import { MatDialog } from '@angular/material/dialog';
import { AnalysisHistoryEntry, AnalysisHistoryService } from './analysis-history.service';
import { AnalysisHistoryDialogComponent } from './analysis-history-dialog.component';
import { AnalysisHistorySaver } from './analysis-history-saver';
import { AnalysisCompare } from './analysis-compare';
import {
  AnalysisNode, addMove, createRoot, fromDto, isWithin, lineThrough, mainline, makeMainline, numberedSan, parsePgnTree, pathTo,
  playSan, playUci, promote, removeNode, starredNodes,
} from './analysis-tree';
import { AnalysisMoveTreeComponent, MoveTreeAction } from './analysis-move-tree.component';
import { MaiaEngineService } from './maia/maia-engine.service';
import { MaiaSparring } from './maia/maia-sparring';
import { MaiaSparringCardComponent } from './maia/maia-sparring-card.component';
import { TablebasePanelComponent, TablebaseVerdict } from './tablebase-panel.component';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { GamesService } from '../games/games.service';
import { AnalyzeGameService } from '../games/analyze-game.service';

const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-analysis',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatTooltipModule, TranslatePipe, AnalysisBoardComponent, AnalysisEngineCardComponent, PositionSetupComponent,
    PositionRepertoiresComponent, OpeningExplorerComponent, PositionMenuComponent, MarkPositionButtonComponent, AnalysisMoveTreeComponent,
    IconLabelDirective, MaiaSparringCardComponent, TablebasePanelComponent
  ],
  templateUrl: './analysis.component.html',
  styleUrl: './analysis.component.scss',
})
export class AnalysisComponent implements OnInit, OnDestroy {
  /** Breitbild-Stufe (u1-rk-analyse-3spalten, ab 2400 px): drei Spalten, der Explorer steht offen in der dritten. */
  readonly wideLayout = mediaSignal();
  /** Herkunft für den „+"-Knopf (0.749.0): das Analysebrett kennt keine Partie. */
  readonly boardMarkOrigin: MarkOrigin = { context: 'board' };
  /** Zugbaum der Analyse (0.604.0): die Wurzel ist die Ausgangsstellung, `children[0]` jeweils die Fortsetzung. */
  root: AnalysisNode = createRoot(START_FEN);
  /** Die Linie durch den aktuellen Knoten: der Weg dorthin und seine Fortsetzung — Pfeiltasten laufen auf ihr. */
  line: AnalysisNode[] = [];
  /** Wie weit man auf `line` steht (0 = Ausgangsstellung). */
  ply = 0;
  /** Ergebnis der Endspiel-Datenbank für die aktuelle Stellung (0.729.0) — schlägt die Engine-Bewertung in der Leiste. */
  tbVerdict: TablebaseVerdict | null = null;
  /** Zählt Änderungen am Baum (Zug, Variante, Stern, Bewertung) — die Zugliste baut danach neu auf. */
  treeVersion = 0;

  orientation: Color = 'white';
  boardFen = START_FEN;
  turnColor: Color = 'white';
  dests = new Map<Key, Key[]>();
  lastMove?: [Key, Key];
  isCheck = false;
  shapes: DrawShape[] = [];

  engineOn = true;
  linesCount = 3;
  depth = 0;
  depthSetting = 22;
  readonly depthOptions = DEPTH_OPTIONS;
  returnTo: string | null = null;
  displayLines: EngineDisplayLine[] = [];
  /** Erste Züge der aktuellen Engine-Linien samt Bewertung — „Züge vergleichen" wählt sie vor. */
  engineCandidates: { uci: string; evalText: string }[] = [];
  evalText = '0.00';
  whiteHeight = 50;
  engineCrashed = false;

  fenInput = '';
  pgnInput = '';
  editing = false;


  /** Sparring gegen Maia (0.632.0) — Zustand und Spiel in MaiaSparring. */
  readonly spar: MaiaSparring;
  /** Karte „Sparring gegen Maia" eingeblendet (0.760.0: standardmäßig zu, über das ⋮ der Stellung; nicht gemerkt). */
  maiaCardOpen = false;
  /** Läuft ein Sparring, ist die Karte immer da — sonst nur, wenn über das ⋮ geöffnet. */
  get maiaCardVisible(): boolean { return this.maiaCardOpen || !!this.spar.sparring; }

  // ---- Analyse-Verlauf + Sterne (0.603.0; Sterne seit 0.604.0 am Knoten des Zugbaums) ----
  private readonly historySaver: AnalysisHistorySaver;
  /** Kennung des Verlauf-Eintrags dieser Analyse (null = neue Analyse, der Server vergibt sie beim ersten Speichern). */
  get historyId(): number | null { return this.historySaver.id; }

  /** External Engines des Lichess-Kontos (leer = kein Picker); Auswahl 'wasm' = Browser. */
  externalEnginesList: ExternalEngineInfo[] = [];
  selectedEngineId = 'wasm';
  /** Im Profil gewählte Hintergrund-Engine — gehört den Aufträgen, fehlt deshalb im Live-Picker. */
  /** Alle als Hintergrund gewaehlten Engines — der Live-Picker blendet sie aus. */
  backgroundEngineIds: string[] = [];
  /** Per `?engine=` gewünschte Engine (von der Auftragsseite) — gilt einmalig für diesen Aufruf. */
  private requestedEngineId: string | null = null;
  /** Mindestens eine externe Engine registriert (inkl. Hintergrund-Engine) → Aufträge sind möglich. */
  hasExternalEngines = false;
  remoteFallback = false;
  /** Abriss der Remote-Suche vor der Zieltiefe (Hinweis in der Karte; null = keiner). */
  remoteCut: RemoteInterruption | null = null;
  private cutSub?: Subscription;
  /** Lebenszeichen: läuft die Suche, und wie viele Sekunden kam keine neue Engine-Zeile? (nur Anzeige) */
  running = false;
  sinceUpdateSec = 0;
  private lastUpdateAt = Date.now();
  private tickSub?: Subscription;
  /** Suchzeit der aktuellen Stellung — startet mit dem ersten „läuft" einer Stellung, läuft über eine
   *  Fortsetzung nach Abriss weiter und friert beim Ende ein. Permanent im Kopf neben „Tiefe x/y". */
  searchElapsedSec = 0;
  private searchStartedAt = Date.now();
  private searchEndedAt: number | null = null;
  private searchFen = '';
  /** Uhrquelle (in Tests überschreibbar). */
  private nowFn: () => number = () => Date.now();
  /** Suchleistung der laufenden Analyse (0 = noch kein Messwert). */
  nodes = 0;
  nps = 0;
  /** Partie-Ende in der aktuellen Stellung (keine legalen Züge) — dort rechnet keine Engine. */
  terminal: 'mate-white-wins' | 'mate-black-wins' | 'stalemate' | null = null;

  /** Vergleichsmodus: eine ZWEITE Engine rechnet dieselbe Stellung (Zustand + zweite Instanz in AnalysisCompare). */
  readonly compare: AnalysisCompare;

  private sub?: Subscription;
  private errorSub?: Subscription;
  private fallbackSub?: Subscription;
  private enginesSub?: Subscription;
  /** Gemerkte Einstellungen (localStorage, je Gerät). */
  private readonly settings = new AnalysisSettings();

  constructor(private engine: AnalysisEngineService, private route: ActivatedRoute, private snackbar: SnackbarService,
              private router: Router, public auth: AuthService, private externalEngines: ExternalEngineService,
              private cdr: ChangeDetectorRef, private translate: TranslateService,
              @Inject(LOCALE_ID) private locale: string, private history: AnalysisHistoryService, private dialog: MatDialog,
              maia: MaiaEngineService, games: GamesService, analyzeGame: AnalyzeGameService) {
    this.historySaver = new AnalysisHistorySaver({
      save: req => this.history.save(req),
      loggedIn: () => this.auth.isLoggedIn,
      snapshot: () => ({ root: this.root, current: this.currentNode }),
      blankFen: START_FEN,
    });
    this.spar = new MaiaSparring({
      maia, games, analyzeGame, auth, snackbar, translate, router,
      root: () => this.root, currentNode: () => this.currentNode, turnColor: () => this.turnColor,
      hasMoves: () => this.dests.size > 0, editing: () => this.editing,
      engineOn: () => this.engineOn, setEngineOn: on => { this.engineOn = on; },
      setOrientation: color => { this.orientation = color; }, running: () => this.running,
      depthSetting: () => this.depthSetting, goToNode: node => this.goToNode(node), refresh: () => this.refresh(),
      treeChanged: () => { this.treeVersion++; }, changed: () => this.cdr.markForCheck(),
    });
    this.compare = new AnalysisCompare({
      mainEngine: engine, externalEngines,
      depthSetting: () => this.depthSetting, linesCount: () => this.linesCount,
      engines: () => this.externalEnginesList, choices: () => this.engineChoices,
      selectedEngineId: () => this.selectedEngineId, currentFen: () => this.currentFen,
      canSearch: () => this.engineOn && this.dests.size > 0, changed: () => this.cdr.markForCheck(),
    });
    ({ linesCount: this.linesCount, engineOn: this.engineOn, depthSetting: this.depthSetting } =
      this.settings.load({ linesCount: this.linesCount, engineOn: this.engineOn, depthSetting: this.depthSetting }));
  }

  ngOnInit(): void {
    const p = readAnalysisParams(this.route.snapshot.queryParamMap, fen => this.isValidFen(fen));
    this.applyRouteParams(p);
    this.connectEngine();
    // External Engines des Lichess-Kontos (nur eingeloggt; stiller Hintergrund-Feed — ohne Liste bleibt es beim
    // Browser-WASM).
    if (this.auth.isLoggedIn) this.loadExternalEngines();
    this.loadInitialPosition(p);
  }

  /** Vorgaben der Adresse übernehmen. Von der Auftragsseite: Engine + Suchparameter des Auftrags, damit hier
   *  WEITERgerechnet wird statt neu zu beginnen (gleiche Engine = warmer Hash) — nur für diesen Aufruf, bewusst
   *  NICHT als Dauereinstellung gespeichert. */
  private applyRouteParams(p: AnalysisRouteParams): void {
    this.root = createRoot(p.fen ?? START_FEN);
    if (p.orientation) this.orientation = p.orientation;
    if (p.returnTo) this.returnTo = p.returnTo;
    if (p.engine) this.requestedEngineId = p.engine;
    if (p.depth != null) this.depthSetting = p.depth;
    if (p.lines != null) this.linesCount = p.lines;
  }

  /** Engine einstellen und ihre Ströme abonnieren (Linien, Absturz, Rückfall, Abriss) samt Sekundentakt. */
  private connectEngine(): void {
    this.engine.setDepth(this.depthSetting);
    this.engine.setMultiPv(this.linesCount);
    this.sub = this.engine.analysis$.subscribe(s => {
      const wasRunning = this.running;
      const now = this.nowFn();
      this.running = s.running;
      this.lastUpdateAt = now;
      if (s.running && (!wasRunning || s.fen !== this.searchFen)) {
        this.searchStartedAt = now; this.searchEndedAt = null; this.searchFen = s.fen;
      } else if (!s.running && wasRunning) {
        this.searchEndedAt = now;
      }
      this.onEngineUpdate(s.fen, s.depth, s.lines, s.nodes, s.nps);
    });
    this.errorSub = this.engine.engineFatalError$.subscribe(e => { this.engineCrashed = e !== null; this.cdr.markForCheck(); });
    this.fallbackSub = this.engine.remoteFallback$.subscribe(f => { this.remoteFallback = f; this.cdr.markForCheck(); });
    this.cutSub = this.engine.remoteInterrupted$.subscribe(c => { this.remoteCut = c; this.cdr.markForCheck(); });
    // Sekundentakt nur für „rechnet seit …": bei MultiPV 5 vergehen ab Tiefe ~27 Minuten ohne neue
    // Zeile — ohne sichtbare Uhr sieht das aus wie ein Hänger. markForCheck nur bei Wertänderung.
    this.tickSub = interval(1000).subscribe(() => this.updateClocks());
  }

  /** External Engines laden. Subscription festhalten: der Engine-Service ist ein App-weites Singleton — verlässt der
   *  Nutzer die Seite, WÄHREND die Liste noch unterwegs ist, würde die Antwort sonst eine Engine im längst
   *  zerstörten Zustand scharf schalten und eine Analyse starten, die niemand mehr sieht. */
  private loadExternalEngines(): void {
    this.enginesSub = this.externalEngines.listEngines().subscribe({ next: r => this.onEnginesLoaded(r), error: () => {} });
  }

  /** Liste da: Picker füllen und die passende Engine wählen — die eines Auftrags, sonst die zuletzt gewählte (die
   *  evtl. schon laufende WASM-Analyse der Startstellung wechselt dann auf die Remote-Suche). */
  private onEnginesLoaded(r: ExternalEnginesResponse): void {
    // Die Hintergrund-Engine gehört den Aufträgen — im Live-Picker (und im Vergleich) taucht sie
    // nicht auf, sonst konkurrierten zwei Suchen um dieselben Kerne.
    this.backgroundEngineIds = r.backgroundEngineIds ?? [];
    this.hasExternalEngines = r.engines.length > 0;
    this.externalEnginesList = r.engines.filter(e => !this.backgroundEngineIds.includes(e.id));
    // Kam der Aufruf von einem Auftrag („im Analysebrett öffnen"), gilt DESSEN Engine — auch wenn es
    // die Hintergrund-Engine ist, die hier sonst ausgeblendet wird. Der Provider hat die Stellung noch
    // im Hash, die Suche ist damit sofort wieder auf der erreichten Tiefe statt bei null.
    const wanted = this.requestedEngineId;
    if (wanted && r.engines.some(e => e.id === wanted)) {
      if (!this.externalEnginesList.some(e => e.id === wanted))
        this.externalEnginesList = [...this.externalEnginesList, r.engines.find(e => e.id === wanted)!];
      this.selectedEngineId = wanted;
      this.applyEngineSelection();
      this.requestedEngineId = null;   // nur für diesen Aufruf, nicht als Dauerwahl merken
      this.cdr.markForCheck();
      return;
    }
    const stored = this.settings.loadProvider();
    if (stored && stored !== 'wasm' && this.externalEnginesList.some(e => e.id === stored)) {
      this.selectedEngineId = stored;
      this.applyEngineSelection();
    }
    // NICHT unbedingt: applyEngineSelection() oben startet den Vergleich bereits selbst,
    // wenn Haupt- und Vergleichswahl kollidieren. Ohne diese Bedingung wuerde die eben
    // gebaute Instanz Millisekunden spaeter wieder zerstoert und neu aufgebaut — im
    // Browser-Fall eine 7-MB-WASM-Instanziierung fuer nichts.
    if (this.compare.on && !this.compare.engine) this.compare.start();
    this.cdr.markForCheck();   // sonst erscheint der Picker erst beim nächsten DOM-Event
  }

  /** Erste Stellung: ein Verlauf-Eintrag (`?history=`), eine Partie (Router-State oder `?pgn=`), eine Zugfolge
   *  (`?moves=`, vom „Analysieren"-Knopf der Puzzles: ans Ende springen) oder nur die Ausgangsstellung. */
  private loadInitialPosition(p: AnalysisRouteParams): void {
    // Eine ganze Partie kann per Router-State übergeben werden (z.B. „In Analyse öffnen" im Bereich „Partien") — zu
    // lang für einen Query-Param; über Seitengrenzen kommt der State nicht an, dann gilt `?pgn=` (LeagueHub, 0.592.0).
    const statePgn = (window.history.state && window.history.state.pgn) as string | undefined;
    const pgn = typeof statePgn === 'string' && statePgn.trim() ? statePgn : p.pgn;
    if (p.historyId != null && this.auth.isLoggedIn) {
      this.resetToStart();
      this.history.get(p.historyId).subscribe({ next: e => this.openHistoryEntry(e), error: () => {} });
      return;
    }
    if (pgn) {
      this.pgnInput = pgn;
      this.loadPgn(true);      // aus einer Partie hergesprungen: das PGN bleibt im Feld stehen (kopierbar, Wunsch 2026-09-28)
    } else if (p.moves.length) {
      this.loadFromUci(p.fen ?? START_FEN, p.moves);
    } else {
      this.resetToStart(p.fen ?? START_FEN);
    }
  }

  /** Baut die Hauptlinie aus UCI-Zügen ab `fromFen` und springt ans Ende (aktuelle Stellung); am ersten Zug, der nicht
   *  geht, ist Schluss. */
  private loadFromUci(fromFen: string, uciMoves: string[]): void {
    if (!this.isValidFen(fromFen)) { this.resetToStart(START_FEN); return; }
    const root = createRoot(fromFen);
    let node = root;
    for (const u of uciMoves) {
      const next = playUci(node, u);
      if (!next) break;
      node = next;
    }
    this.setTree(root, node);
  }

  ngOnDestroy(): void {
    // Wer die Seite verlässt, bevor die Drossel abläuft, soll den letzten Stand trotzdem im Verlauf haben.
    this.historySaver.flush();
    this.sub?.unsubscribe();
    this.errorSub?.unsubscribe();
    this.fallbackSub?.unsubscribe();
    this.cutSub?.unsubscribe();
    this.tickSub?.unsubscribe();
    this.enginesSub?.unsubscribe();
    this.compare.stop();         // eigene Instanz + deren Worker/Streams beenden
    this.engine.destroy();
    this.spar.destroy();         // Maia freigeben, eine späte Antwort verfällt
  }

  // ---- Navigation ----
  get startFen(): string { return this.root.fen; }
  /** Der Knoten, dessen Stellung auf dem Brett steht (die Wurzel = Ausgangsstellung). */
  get currentNode(): AnalysisNode { return this.ply === 0 ? this.root : this.line[this.ply - 1]; }
  get currentFen(): string { return this.currentNode.fen; }

  goTo(ply: number): void {
    this.spar.abortMaia();
    this.ply = Math.max(0, Math.min(ply, this.line.length));
    this.refresh();
  }

  /** Auf einen beliebigen Knoten springen (Klick in Zugliste, Variante, Sternliste) — die Linie läuft dann durch ihn. */
  goToNode(node: AnalysisNode): void {
    this.spar.abortMaia();
    this.line = lineThrough(node);
    this.ply = pathTo(node).length;
    this.refresh();
  }

  /** Einen neuen Baum aufs Brett (Laden, Zurücksetzen) und dort auf `current` stehen. */
  private setTree(root: AnalysisNode, current: AnalysisNode = root): void {
    this.spar.onNewTree();   // beendet ein Sparring (Maia-Regel 9), „Partie analysieren" verfällt
    this.root = root;
    this.treeVersion++;
    this.goToNode(current);
  }
  prev(): void { this.goTo(this.ply - 1); }
  next(): void { this.goTo(this.ply + 1); }

  @HostListener('window:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    if (!isBoardHotkey(e)) return;
    if (e.key === 'ArrowLeft') { e.preventDefault(); this.prev(); }
    else if (e.key === 'ArrowRight') { e.preventDefault(); this.next(); }
    else if (e.key === 'Home') { e.preventDefault(); this.goTo(0); }
    else if (e.key === 'End') { e.preventDefault(); this.goTo(this.line.length); }
    else if ((e.key === 's' || e.key === 'S') && !e.ctrlKey && !e.metaKey && !e.altKey) { e.preventDefault(); this.toggleStar(); }
  }

  // ---- User move ----
  onMove(ev: { orig: Key; dest: Key; promotion?: string }): void {
    let c: Chess;
    try { c = new Chess(this.currentFen); } catch { return; }
    const mover: Color = c.turn() === 'w' ? 'white' : 'black';
    const piece = c.get(ev.orig as any);
    const isPromo = piece?.type === 'p' && (ev.dest[1] === '8' || ev.dest[1] === '1');
    // Umwandlungsfigur kommt jetzt aus dem Picker; Dame nur als Fallback.
    const promotion = isPromo ? (ev.promotion ?? 'q') : undefined;
    let mv;
    try {
      mv = c.move({ from: ev.orig, to: ev.dest, promotion });
    } catch { this.refresh(); return; }   // illegaler Zug -> Brett zurücksetzen
    if (!mv) { this.refresh(); return; }

    // „Schlechte Züge melden": die Stellung davor samt ihrer Bewertungs-Spur merken, BEVOR goToNode sie verlässt.
    this.spar.beforeBoardMove(this.currentNode, mover);
    // Zugbaum (0.604.0): ein anderer Zug als die Fortsetzung wird eine VARIANTE, derselbe Zug geht in die vorhandene.
    const node = addMove(this.currentNode, { san: mv.san, uci: mv.from + mv.to + (mv.promotion ?? ''), fen: c.fen() });
    this.treeVersion++;
    this.goToNode(node);
    this.spar.afterBoardMove(node, mover);   // Sparring: auf den EIGENEN Zug antwortet Maia
  }

  /** Baummodus des Repertoire-Panels bzw. Explorer: die geklickte Zugfolge ab der aktuellen Stellung aufs Brett
   * spielen (wie selbst gezogen — weicht sie von der Fortsetzung ab, wird sie eine Variante). Illegale/unbekannte
   * SAN brechen still ab, statt die Linie halb zu zerschießen. */
  playRepertoireMoves(sans: string[]): void {
    if (!sans?.length) return;
    let node = this.currentNode;
    for (const san of sans) {
      const next = playSan(node, san);
      if (!next) break;
      node = next;
    }
    if (node === this.currentNode) return;
    this.treeVersion++;
    this.goToNode(node);
    this.spar.noteSparringMove(node);
    // Sparring: ein Zug aus Explorer/Repertoire zählt wie ein eigener — steht danach Maia am Zug, antwortet sie. Zog die
    // Folge für Maias Seite, ist danach der Nutzer dran, und es kommt (wie in onMove) keine Anfrage.
    if (this.spar.maiaToMove) this.spar.requestMaiaMove();
  }

  // ---- Refresh board + engine for current ply ----
  private refresh(): void {
    this.historySaver.schedule();
    const fen = this.currentFen;
    let c: Chess;
    try { c = new Chess(fen); } catch { return; }
    this.boardFen = fen;
    this.turnColor = c.turn() === 'w' ? 'white' : 'black';
    this.isCheck = c.isCheck();
    this.dests = this.computeDests(c);
    const lm = this.ply > 0 ? this.currentNode.uci : undefined;
    this.lastMove = lm ? [lm.substring(0, 2) as Key, lm.substring(2, 4) as Key] : undefined;
    this.shapes = [];
    this.displayLines = [];
    this.engineCandidates = [];
    this.depth = 0;
    this.nodes = 0;
    this.nps = 0;
    // Terminale Stellung (Matt/Patt → keine legalen Züge): der Engine kein `go` schicken. Ein
    // Suchlauf ohne legale Züge ist sinnlos und ein vermeidbarer Sonderfall im WASM-Kern.
    // Das Ergebnis MUSS dann aber benannt werden: sonst stünde dort dauerhaft „Berechne…",
    // obwohl nichts mehr gerechnet wird und auch nichts mehr zu rechnen ist.
    this.terminal = this.dests.size > 0 ? null : this.terminalStateOf(c);
    this.compare.clearResults();
    // Die Bewertungs-Spur gehört zur Stellung — die der alten hat ein eigener Zug vorher übernommen (pendingBefore).
    this.spar.evalTrack = { fen, points: [] };
    if (this.engineOn && this.dests.size > 0) {
      this.runAnalysis(this.engine, fen);
      this.runAnalysis(this.compare.engine, fen);
    } else if (this.spar.sparringEngineQuiet && this.dests.size > 0) {
      // Sparring mit „Schlechte Züge melden"/„Bewertungsleiste anlassen": nur die Haupt-Engine rechnet, still.
      this.compare.engine?.stop();
      this.runAnalysis(this.engine, fen);
      if (!this.spar.keepEvalBar) this.updateEval(null);
    } else {
      this.engine.stop();
      this.compare.engine?.stop();
      this.updateEval(null);
    }
  }

  /** Matt oder Patt? Nur aufrufen, wenn es keine legalen Züge gibt. Wirft nicht: bei einer
   *  illegalen Stellung (Buch-Diagramme ohne König) liefert chess.js keinen Zustand — dann
   *  lieber gar keine Aussage als eine falsche. */
  private terminalStateOf(c: Chess): 'mate-white-wins' | 'mate-black-wins' | 'stalemate' | null {
    try {
      if (c.isStalemate()) return 'stalemate';
      // Matt heißt: die Seite AM ZUG hat verloren.
      if (c.isCheckmate()) return c.turn() === 'w' ? 'mate-black-wins' : 'mate-white-wins';
    } catch { /* illegale Stellung */ }
    return null;
  }

  /** Übersetzter Satz für das Partie-Ende (leer, wenn die Stellung nicht terminal ist). */
  get terminalText(): string {
    switch (this.terminal) {
      case 'mate-white-wins': return this.translate.instant('analysis.mateWhiteWins');
      case 'mate-black-wins': return this.translate.instant('analysis.mateBlackWins');
      case 'stalemate': return this.translate.instant('analysis.stalemate');
      default: return '';
    }
  }

  private computeDests(c: Chess): Map<Key, Key[]> {
    const map = new Map<Key, Key[]>();
    for (const m of c.moves({ verbose: true }) as any[]) {
      const arr = map.get(m.from) || [];
      arr.push(m.to);
      map.set(m.from, arr);
    }
    return map;
  }

  // ---- Engine updates ----
  private onEngineUpdate(fen: string, depth: number, lines: AnalysisLine[], nodes = 0, nps = 0): void {
    const quiet = !this.engineOn && this.spar.sparringEngineQuiet;
    if (!(this.engineOn || quiet) || fen !== this.currentFen) return;
    this.nodes = nodes;
    this.nps = nps;
    // Angular 22 refresht eine unmarkierte View nach async/HTTP NICHT mehr von selbst (siehe
    // CLAUDE.md-Konvention). Beim WASM-Pfad kaschieren Worker-/Event-Ticks das noch; bei der
    // externen Engine kommen die Zeilen NUR aus einem HTTP-Stream — ohne diese Marke bliebe die
    // Linienliste stehen, obwohl der Zustand längst stimmt.
    this.cdr.markForCheck();
    this.depth = depth;
    const best = lines[0];
    if (best) this.spar.trackEval(fen, best);
    // Stille Engine im Sparring: Linien, Kandidaten und Pfeile verrieten den besten Zug — sie bleiben leer.
    if (!quiet) {
      this.displayLines = this.toDisplayLines(fen, lines);
      this.engineCandidates = lines.filter(l => !!l.pvUci[0]).map(l => ({ uci: l.pvUci[0], evalText: l.evalText }));
      this.shapes = lines.map((l, i) => {
        const u = l.pvUci[0];
        return u ? { orig: u.substring(0, 2) as Key, dest: u.substring(2, 4) as Key, brush: arrowBrush(i),
          // d5-rk-analyse-pfeile-nur-farbe: Nummer auf dem Pfeil = Marke in der Linienliste (nicht nur über Farbe).
          label: { text: String(i + 1) } } as DrawShape : null;
      }).filter((s): s is DrawShape => !!s);
    }
    // Bewertungsleiste HALTEN, bis die neue Suche etwas Belastbares liefert. Jede Suche beginnt mit
    // einem Zwischenstand ohne Linien; früher sprang die Leiste darauf auf 0.00 und erst Sekunden
    // später (externe Engine: Netzweg + Anlauf) auf den echten Wert — bei jedem Zug ein Ausschlag
    // zur Mitte und zurück. Die Bewertung ist aus Weiß-Sicht, der Wert der Vorstellung liegt also
    // meist nah am neuen. Übernommen wird ab EVAL_SETTLE_DEPTH, bei Matt sofort (ein gefundenes
    // Matt ist auch flach verlässlich) und sobald die Suche endet, egal wie tief sie kam.
    // Engine aus / Partie-Ende setzen die Leiste weiterhin direkt über refresh() → updateEval(null).
    // Im Sparring bekommen Leiste und Zugliste die Bewertung nur mit „Bewertungsleiste anlassen".
    if (best && (!quiet || this.spar.keepEvalBar)
        && (best.depth >= EVAL_SETTLE_DEPTH || best.scoreType === 'mate' || !this.running)) {
      this.updateEval(best);
      // Die Bewertung wandert an den Zug in der Zugliste (wie auf Lichess) und mit dem Baum in den Verlauf.
      const node = this.currentNode;
      if (node !== this.root && node.evalText !== best.evalText) {
        node.evalText = best.evalText;
        this.treeVersion++;
        this.historySaver.schedule();
      }
    }
    this.spar.serveMoveCheck(fen);
  }

  /** Engine-Linien in Anzeigezeilen. Beide Engine-Seiten MUESSEN hier durch: eine
   *  Nebeneinander-Ansicht, die dieselbe Bewertung links anders einfaerbt als rechts, waere
   *  schlimmer als gar kein Vergleich. Frueher lag die Abbildung zweimal im Code, inklusive der
   *  feinen Unterscheidung `score > 0` (Matt) gegen `score >= 0` (Zentibauern). */
  private toDisplayLines(fen: string, lines: AnalysisLine[]): EngineDisplayLine[] {
    return toDisplayLinesUtil(fen, lines, 12);
  }

  /** Gemeinsame Tempo-Formatierung. `nodes === null` = Kurzform (Vergleichs-Engine). */
  private speedHintFor(nps: number, nodes: number | null): string {
    if (nps <= 0) return this.translate.instant('analysis.speedWaiting');
    const speed = formatKiloNps(nps, this.locale);
    return nodes === null
      ? this.translate.instant('analysis.speedShort', { speed })
      : this.translate.instant('analysis.speedHint', { speed, nodes: formatKiloNodes(nodes, this.locale) });
  }

  /** Text hinter dem (i): Rechengeschwindigkeit der laufenden Analyse.
   *  ACHTUNG: template-gebundener Getter — er läuft MITTEN in der Change-Detection und darf
   *  deshalb unter keinen Umständen werfen (ein Wurf hier ließe die halbe Karte unrendert,
   *  siehe CLAUDE.md-Konvention). Daher `toLocaleString` (Browser-Intl, fällt bei unbekannter
   *  Sprache selbst zurück) statt Angulars formatNumber, das bei nicht registrierten
   *  Locale-Daten NG0701 wirft — und zusätzlich ein try/catch. */
  get speedHint(): string { return this.speedHintFor(this.nps, this.nodes); }

  /** Lebenszeichen der Remote-Suche — erst ab 5 s ohne neue Zeile, damit es im Normalbetrieb nicht flackert. */
  get showThinking(): boolean {
    return this.engineOn && !this.terminal && this.running && this.selectedEngineId !== 'wasm' && this.sinceUpdateSec >= 5;
  }
  /** Beide Uhren nachziehen (Sekundentakt): Funkstille seit der letzten Zeile + Suchzeit der Stellung. */
  private updateClocks(): void {
    const now = this.nowFn();
    const v = this.running ? Math.floor((now - this.lastUpdateAt) / 1000) : 0;
    const e = Math.max(0, Math.floor(((this.searchEndedAt ?? now) - this.searchStartedAt) / 1000));
    if (v !== this.sinceUpdateSec || e !== this.searchElapsedSec) {
      this.sinceUpdateSec = v; this.searchElapsedSec = e; this.cdr.markForCheck();
    }
  }

  get thinkingTime(): string { return AnalysisComponent.formatElapsed(this.sinceUpdateSec); }
  /** Suchzeit der aktuellen Stellung als m:ss (läuft, bis die Suche endet; dann eingefroren). */
  get searchTime(): string { return AnalysisComponent.formatElapsed(this.searchElapsedSec); }
  static formatElapsed(totalSec: number): string { return formatElapsedUtil(totalSec); }
  /** Erwartung setzen: 4+ Linien × Tiefe ≥ 27 braucht auf einem PC je Iteration Minuten. */
  get slowConfigHint(): boolean { return this.linesCount >= 4 && this.depthSetting >= 27; }

  /** 8234567 → „8,2 MN/s" (Tausender/Millionen wie in Schach-Oberflächen üblich). */

  private updateEval(best: AnalysisLine | null): void {
    if (!best) {
      // Partie-Ende: die Leiste zeigt das ERGEBNIS, nicht eine ausgeglichene Stellung.
      switch (this.terminal) {
        case 'mate-white-wins': this.evalText = '1-0'; this.whiteHeight = 100; return;
        case 'mate-black-wins': this.evalText = '0-1'; this.whiteHeight = 0; return;
        case 'stalemate': this.evalText = '½-½'; this.whiteHeight = 50; return;
      }
      this.evalText = '0.00'; this.whiteHeight = 50; return;
    }
    this.evalText = best.evalText;
    if (best.scoreType === 'mate') {
      this.whiteHeight = best.score > 0 ? 100 : 0;
    } else {
      const cp = best.score;
      this.whiteHeight = Math.max(2, Math.min(98, 50 + 50 * (2 / (1 + Math.exp(-0.004 * cp)) - 1)));
    }
  }

  private uciLineToSan(fromFen: string, uci: string[], maxPlies: number): string {
    return uciLineToSanUtil(fromFen, uci, maxPlies);
  }

  // ---- Controls / IO ----
  onEngineToggle(): void {
    this.settings.saveEngineOn(this.engineOn);
    this.refresh();
  }
  onLinesChange(): void {
    this.settings.saveLines(this.linesCount);
    this.engine.setMultiPv(this.linesCount);
    this.compare.engine?.setMultiPv(this.linesCount);
    this.restartSearches();
  }
  onDepthChange(): void {
    this.settings.saveDepth(this.depthSetting);
    this.engine.setDepth(this.depthSetting);
    this.compare.engine?.setDepth(this.depthSetting);
    this.restartSearches();
  }
  /** Startet beide Suchen neu — mit DEMSELBEN Vorbehalt wie refresh(): in einer terminalen
   *  Stellung (Matt/Patt) bekommt keine Engine ein `go`. Ohne diesen gemeinsamen Weg setzten
   *  Tiefen-/Linienwechsel den Matt-Fall wieder außer Kraft. */
  private restartSearches(): void {
    if (this.dests.size === 0) return;
    if (this.engineOn) {
      this.runAnalysis(this.engine, this.currentFen);
      this.runAnalysis(this.compare.engine, this.currentFen);
    } else if (this.spar.sparringEngineQuiet) {
      this.runAnalysis(this.engine, this.currentFen);   // stille Engine im Sparring: nur die Haupt-Engine
    }
  }

  /** analyze() lehnt ab, wenn init() scheitert oder die Engine waehrend des Handshakes zerstoert
   *  wird. Gemeldet ist das dann bereits ueber engineFatalError$ + reportEngineEvent — hier nur
   *  noch schlucken, damit daraus kein „Uncaught (in promise)" in der Konsole wird. */
  private runAnalysis(engine: AnalysisEngineService | undefined, fen: string): void {
    engine?.analyze(fen).catch(() => {});
  }

  /** Alle wählbaren Engines (Browser + registrierte externe) — für beide Auswahlfelder.
   *  ACHTUNG: template-gebundener Getter in einer Default-Change-Detection-Component, die unter
   *  einer externen Engine viele Male pro Sekunde markiert wird. Unmemoisiert baute er bei JEDEM
   *  Durchlauf ein frisches Array frischer Objekte und schlug `analysis.engineBrowser` neu nach —
   *  und das mehrfach je Durchlauf, weil beide Namens-Getter ihn ebenfalls aufrufen. Der Cache
   *  haelt bewusst die Sprache mit fest, sonst bliebe die Beschriftung nach einem Sprachwechsel
   *  auf der alten stehen. */
  private choicesCache?: { lang: string | null; list: ExternalEngineInfo[]; value: EngineChoice[] };
  get engineChoices(): EngineChoice[] {
    const lang = this.translate.currentLang();   // ngx-translate 18: Signal, kein String
    const c = this.choicesCache;
    if (c && c.lang === lang && c.list === this.externalEnginesList) return c.value;
    const value = [
      { id: 'wasm', name: this.translate.instant('analysis.engineBrowser'), tag: null },
      // `tag`: „· über Lichess" / „· offline" hinter dem Namen — die Auswahl mischt beide Quellen.
      ...this.externalEnginesList.map(e => ({ id: e.id, name: e.name, tag: engineTagKey(e) })),
    ];
    this.choicesCache = { lang, list: this.externalEnginesList, value };
    return value;
  }

  /** Anzeigename der Vergleichs-Engine. Ist sie zurückgefallen, wird die TATSÄCHLICH rechnende
   *  Engine genannt — ein Vergleich mit falschem Etikett wäre schlimmer als gar keiner. */
  get compareEngineName(): string { return this.compare.name(this.translate.instant('analysis.engineBrowser')); }
  /** Anzeigename der Haupt-Engine — im Vergleichsmodus muss beschriftet sein, welche welche ist. */
  get mainEngineName(): string {
    if (this.remoteFallback && this.selectedEngineId !== 'wasm') return this.translate.instant('analysis.engineBrowser');
    return this.engineChoices.find(c => c.id === this.selectedEngineId)?.name ?? '';
  }

  /** Tempo der Vergleichs-Engine für deren (i) — gleiche Formatierung wie bei der Haupt-Engine. */
  get compareSpeedHint(): string { return this.speedHintFor(this.compare.nps, null); }

  onEngineSelect(): void {
    this.settings.saveProvider(this.selectedEngineId);
    this.applyEngineSelection();
  }
  /** Verdrahtet die aktuelle Auswahl in den Engine-Service (Transport = HTTP-Proxy) und startet neu. */
  private applyEngineSelection(): void {
    const info = this.externalEnginesList.find(e => e.id === this.selectedEngineId) ?? null;
    this.engine.setRemoteEngine(info, (id, work) => this.externalEngines.analyse(id, work));
    // Wandert die Haupt-Engine auf die, die gerade als Vergleich läuft, muss die Vergleichs-
    // seite ausweichen — sonst rechnen beide Instanzen dasselbe.
    // ensureDistinctEngine() NICHT hier aufrufen: compare.start() macht es als zweiten
    // Schritt ohnehin. Zwei Aufrufstellen fuer dieselbe Invariante lesen sich, als sicherten sie
    // Verschiedenes ab, und laden dazu ein, nur eine davon zu „reparieren".
    if (this.compare.on && this.compare.engineId === this.selectedEngineId) this.compare.start();
    if ((this.engineOn || this.spar.sparringEngineQuiet) && this.dests.size > 0) this.runAnalysis(this.engine, this.currentFen);
  }

  backToPuzzle(): void {
    if (this.returnTo) this.router.navigateByUrl(this.returnTo);
  }
  reloadPage(): void { window.location.reload(); }
  flip(): void { this.orientation = this.orientation === 'white' ? 'black' : 'white'; }

  reset(): void { this.historySaver.newSession(); this.resetToStart(START_FEN); }
  private resetToStart(fen = this.startFen): void { this.setTree(createRoot(fen)); }

  // ---- Karte „Sparring gegen Maia" ein-/ausblenden (0.760.0) ----
  openMaiaCard(): void {
    this.maiaCardOpen = true;
    this.cdr.markForCheck();
    // Erst nach dem Zeichnen gibt es die Karte — dann in Sicht holen (am Handy liegt sie unter dem Brett).
    setTimeout(() => document.querySelector('app-maia-sparring-card')?.scrollIntoView?.({ block: 'nearest', behavior: 'smooth' }));
  }
  closeMaiaCard(): void {
    if (this.spar.sparring) return;   // ein laufendes Sparring braucht seine Knöpfe
    this.maiaCardOpen = false;
    this.cdr.markForCheck();
  }

  // ---- Stellung aufbauen (Brett-Editor) ----
  startEditing(): void { this.spar.abortMaia(); this.editing = true; }
  onSetupApply(fen: string): void {
    this.editing = false;
    this.historySaver.newSession();
    this.fenInput = '';
    this.resetToStart(fen);
  }

  loadFen(): void {
    const fen = this.fenInput.trim();
    if (!fen) return;
    if (!this.isValidFen(fen)) { this.snackbar.show(this.translate.instant('analysis.invalidFen'), { action: 'common.ok', duration: 2500 }); return; }
    this.historySaver.newSession();
    this.fenInput = '';
    this.resetToStart(fen);
  }

  /** PGN samt Varianten laden (0.604.0 — vorher nur die Hauptlinie) und ans Ende der Hauptlinie springen.
   *  @param keepText Das PGN im Feld stehen lassen (Sprung aus einer Partie) — beim Einfügen von Hand wird es geleert. */
  loadPgn(keepText = false): void {
    const pgn = this.pgnInput.trim();
    if (!pgn) return;
    const parsed = parsePgnTree(pgn);
    if (!parsed) { this.snackbar.show(this.translate.instant('analysis.invalidPgn'), { action: 'common.ok', duration: 2500 }); return; }
    this.historySaver.newSession(pgnTitle(parsed.headers));
    if (!keepText) this.pgnInput = '';
    const main = mainline(parsed.root);
    this.setTree(parsed.root, main[main.length - 1] ?? parsed.root);
  }

  // ---- Sterne + Zugbaum ----

  private starCache: { version: number; root: AnalysisNode | null; list: AnalysisNode[] } = { version: -1, root: null, list: [] };
  /** Markierte Stellungen, Hauptlinie zuerst (template-gebunden, deshalb je Baumstand nur einmal gesucht). */
  get starredList(): AnalysisNode[] {
    const c = this.starCache;
    if (c.version !== this.treeVersion || c.root !== this.root) {
      this.starCache = { version: this.treeVersion, root: this.root, list: starredNodes(this.root) };
    }
    return this.starCache.list;
  }

  /** Stern auf der Stellung, die gerade auf dem Brett steht (Taste S). */
  toggleStar(): void { this.toggleStarOn(this.currentNode); }

  private toggleStarOn(node: AnalysisNode): void {
    if (node.starred) delete node.starred; else node.starred = true;
    this.treeVersion++;
    this.historySaver.schedule();
  }

  /** „12...Nf6" — der Zug, der zur markierten Stellung führte; die Wurzel = Ausgangsstellung. */
  starLabel(node: AnalysisNode): string {
    return node === this.root || !node.parent ? this.translate.instant('analysis.star.start') : numberedSan(node);
  }

  /** Menü der Zugliste (Rechtsklick bzw. langer Druck): Stern, Variante hochstufen, zur Hauptvariante, ab hier löschen. */
  onTreeAction(e: { kind: MoveTreeAction; node: AnalysisNode }): void {
    const current = this.currentNode;
    switch (e.kind) {
      case 'star': this.toggleStarOn(e.node); return;
      case 'promote': promote(e.node); break;
      case 'mainline': makeMainline(e.node); break;
      case 'delete': {
        const within = isWithin(current, e.node);
        // Fällt die Ausgangsstellung des Sparrings weg, endet es (Maia-Regel 9).
        const endsSparring = this.spar.endedByRemoving(e.node);
        const parent = removeNode(e.node);
        if (!parent) return;
        this.spar.afterRemove(parent, endsSparring);
        this.treeVersion++;
        // Stand man in dem, was wegfällt, geht es beim Zug davor weiter.
        if (within) { this.goToNode(parent); return; }
        if (endsSparring) {
          this.treeVersion++;
          this.line = lineThrough(current);
          this.refresh();   // die Engine läuft wieder (falls sie vorher an war)
          return;
        }
        break;
      }
    }
    // Brett und Engine bleiben, nur die Reihenfolge änderte sich: die Linie durch den aktuellen Zug neu ziehen.
    this.treeVersion++;
    this.line = lineThrough(current);
    this.historySaver.schedule();
  }

  // ---- Analyse-Verlauf ----

  openHistory(): void {
    this.dialog.open(AnalysisHistoryDialogComponent, { width: '560px', maxWidth: '96vw' }).afterClosed()
      .subscribe((e: AnalysisHistoryEntry | undefined) => { if (e) this.openHistoryEntry(e); });
  }

  /** Einen Eintrag aufs Brett: Ausgangsstellung, Zugbaum samt Varianten und Sternen, der Zug von damals — und weiter
   *  speichern unter DERSELBEN Kennung. Die Liste trägt den Baum nicht mit; dann wird der Eintrag erst geholt. */
  openHistoryEntry(e: AnalysisHistoryEntry): void {
    if (!this.isValidFen(e.startFen)) return;
    if (!e.tree) {
      this.history.get(e.id).subscribe({
        next: full => { if (full.tree) this.openHistoryEntry(full); },
        error: () => this.snackbar.warn(this.translate.instant('analysis.history.loadFailed')),
      });
      return;
    }
    this.historySaver.openEntry(e);
    const { root, nodes } = fromDto(e.startFen, e.tree);
    this.setTree(root, (e.current >= 0 ? nodes[e.current] : null) ?? root);
    this.historySaver.markSaved();   // eben geladen = schon gespeichert
    this.cdr.markForCheck();
  }

  private isValidFen(fen: string): boolean {
    try { new Chess(fen); return true; } catch { return false; }
  }
}

/** „Weiß – Schwarz" aus den PGN-Kopfdaten (ohne „?"), sonst null. */
function pgnTitle(h: Record<string, string>): string | null {
  const white = h['White'] && h['White'] !== '?' ? h['White'] : null;
  const black = h['Black'] && h['Black'] !== '?' ? h['Black'] : null;
  if (white && black) return `${white} – ${black}`;
  return white ?? black ?? (h['Event'] && h['Event'] !== '?' ? h['Event'] : null);
}
