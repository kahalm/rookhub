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
import { EngineDisplayLine, formatElapsed as formatElapsedUtil, toDisplayLines } from './engine-lines.util';
import { speedHintText } from './analysis-engine-choice';
import { AnalysisEnginePicker } from './analysis-engine-picker';
import { engineArrows, engineCandidates, evalBar, legalDests, SearchClock, TerminalState, terminalStateOf } from './analysis-readout';
import { AnalysisBoardComponent } from './analysis-board.component';
import { AnalysisEngineCardComponent, EngineChoice, arrowBrush } from './analysis-engine-card.component';
import { PositionSetupComponent } from './position-setup.component';
import { AnalysisEngineService, AnalysisLine, RemoteInterruption } from './analysis-engine.service';
import { ExternalEngineService, ExternalEngineInfo } from './external-engine.service';
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
  AnalysisNode, addMove, createRoot, fromDto, isWithin, mainline, makeMainline, numberedSan, parsePgnTree, playSan, playUci, promote,
  removeNode,
} from './analysis-tree';
import { AnalysisLineModel, boardMove, playLine } from './analysis-line-model';
import { pgnTitle } from './analysis-title.util';
import { LineStudyService, lineStudyKey } from './line-study.service';
import { LineStudySession } from './line-study-session';
import { LineStudyCardComponent } from './line-study-card.component';
import { CalcAnnotateCardComponent } from '../courses/calc/calc-annotate-card.component';
import { AnalysisMoveTreeComponent, MoveTreeAction } from './analysis-move-tree.component';
import { MaiaEngineService } from './maia/maia-engine.service';
import { MaiaSparring } from './maia/maia-sparring';
import { MaiaSparringCardComponent } from './maia/maia-sparring-card.component';
import { TablebasePanelComponent, TablebaseVerdict } from './tablebase-panel.component';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { NextMoveChoice, nextMoveKey, openNextMoveChoice } from './next-move-choice';
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
    IconLabelDirective, MaiaSparringCardComponent, TablebasePanelComponent, LineStudyCardComponent,
    CalcAnnotateCardComponent
  ],
  templateUrl: './analysis.component.html',
  styleUrl: './analysis.component.scss',
})
export class AnalysisComponent implements OnInit, OnDestroy {
  /** Breitbild-Stufe (u1-rk-analyse-3spalten, ab 2400 px): drei Spalten, der Explorer steht offen in der dritten. */
  readonly wideLayout = mediaSignal();
  /** Herkunft für den „+"-Knopf (0.749.0): das Analysebrett kennt keine Partie. */
  readonly boardMarkOrigin: MarkOrigin = { context: 'board' };
  /** Zugbaum, Linie und Stelle darauf ({@link AnalysisLineModel}); `root`/`line`/`ply`/`treeVersion` reichen durch. */
  private readonly model = new AnalysisLineModel(START_FEN);
  get root(): AnalysisNode { return this.model.root; }
  get line(): AnalysisNode[] { return this.model.line; }
  get ply(): number { return this.model.ply; }
  /** Zählt Änderungen am Baum (Zug, Variante, Stern, Bewertung) — die Zugliste baut danach neu auf. */
  get treeVersion(): number { return this.model.treeVersion; }
  set treeVersion(v: number) { this.model.treeVersion = v; }
  /** Ergebnis der Endspiel-Datenbank für die aktuelle Stellung (0.729.0) — schlägt die Engine-Bewertung in der Leiste. */
  tbVerdict: TablebaseVerdict | null = null;

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

  // ---- „Ohne Engine analysieren" (0.855.0) ----
  /** API der Linien-Bäume (aus dem AnalyzeGameService; in Specs mit Stub fehlt sie — dann kein „ohne Engine"). */
  private readonly lineStudies?: LineStudyService;
  /** Eigene Linien zu einer Partie ohne Bewertung; `null` = normales Analysebrett. */
  study: LineStudySession | null = null;
  /** Ohne Engine und noch nicht aufgedeckt: keine Leiste, keine Engine, kein Explorer, keine Bewertungen. */
  get blind(): boolean { return !!this.study && !this.study.revealed; }

  // ---- Analyse-Verlauf + Sterne (0.603.0; Sterne seit 0.604.0 am Knoten des Zugbaums) ----
  private readonly historySaver: AnalysisHistorySaver;
  /** Kennung des Verlauf-Eintrags dieser Analyse (null = neue Analyse, der Server vergibt sie beim ersten Speichern). */
  get historyId(): number | null { return this.historySaver.id; }

  /** Engine-Wahl (External Engines laden, Auswahl, Verdrahtung) — siehe {@link AnalysisEnginePicker}. */
  private readonly picker: AnalysisEnginePicker;
  /** External Engines des Lichess-Kontos (leer = kein Picker); Auswahl 'wasm' = Browser. */
  get externalEnginesList(): ExternalEngineInfo[] { return this.picker.list; }
  set externalEnginesList(list: ExternalEngineInfo[]) { this.picker.list = list; }
  get selectedEngineId(): string { return this.picker.selectedId; }
  set selectedEngineId(id: string) { this.picker.selectedId = id; }
  /** Alle als Hintergrund gewaehlten Engines — der Live-Picker blendet sie aus. */
  get backgroundEngineIds(): string[] { return this.picker.backgroundIds; }
  /** Mindestens eine externe Engine registriert (inkl. Hintergrund-Engine) → Aufträge sind möglich. */
  get hasExternalEngines(): boolean { return this.picker.hasExternal; }
  remoteFallback = false;
  /** Abriss der Remote-Suche vor der Zieltiefe (Hinweis in der Karte; null = keiner). */
  remoteCut: RemoteInterruption | null = null;
  private cutSub?: Subscription;
  /** Lebenszeichen: läuft die Suche, und wie viele Sekunden kam keine neue Engine-Zeile? (nur Anzeige) */
  running = false;
  sinceUpdateSec = 0;
  private tickSub?: Subscription;
  /** Suchzeit der aktuellen Stellung (siehe {@link SearchClock}). Permanent im Kopf neben „Tiefe x/y". */
  searchElapsedSec = 0;
  private readonly searchClock = new SearchClock();
  /** Uhrquelle (in Tests überschreibbar). */
  private nowFn: () => number = () => Date.now();
  /** Suchleistung der laufenden Analyse (0 = noch kein Messwert). */
  nodes = 0;
  nps = 0;
  /** Partie-Ende in der aktuellen Stellung (keine legalen Züge) — dort rechnet keine Engine. */
  terminal: TerminalState = null;

  /** Vergleichsmodus: eine ZWEITE Engine rechnet dieselbe Stellung (Zustand + zweite Instanz in AnalysisCompare). */
  readonly compare: AnalysisCompare;

  private sub?: Subscription;
  private errorSub?: Subscription;
  private fallbackSub?: Subscription;
  /** Gemerkte Einstellungen (localStorage, je Gerät). */
  private readonly settings = new AnalysisSettings();

  constructor(private engine: AnalysisEngineService, private route: ActivatedRoute, private snackbar: SnackbarService,
              private router: Router, public auth: AuthService, private externalEngines: ExternalEngineService,
              private cdr: ChangeDetectorRef, private translate: TranslateService,
              @Inject(LOCALE_ID) private locale: string, private history: AnalysisHistoryService, private dialog: MatDialog,
              maia: MaiaEngineService, games: GamesService, analyzeGame: AnalyzeGameService) {
    this.lineStudies = analyzeGame?.lineStudies;
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
    this.picker = new AnalysisEnginePicker({
      engine, externalEngines, settings: this.settings, compare: () => this.compare,
      t: key => this.translate.instant(key), currentLang: () => this.translate.currentLang(),
      remoteFallback: () => this.remoteFallback,
      mainShouldRun: () => (this.engineOn || this.spar.sparringEngineQuiet) && this.dests.size > 0,
      runMain: () => this.runAnalysis(this.engine, this.currentFen),
      changed: () => this.cdr.markForCheck(),
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
    if (this.auth.isLoggedIn) this.picker.load();
    this.loadInitialPosition(p);
  }

  /** Vorgaben der Adresse übernehmen. Von der Auftragsseite: Engine + Suchparameter des Auftrags, damit hier
   *  WEITERgerechnet wird statt neu zu beginnen (gleiche Engine = warmer Hash) — nur für diesen Aufruf, bewusst
   *  NICHT als Dauereinstellung gespeichert. */
  private applyRouteParams(p: AnalysisRouteParams): void {
    this.model.root = createRoot(p.fen ?? START_FEN);
    if (p.orientation) this.orientation = p.orientation;
    if (p.returnTo) this.returnTo = p.returnTo;
    if (p.engine) this.picker.requestedId = p.engine;
    if (p.depth != null) this.depthSetting = p.depth;
    if (p.lines != null) this.linesCount = p.lines;
  }

  /** Engine einstellen und ihre Ströme abonnieren (Linien, Absturz, Rückfall, Abriss) samt Sekundentakt. */
  private connectEngine(): void {
    this.engine.setDepth(this.depthSetting);
    this.engine.setMultiPv(this.linesCount);
    this.sub = this.engine.analysis$.subscribe(s => {
      this.searchClock.onStatus(s.running, s.fen, this.nowFn());
      this.running = s.running;
      this.onEngineUpdate(s.fen, s.depth, s.lines, s.nodes, s.nps);
    });
    this.errorSub = this.engine.engineFatalError$.subscribe(e => { this.engineCrashed = e !== null; this.cdr.markForCheck(); });
    this.fallbackSub = this.engine.remoteFallback$.subscribe(f => { this.remoteFallback = f; this.cdr.markForCheck(); });
    this.cutSub = this.engine.remoteInterrupted$.subscribe(c => { this.remoteCut = c; this.cdr.markForCheck(); });
    // Sekundentakt nur für „rechnet seit …": bei MultiPV 5 vergehen ab Tiefe ~27 Minuten ohne neue
    // Zeile — ohne sichtbare Uhr sieht das aus wie ein Hänger. markForCheck nur bei Wertänderung.
    this.tickSub = interval(1000).subscribe(() => this.updateClocks());
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
    if (pgn && this.lineStudies && this.auth.isLoggedIn && (window.history.state?.blind === true || p.blind)) {
      this.pgnInput = pgn;
      this.startWithoutEngine();
    } else if (pgn) {
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
    this.setTree(root, playLine(root, uciMoves, playUci));
  }

  /**
   * „Ohne Engine" (0.855.0): Engine aus (nur für diese Seite, nicht als Einstellung), die Partie laden und ihren eigenen Baum
   * holen bzw. anlegen. Gespeichert wird dann in den Linien-Baum statt in den Analyse-Verlauf.
   */
  private startWithoutEngine(): void {
    this.engineOn = false;
    const parsed = parsePgnTree(this.pgnInput.trim());
    if (!parsed) { this.loadPgn(true); return; }
    const gameUcis = mainline(parsed.root).map(n => n.uci);
    this.study = new LineStudySession(this.lineStudies!, {
      root: () => this.root,
      current: () => this.currentNode,
      setTree: (root, current) => this.setTree(root, current),
      revealed: () => { this.engineOn = true; this.refresh(); },
      changed: () => { this.treeVersion++; this.cdr.markForCheck(); },
    }, lineStudyKey(parsed.root.fen, gameUcis), gameUcis, pgnTitle(parsed.headers));
    this.loadPgn(true);
    this.study.open();
  }

  /** „Engine einschalten" auf der Karte „Ohne Engine". */
  revealStudy(): void { this.study?.reveal(); }

  // Eigene Anmerkungen am Zug (0.855.2, Wunsch 2026-10-27: „Bewertungsbuttons analog zum Kalk-Modus fehlen, auch Kommentare zu
  // Stellungen wären gut") — dieselbe Karte wie im Kalkulations-Modus, gespeichert im Linien-Baum.
  private commentDraft: { node: AnalysisNode; text: string } | null = null;
  /** Kommentar-Entwurf des aktuellen Zugs (ungespeichert bis Enter/Verlassen des Felds). */
  get commentText(): string {
    return this.commentDraft?.node === this.currentNode ? this.commentDraft.text : this.currentNode.comment ?? '';
  }
  set commentText(text: string) { this.commentDraft = { node: this.currentNode, text }; }

  setGlyph(glyph: string): void { this.annotate(n => { n.glyph = n.glyph === glyph ? undefined : glyph; }); }
  setEvaluation(evaluation: string): void { this.annotate(n => { n.evaluation = n.evaluation === evaluation ? undefined : evaluation; }); }
  clearAnnotations(): void { this.annotate(n => { n.glyph = undefined; n.evaluation = undefined; }); }
  saveComment(): void {
    const text = this.commentText.trim();
    this.commentDraft = null;
    this.annotate(n => { n.comment = text || undefined; });
  }

  private annotate(change: (node: AnalysisNode) => void): void {
    const node = this.currentNode;
    if (node === this.root) return;
    change(node);
    this.treeVersion++;
    this.scheduleSave();
  }

  /** Änderung am Baum merken: in den Linien-Baum (ohne Engine) oder in den Analyse-Verlauf. */
  private scheduleSave(): void {
    if (this.study) this.study.schedule();
    else this.historySaver.schedule();
  }

  ngOnDestroy(): void {
    // Wer die Seite verlässt, bevor die Drossel abläuft, soll den letzten Stand trotzdem im Verlauf haben.
    this.historySaver.flush();
    this.study?.destroy();
    this.sub?.unsubscribe();
    this.errorSub?.unsubscribe();
    this.fallbackSub?.unsubscribe();
    this.cutSub?.unsubscribe();
    this.tickSub?.unsubscribe();
    this.picker.destroy();
    this.compare.stop();         // eigene Instanz + deren Worker/Streams beenden
    this.engine.destroy();
    this.spar.destroy();         // Maia freigeben, eine späte Antwort verfällt
  }

  // ---- Navigation ----
  get startFen(): string { return this.root.fen; }
  /** Der Knoten, dessen Stellung auf dem Brett steht (die Wurzel = Ausgangsstellung). */
  get currentNode(): AnalysisNode { return this.model.currentNode; }
  get currentFen(): string { return this.currentNode.fen; }

  goTo(ply: number): void {
    this.spar.abortMaia();
    this.model.goTo(ply);
    this.refresh();
  }

  /** Auf einen beliebigen Knoten springen (Klick in Zugliste, Variante, Sternliste) — die Linie läuft dann durch ihn. */
  goToNode(node: AnalysisNode): void {
    this.spar.abortMaia();
    this.model.goToNode(node);
    this.refresh();
  }

  /** Einen neuen Baum aufs Brett (Laden, Zurücksetzen) und dort auf `current` stehen. */
  private setTree(root: AnalysisNode, current: AnalysisNode = root): void {
    this.spar.onNewTree();   // beendet ein Sparring (Maia-Regel 9), „Partie analysieren" verfällt
    this.model.setRoot(root);
    this.goToNode(current);
  }
  prev(): void { this.goTo(this.ply - 1); }
  next(): void { this.goTo(this.ply + 1); }

  /** Offene Zugwahl beim Blättern ({@link openNextMoveChoice}, 0.857.0) — `null` = zu. */
  nextChoice: NextMoveChoice | null = null;

  /** „12.Nf3" bzw. „12...Nf6" in der Zugwahl. */
  readonly moveLabel = numberedSan;

  /** Zug aus der Zugwahl nehmen (Taste oder Klick). */
  pickNext(node: AnalysisNode): void {
    this.nextChoice = null;
    this.goToNode(node);
  }

  @HostListener('window:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    if (!isBoardHotkey(e)) return;
    if (this.nextChoice) {
      const r = nextMoveKey(this.nextChoice, e.key);
      if (r.kind !== 'ignore') e.preventDefault();
      if (r.kind === 'select') { this.nextChoice = r.choice; return; }
      if (r.kind === 'pick') { this.pickNext(r.node); return; }
      if (r.kind === 'close') { this.nextChoice = null; return; }
    }
    if (e.key === 'ArrowLeft') { e.preventDefault(); this.prev(); }
    else if (e.key === 'ArrowRight') {
      e.preventDefault();
      // Mehrere Fortsetzungen: erst fragen, wohin (Wunsch 2026-10-27), statt still der Linie zu folgen.
      const choice = openNextMoveChoice(this.currentNode);
      if (choice) this.nextChoice = choice; else this.next();
    }
    else if (e.key === 'Home') { e.preventDefault(); this.goTo(0); }
    else if (e.key === 'End') { e.preventDefault(); this.goTo(this.line.length); }
    else if ((e.key === 's' || e.key === 'S') && !e.ctrlKey && !e.metaKey && !e.altKey) { e.preventDefault(); this.toggleStar(); }
  }

  // ---- User move ----
  onMove(ev: { orig: Key; dest: Key; promotion?: string }): void {
    const r = boardMove(this.currentFen, ev);
    if (r.kind === 'bad-fen') return;
    if (r.kind === 'illegal') { this.refresh(); return; }   // illegaler Zug -> Brett zurücksetzen
    const mover = r.mover;
    // „Schlechte Züge melden": die Stellung davor samt ihrer Bewertungs-Spur merken, BEVOR goToNode sie verlässt.
    this.spar.beforeBoardMove(this.currentNode, mover);
    // Zugbaum (0.604.0): ein anderer Zug als die Fortsetzung wird eine VARIANTE, derselbe Zug geht in die vorhandene.
    const node = addMove(this.currentNode, r.move);
    this.treeVersion++;
    this.goToNode(node);
    this.spar.afterBoardMove(node, mover);   // Sparring: auf den EIGENEN Zug antwortet Maia
  }

  /** Baummodus des Repertoire-Panels bzw. Explorer: die geklickte Zugfolge ab der aktuellen Stellung aufs Brett
   * spielen (wie selbst gezogen — weicht sie von der Fortsetzung ab, wird sie eine Variante). Illegale/unbekannte
   * SAN brechen still ab, statt die Linie halb zu zerschießen. */
  playRepertoireMoves(sans: string[]): void {
    if (!sans?.length) return;
    const node = playLine(this.currentNode, sans, playSan);
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
    this.nextChoice = null;   // jede Bewegung (Klick in der Zugliste, Zug am Brett) schließt die Zugwahl
    this.scheduleSave();
    const fen = this.currentFen;
    let c: Chess;
    try { c = new Chess(fen); } catch { return; }
    this.boardFen = fen;
    this.turnColor = c.turn() === 'w' ? 'white' : 'black';
    this.isCheck = c.isCheck();
    this.dests = legalDests(c);
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
    this.terminal = this.dests.size > 0 ? null : terminalStateOf(c);
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

  /** Übersetzter Satz für das Partie-Ende (leer, wenn die Stellung nicht terminal ist). */
  get terminalText(): string {
    switch (this.terminal) {
      case 'mate-white-wins': return this.translate.instant('analysis.mateWhiteWins');
      case 'mate-black-wins': return this.translate.instant('analysis.mateBlackWins');
      case 'stalemate': return this.translate.instant('analysis.stalemate');
      default: return '';
    }
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
      // Beide Engine-Seiten laufen durch toDisplayLines — dieselbe Bewertung darf links nicht anders eingefärbt sein
      // als rechts (früher lag die Abbildung zweimal im Code).
      this.displayLines = toDisplayLines(fen, lines, 12);
      this.engineCandidates = engineCandidates(lines);
      this.shapes = engineArrows(lines, arrowBrush);
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
        this.scheduleSave();
      }
    }
    this.spar.serveMoveCheck(fen);
  }

  /** Text hinter dem (i): Rechengeschwindigkeit der laufenden Analyse (template-gebunden, wirft nie). */
  get speedHint(): string { return speedHintText(this.t, this.locale, this.nps, this.nodes); }
  private readonly t = (k: string, p?: object) => this.translate.instant(k, p) as string;

  /** Lebenszeichen der Remote-Suche — erst ab 5 s ohne neue Zeile, damit es im Normalbetrieb nicht flackert. */
  get showThinking(): boolean {
    return this.engineOn && !this.terminal && this.running && this.selectedEngineId !== 'wasm' && this.sinceUpdateSec >= 5;
  }
  /** Beide Uhren nachziehen (Sekundentakt): Funkstille seit der letzten Zeile + Suchzeit der Stellung. */
  private updateClocks(): void {
    const { sinceUpdateSec, searchElapsedSec } = this.searchClock.read(this.nowFn());
    if (sinceUpdateSec !== this.sinceUpdateSec || searchElapsedSec !== this.searchElapsedSec) {
      this.sinceUpdateSec = sinceUpdateSec; this.searchElapsedSec = searchElapsedSec; this.cdr.markForCheck();
    }
  }

  get thinkingTime(): string { return AnalysisComponent.formatElapsed(this.sinceUpdateSec); }
  /** Suchzeit der aktuellen Stellung als m:ss (läuft, bis die Suche endet; dann eingefroren). */
  get searchTime(): string { return AnalysisComponent.formatElapsed(this.searchElapsedSec); }
  static formatElapsed(totalSec: number): string { return formatElapsedUtil(totalSec); }
  /** Erwartung setzen: 4+ Linien × Tiefe ≥ 27 braucht auf einem PC je Iteration Minuten. */
  get slowConfigHint(): boolean { return this.linesCount >= 4 && this.depthSetting >= 27; }

  /** Bewertungsleiste setzen ({@link evalBar}). */
  private updateEval(best: AnalysisLine | null): void {
    ({ evalText: this.evalText, whiteHeight: this.whiteHeight } = evalBar(best, this.terminal));
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

  /** Alle wählbaren Engines (Browser + registrierte externe) — für beide Auswahlfelder, memoisiert. */
  get engineChoices(): EngineChoice[] { return this.picker.choices; }

  /** Anzeigename der Vergleichs-Engine. Ist sie zurückgefallen, wird die TATSÄCHLICH rechnende
   *  Engine genannt — ein Vergleich mit falschem Etikett wäre schlimmer als gar keiner. */
  get compareEngineName(): string { return this.compare.name(this.translate.instant('analysis.engineBrowser')); }
  /** Anzeigename der Haupt-Engine — im Vergleichsmodus muss beschriftet sein, welche welche ist. */
  get mainEngineName(): string { return this.picker.mainName; }

  /** Tempo der Vergleichs-Engine für deren (i) — gleiche Formatierung wie bei der Haupt-Engine. */
  get compareSpeedHint(): string { return speedHintText(this.t, this.locale, this.compare.nps, null); }

  onEngineSelect(): void { this.picker.select(); }

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

  /** Markierte Stellungen, Hauptlinie zuerst (template-gebunden, deshalb je Baumstand nur einmal gesucht). */
  get starredList(): AnalysisNode[] { return this.model.starred; }

  /** Stern auf der Stellung, die gerade auf dem Brett steht (Taste S). */
  toggleStar(): void { this.toggleStarOn(this.currentNode); }

  private toggleStarOn(node: AnalysisNode): void {
    this.model.toggleStar(node);
    this.scheduleSave();
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
          this.model.relineThrough(current);
          this.refresh();   // die Engine läuft wieder (falls sie vorher an war)
          return;
        }
        break;
      }
    }
    // Brett und Engine bleiben, nur die Reihenfolge änderte sich: die Linie durch den aktuellen Zug neu ziehen.
    this.treeVersion++;
    this.model.relineThrough(current);
    this.scheduleSave();
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
