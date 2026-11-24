import {
  ChangeDetectionStrategy, ChangeDetectorRef, Component, DestroyRef, EventEmitter, HostListener, Input, OnChanges, Output, SimpleChanges,
  computed, effect, inject, input, signal, untracked,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { Color, Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import { SnackbarService } from '../../core/snackbar.service';
import { PreferencesService } from '../../core/preferences.service';
import { localStore, readRaw, writeRaw } from '../../core/local-json-store';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { ChessBoardComponent, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { CalcLinesComponent } from '../courses/calc/calc-lines.component';
import {
  CalcTree, addMove, createTree, deserializeTree, findNode, lines as calcLines, pathToRoot, removeLine, rootNode, serializeTree,
} from '../courses/calc/calc-tree.util';
import { LiveEngineSession } from '../games/live-engine-session';
import { LiveEnginePanelComponent } from '../games/live-engine-panel.component';
import { MaiaEngineService } from '../analysis/maia/maia-engine.service';
import { MAIA_DEFAULT_ELO, MAIA_ELO_OPTIONS } from '../analysis/maia/maia-model';
import { StockfishService } from '../puzzles/stockfish.service';
import { PuzzleBoardComponent } from '../puzzles/puzzle-board.component';
import { applyUci, calcDests, tryFreeMove, tryLoadFen } from '../../shared/chess/puzzle-move.util';
import { BoardFsActionsComponent } from '../puzzles/board-fs-actions.component';
import { HintLadder } from '../puzzles/hint-ladder';
import { buildStagedHints, classifyMoveFromFen } from '../puzzles/puzzle-hints.util';
import { offPathHintTexts } from '../puzzles/solver-texts';
import { SecondsCountdown, realClock } from '../puzzles/solver-timer';
import { EbookDiagram, EbookGame, EbookService } from './ebook.service';
import { cardDate, cardLang } from '../../shared/player-card/card-lang';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
// UI-Sweep 2026-11-10 (d10-rk-sanpipe-dialoge-ebook-zugliste): Zuglisten in den Figurenbuchstaben der Oberflächensprache.
import { SanPipe } from '../../shared/chess/san.pipe';
import { sideToMove, whiteToMove } from '../../shared/chess/fen-side';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { EbookOpeningTree } from './ebook-tree.util';
import { EbookTreePanelComponent } from './ebook-tree-panel.component';
import { EbookGameLine, ebookResultLabel, numberedSans, replayGame, sameResult, scoreCp } from './ebook-board.util';

export { ebookResultLabel, type EbookGameLine } from './ebook-board.util';

/** Was das Brett gerade tut (0.777.0). */
export type EbookBoardMode = 'view' | 'free' | 'calc' | 'solve' | 'maia' | 'game' | 'tree';

/** Lösen: beide Arten verhalten sich wie der Puzzle-Löser (1.011.0: Fehlzug ohne „falsch“, Stockfish antwortet, Fehler
 *  gemerkt, Mausrutscher, Tipps; keine Lösung vorspielen, Nutzerwunsch 20.10.2026). Seit 0.825.0 bleibt im „Training“
 *  beim Lösen und Kalkulieren außerdem die Buchstellung stehen — die Züge stehen nur als Text da, wie im Kurs-Training. */
export type EbookSolveStyle = 'easy' | 'training';
/** Gegen welche Lösung gelöst wird: die gespeicherte (Buch bzw. bei der Prüfung gespeicherte Engine-Linie) oder Stockfish jetzt. */
export type EbookSolveSource = 'stored' | 'engine';

/** So viele Halbzüge rechnet die Stockfish-Lösung im Browser. */
export const ENGINE_SOLUTION_PLIES = 7;

/**
 * Das Brett neben dem Buch (0.777.0, vorher Teil von {@link EbookReaderComponent}). Modi:
 * - **Ansehen** — nur die Buchstellung.
 * - **Frei ziehen** — Züge bewegen die Figuren (Liste mit Zurücknehmen).
 * - **Kalkulieren** — wie der Kalkulations-Modus der Kurse: Varianten werden als Baum notiert (je Diagramm auf diesem Gerät
 *   gemerkt), jede Linie lässt sich später im Analysebrett prüfen; die Engine-Leiste rechnet die gerade gewählte Stellung.
 * - **Lösen** — gegen die gespeicherte Lösung (Buch bzw. Engine-Linie der Prüfung) oder eine jetzt im Browser gerechnete
 *   Stockfish-Lösung; wie der Puzzle-Löser (Fehlzug → gegen Stockfish weiter, Mausrutscher, Tipps, Countdown). WER löst, sagt der Server je
 *   Diagramm (0.975.0, `solvable`/`opponentFirst` aus `BookSolverLine.ForReader` — die Regel wird hier NICHT nachgebaut):
 *   bei `opponentFirst` wird der erste Zug der Linie vorgespielt und der Löser hat die andere Farbe; bei `solvable: false`
 *   gibt es den Modus für dieses Diagramm nicht (die Buchlinie lässt sich unter „Ansehen“ aufklappen).
 * - **Gegen Maia** — Sparring ab der Buchstellung; man spielt die Seite am Zug, Maia antwortet in der gewählten Stärke.
 * - **Partie** (0.831.0, Partiensammlungen) — die ganze Partie, aus der das Diagramm stammt, ab der Diagrammstellung zum
 *   Durchklicken; eigene Züge werden zur Nebenlinie (Zurück führt in die Partie). Nur mit hinterlegter Partie.
 * - **Baum** (0.881.0, Eröffnungsbücher) — der Eröffnungsbaum des Buchs ab der Diagrammstellung ({@link EbookTreePanelComponent});
 *   Brettzüge, die im Buch stehen, gehen weiter, andere werden mit „nicht im Buch“ zurückgenommen. Nur mit Baum.
 * Die Engine (Stockfish im Browser, {@link LiveEngineSession}) ist in jedem Modus zuschaltbar.
 */

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-ebook-board',
  standalone: true,
  imports: [RouterLink, NgTemplateOutlet, MatButtonModule, MatIconModule, MatMenuModule, MatProgressBarModule,
    TranslatePipe, IconLabelDirective, ChessBoardComponent, PuzzleBoardComponent, CalcLinesComponent, LiveEnginePanelComponent, HelpHintComponent,
    EbookTreePanelComponent, SanPipe, BoardFsActionsComponent],
  templateUrl: './ebook-board.component.html',
  styleUrls: ['./ebook-board.component.scss'],
})
export class EbookBoardComponent implements OnChanges {
  @Input({ required: true }) diagram!: EbookDiagram;
  @Input() ebookId = 0;
  /** Startmodus fürs Buch (0.781.0, im Bearbeiten-Dialog wählbar, Standard „Lösen“). Gilt beim ersten Diagramm; danach
   *  bleibt, was der Leser selbst wählt. */
  @Input() defaultMode: EbookBoardMode | null | undefined = 'solve';
  /** „Zur Lösungsseite“ — der Leser springt dorthin. */
  @Output() solutionPage = new EventEmitter<number>();
  /** „Diagramm falsch“ im ⋮ des Bretts (0.785.x, vorher eigene Zeile im Leser) — nur mit Konto. */
  @Input() reportable = false;
  /** Melden/Zurücknehmen angeklickt — der Leser schickt es ab (Zustand kommt über `diagram.reportedByMe`). */
  @Output() report = new EventEmitter<void>();
  /** „Stellung korrigieren“ im ⋮ (0.798.0) — nur wer das Buch verwalten darf. */
  @Input() correctable = false;
  /** „Stellung korrigieren“ angeklickt — der Leser öffnet den Editor und speichert; die neue Stellung kommt über `diagram`. */
  @Output() correct = new EventEmitter<void>();
  /** Das Buch hat einen Eröffnungsbaum (0.881.0) — Modus „Baum“ sichtbar. */
  readonly hasTree = input(false);
  /** Buchtitel (Name und Kopf eines aus dem Baum gespeicherten Repertoires). */
  readonly bookTitle = input('');
  /** „Als Repertoire speichern“ anbieten — nur mit Konto. */
  readonly canSaveRepertoire = input(false);
  /** Gelöst und „Automatisch weiter“ an: der Leser springt zum nächsten Diagramm. */
  @Output() solvedNext = new EventEmitter<void>();

  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly stockfish = inject(StockfishService);
  readonly maia = inject(MaiaEngineService);
  readonly prefs = inject(PreferencesService);

  readonly modes: EbookBoardMode[] = ['view', 'free', 'calc', 'solve', 'maia', 'game', 'tree'];
  /** Sichtbare Modi: „Partie“ nur, wenn das Diagramm eine Partie hat oder das Buch auf „Partie“ steht (Partiensammlung);
   *  „Baum“ nur bei Büchern mit Eröffnungsbaum. */
  get visibleModes(): EbookBoardMode[] {
    const game = this.diagram?.hasGame || this.defaultMode === 'game' || this.mode() === 'game';
    const tree = this.hasTree() || this.mode() === 'tree';
    return this.modes.filter(m => (m !== 'game' || game) && (m !== 'tree' || tree) && (m !== 'solve' || this.solvable));
  }

  /** Modus braucht eine regelgerechte Buchstellung — „Ansehen“ und „Baum“ (eigene Stellungen ab der Grundstellung) nicht. */
  needsLegal(m: EbookBoardMode): boolean { return m !== 'view' && m !== 'tree'; }
  private readonly ebooks = inject(EbookService);

  readonly mode = signal<EbookBoardMode>('solve');
  readonly flipped = signal(false);
  /** Die Buchstellung als Signal (für computed/effect). */
  readonly start = signal<EbookDiagram | null>(null);

  /** Frei ziehen / Lösen / Maia: gespielte Züge ab der Buchstellung. */
  readonly played = signal<{ san: string; fen: string; uci: string }[]>([]);

  /** Kalkulieren: Variantenbaum + gewählter Knoten; `revision` lässt die Linienliste neu zeichnen. */
  calcTree: CalcTree | null = null;
  readonly calcCursor = signal(0);
  readonly calcRevision = signal(0);

  /** Lösen. Standard „Training“ (Nutzerwunsch 15.10.); die eigene Wahl ist auf dem Gerät gemerkt und gewinnt. */
  readonly solveStyles: EbookSolveStyle[] = ['easy', 'training'];
  readonly solveStyle = signal<EbookSolveStyle>(EbookBoardComponent.loadSolveStyle());
  static readonly SOLVE_STYLE_KEY = 'rh.ebooks.solveStyle';
  static readonly AUTO_NEXT_KEY = 'rh.ebooks.autoNext';
  /** Nach dem Lösen automatisch zum nächsten Diagramm (0.787.1), auf dem Gerät gemerkt; Standard an (Nutzerwunsch 20.10.2026). */
  readonly autoNext = signal(readRaw(localStore(), EbookBoardComponent.AUTO_NEXT_KEY) !== '0');
  /** Sekunden bis zum Weiterspringen (sichtbar am Knopf „Nächstes Diagramm“, 1.011.0) — Zeit, die eigene Lösung anzusehen. */
  static readonly AUTO_NEXT_SECONDS = 3;
  /** Countdown zum nächsten Diagramm (der des Puzzle-Lösers); jeder Takt zeichnet neu. */
  private readonly nextTicker = new SecondsCountdown({
    ...realClock, setInterval: (fn, ms) => realClock.setInterval(() => { fn(); this.cdr.markForCheck(); }, ms),
  });
  get autoNextCountdown(): number { return this.nextTicker.remaining; }
  /** Gibt es ein nächstes Diagramm? Sonst kein „Weiter“ und kein Countdown. */
  readonly hasNext = input(true);
  /** In diesem Diagramm schon ein falscher Zug gespielt — in beiden Arten (1.011.0, wie im Puzzle-Löser); der
   *  Mausrutscher und „Zurücksetzen“ während des Lösens wischen ihn nicht weg, erst ein neuer Versuch nach dem Ende. */
  readonly mistake = signal(false);
  /** Abseits der Lösung selbst mattgesetzt (eigene Lösung, wie im Puzzle-Löser). */
  readonly alternative = signal(false);
  readonly solveSource = signal<EbookSolveSource>('stored');
  readonly solveState = signal<'idle' | 'running' | 'solved' | 'failed'>('idle');
  /** Stockfish-Lösung (UCI), sobald gerechnet; `null` = noch nicht / läuft. */
  readonly engineLine = signal<string[] | null>(null);
  readonly engineLineBusy = signal(false);
  readonly showSolution = signal(false);
  /** Training: das eingefrorene Brett kurz auf die aktuelle Stellung umschalten (Auge, Nutzerwunsch 25.10.2026) —
   *  die Lösung hat dafür die Glühbirne. Nur Ansehen; gezogen wird weiter auf dem Viz-Brett. */
  readonly peek = signal(false);
  /** Stockfish rechnet für den Löser (prüft einen abweichenden Zug oder sucht die Antwort) — Status „Stockfish denkt…“. */
  readonly checking = signal(false);
  /** Vom Buch (bzw. der gespeicherten Linie) abgewichen, aber gleichwertig — Antworten kommen jetzt von Stockfish. */
  readonly deviated = signal(false);
  /** Ab welchem Halbzug {@link deviated} gilt (der Mausrutscher kann davor zurück). */
  private deviatedAt: number | null = null;
  /** Falscher Zug steht auf dem Brett (wie im Puzzle-Löser): Index des Fehlzugs in {@link played}, sonst `null`.
   *  Kein „falsch“ — Stockfish antwortet, man spielt gegen Stockfish weiter. */
  readonly wrongAt = signal<number | null>(null);
  /** Einmal je Versuch: das letzte Zugpaar zurück (Mausrutscher wie im Puzzle-Löser). */
  readonly mouseslipUsed = signal(false);
  /** Gestufte Tipps je Zug (die des Puzzle-Lösers); alle drei zu einem Zug → „mehr oder weniger“ gelöst. */
  readonly hintLadder = new HintLadder();
  /** Zählt bei jedem Neubeginn/Mausrutscher hoch — eine danach eintreffende Stockfish-Antwort wird verworfen. */
  private epoch = 0;
  private timer: ReturnType<typeof setTimeout> | null = null;
  /** Pause, bevor der Gegnerzug einer „Gegner zuerst“-Aufgabe vorgespielt wird (wie der Setup-Zug im Puzzle-Löser). */
  static readonly LEAD_DELAY_MS = 600;
  /** Pause vor der Antwort aus der Lösungslinie (wie im Puzzle-Löser). */
  static readonly REPLY_DELAY_MS = 400;
  /** „Lösen“ war gewählt, das Diagramm bietet es aber nicht an → „Ansehen“; beim nächsten Diagramm wieder „Lösen“. */
  private solveSkipped = false;

  /** Maia. */
  readonly maiaElo = signal<number>(MAIA_DEFAULT_ELO);
  readonly maiaEloOptions = MAIA_ELO_OPTIONS;
  readonly maiaThinking = signal(false);
  /** Hat dieses Brett Maia geladen? Dann gibt es sie beim Verlassen frei (F2-011). */
  private maiaUsed = false;

  /** Partie (0.831.0): geladene Partie, Lade-Zustand und der Halbzug in der Hauptlinie; eigene Züge ab dort in {@link played}. */
  readonly gameLine = signal<EbookGameLine | null>(null);
  readonly gameState = signal<'idle' | 'loading' | 'ready' | 'missing'>('idle');
  readonly gamePly = signal(0);
  /** Brett steht genau auf der Diagrammstellung der Partie (ohne eigene Züge) — dann Marke statt Knopf „Zum Diagramm“. */
  readonly atGameDiagram = computed(() => this.played().length === 0 && this.gamePly() === (this.gameLine()?.game?.ply ?? 0));
  private gameRequest = 0;

  /** Baum (0.881.0): gebauter Baum, Lade-Zustand, gewählter Knoten, „Diagramm nicht im Baum“ und die Stellung eines kurz
   *  gezeigten Zugs, der nicht im Buch steht. */
  readonly openingTree = signal<EbookOpeningTree | null>(null);
  readonly treeState = signal<'idle' | 'loading' | 'ready' | 'missing'>('idle');
  readonly treeCursor = signal(0);
  readonly treeNotInTree = signal(false);
  readonly treeOffFen = signal<string | null>(null);
  private treeRequest = 0;
  /** Zuletzt gebauter Baum (ein Buch) — das Brett entsteht je Diagrammauswahl neu, der Baum nicht. */
  private static treeCache: { ebookId: number; tree: EbookOpeningTree } | null = null;

  /** Engine-Leiste (zuschaltbar in jedem Modus). */
  readonly engine = signal<LiveEngineSession | null>(null);
  private engineIndex = 0;

  /** Ob die Buchstellung regelgerecht ist — sonst bleibt nur Ansehen. */
  readonly legal = computed(() => {
    const d = this.start();
    if (!d) return false;
    try { new Chess(d.fen); return true; } catch { return false; }
  });

  /** Stellung auf dem Brett. */
  readonly boardFen = computed(() => {
    const d = this.start();
    if (!d) return '';
    if (this.mode() === 'game') {
      const g = this.gameLine();
      return this.played().at(-1)?.fen ?? (g ? g.fens[this.gamePly()] : d.fen);
    }
    if (this.mode() === 'tree') {
      const t = this.openingTree();
      return this.treeOffFen() ?? (t && this.treeState() === 'ready' ? t.fen[this.treeCursor()] : d.fen);
    }
    if (this.mode() === 'calc') {
      this.calcRevision();
      const node = this.calcTree ? findNode(this.calcTree, this.calcCursor()) : undefined;
      return node?.fen ?? d.fen;
    }
    return this.played().at(-1)?.fen ?? d.fen;
  });

  /** Training beim Lösen/Kalkulieren (0.825.0, Nutzerwunsch 23.10.2026 „wie beim Kurs“): das Brett bleibt auf der
   *  Buchstellung eingefroren, gezogen wird auf dem Kurs-Brett im Visualisierungs-Modus gegen {@link boardFen}. */
  readonly frozen = computed(() => this.legal() && this.solveStyle() === 'training' && (this.mode() === 'solve' || this.mode() === 'calc'));

  /** Was das eingefrorene Brett zeigt: die Buchstellung — bei „Gegner zuerst“ die Stellung nach dem vorgespielten Gegnerzug
   *  (das ist die Aufgabe; der Zug selbst steht als Pfeil da). */
  readonly frozenFen = computed(() => {
    const d = this.start();
    if (!d) return '';
    const lead = this.mode() === 'solve' ? this.lead.length : 0;
    return lead > 0 && this.played().length >= lead ? this.played()[lead - 1].fen : d.fen;
  });

  /** Eingefrorenes Brett: der letzte Zug der Gegenseite als Pfeil (der eigene steht ja im Kopf). */
  readonly opponentArrow = computed<[Key, Key] | undefined>(() => {
    if (this.mode() !== 'solve') return undefined;
    const l = this.played();
    // Gegnerzüge stehen an ungerader Stelle, bei „Gegner zuerst“ an gerader.
    if (l.length === 0 || (l.length - this.lead.length) % 2 === 1) return undefined;
    const u = l[l.length - 1].uci;
    return [u.slice(0, 2) as Key, u.slice(2, 4) as Key];
  });

  readonly noDests = new Map<Key, Key[]>();

  readonly playable = computed(() => {
    if (this.mode() === 'tree') return this.treeState() === 'ready' && this.treeOffFen() === null;
    if (!this.legal()) return false;
    switch (this.mode()) {
      case 'view': return false;
      case 'solve': return this.solveState() === 'running' && !this.checking() && this.isUserTurn();
      case 'maia': return !this.maiaThinking() && this.isUserTurn() && this.maia.status() === 'ready';
      case 'game': return this.gameState() === 'ready';
      default: return true;
    }
  });

  /** Brett des Lösens „Einfach“ (1.011.0): dasselbe Brett wie im Puzzle-Löser — Zugziele, Vorziehen, letzter Zug,
   *  Brett-Thema und Figurensatz aus den Einstellungen, Vollbild mit der Aktionsleiste. */
  readonly solveDests = computed(() => {
    if (!this.playable() || this.peek()) return this.noDests;
    const c = tryLoadFen(this.boardFen());
    return c ? calcDests(c) : this.noDests;
  });
  /** Vorziehen, während Stockfish bzw. die Linie antwortet (wie im Puzzle-Löser, nicht vor dem vorgespielten Gegnerzug). */
  readonly premovable = computed(() => this.mode() === 'solve' && this.solveState() === 'running' && !this.peek()
    && this.played().length >= this.lead.length && !this.isUserTurn());
  /** Der letzte Zug (Hervorhebung), solange Züge ab der Buchstellung gespielt sind. */
  readonly lastMoveKeys = computed<[Key, Key] | undefined>(() => {
    const u = this.mode() === 'calc' || this.mode() === 'tree' ? undefined : this.played().at(-1)?.uci;
    return u ? [u.slice(0, 2) as Key, u.slice(2, 4) as Key] : undefined;
  });
  get boardOrientation(): Color { return this.flipped() ? 'black' : 'white'; }
  get solverColor(): Color { return this.userColor === 'w' ? 'white' : 'black'; }
  get boardTurn(): Color { return whiteToMove(this.boardFen()) ? 'white' : 'black'; }
  get boardCheck(): boolean { return !!tryLoadFen(this.boardFen())?.isCheck(); }
  /** Pfeile der zugeschalteten Engine auf dem Puzzle-Brett. */
  readonly engineShapes = computed<DrawShape[]>(() => (this.engine()?.arrows() ?? []).map(a => ({
    orig: a.from as Key, dest: a.to as Key, brush: a.brush ?? 'green', ...(a.lineWidth != null ? { modifiers: { lineWidth: a.lineWidth } } : {}),
  })));

  constructor() {
    // Engine rechnet jede neue Brettstellung (neuer Index = neue Stellung, keine eigene Nebenvariante).
    // `sync` liest und schreibt Signale der Sitzung (baseIndex …) — OHNE untracked hinge der Effekt an ihnen, jeder
    // Schreibzugriff löste ihn neu aus, mit neuem Index: Endlosschleife, die Seite stand (0.801.0, „Engine an“).
    effect(() => {
      const s = this.engine();
      const fen = this.boardFen();
      if (s && fen) untracked(() => s.sync(++this.engineIndex, fen));
    });

    inject(DestroyRef).onDestroy(() => {
      this.clearTimer();
      this.engine()?.destroy();
      // F2-011: Maia (Modell + Sitzung im Worker, ~150 MB) freigeben wie das Analysebrett — nur, wenn dieses Brett sie geholt hat.
      if (this.maiaUsed) this.maia.release();
    });
  }

  ngOnChanges(ch: SimpleChanges): void {
    if (!ch['diagram'] || !this.diagram) return;
    const prev = ch['diagram'].previousValue as EbookDiagram | undefined;
    // Nur Meldung o. Ä. geändert; eine korrigierte Stellung (gleiche Id, neue FEN) beginnt dagegen von vorn.
    if (prev?.id === this.diagram.id && prev.fen === this.diagram.fen) { this.start.set(this.diagram); return; }
    this.clearTimer();
    this.start.set(this.diagram);
    if (!prev) this.mode.set(this.defaultMode && this.modes.includes(this.defaultMode) ? this.defaultMode : 'solve');
    else if (this.solveSkipped && this.mode() === 'view') this.mode.set('solve');
    this.solveSkipped = false;
    this.played.set([]);
    this.wrongAt.set(null);
    this.epoch++;
    this.engineLine.set(null);
    this.showSolution.set(false);
    this.peek.set(false);
    this.solveState.set('idle');
    this.maiaThinking.set(false);
    if (this.mode() === 'tree' && !this.hasTree()) this.mode.set('solve');
    if (!this.legal() && this.needsLegal(this.mode())) this.mode.set('view');
    // Kein „Lösen“ für dieses Diagramm (Löserseite nicht bestimmbar / Linie unspielbar): ansehen, die Wahl aber behalten.
    if (this.mode() === 'solve' && !this.solvable) { this.mode.set('view'); this.solveSkipped = true; }
    this.flipped.set(this.userColor === 'b');
    if (!this.hasStoredSolution) this.solveSource.set('engine');
    if (this.mode() === 'calc') this.loadCalc();
    if (this.mode() === 'solve') this.startSolving();
    if (this.mode() === 'maia') this.prepareMaia();
    if (this.mode() === 'game') this.loadGame();
    if (this.mode() === 'tree') this.loadTree();
  }

  // ---------------------------------------------------------------- Modus

  setMode(m: EbookBoardMode): void {
    if (this.needsLegal(m) && !this.legal()) return;
    if (m === 'solve' && !this.solvable) return;
    this.clearTimer();
    this.played.set([]);
    this.solveState.set('idle');
    this.maiaThinking.set(false);
    this.solveSkipped = false;
    this.wrongAt.set(null);
    this.epoch++;
    this.mode.set(m);
    // „Gegner zuerst“: beim Lösen aus Sicht des Lösers, sonst wie das Buch (Seite am Zug unten).
    if (this.diagram?.opponentFirst) this.flipped.set(this.userColor === 'b');
    if (m === 'calc') this.loadCalc();
    if (m === 'solve') this.startSolving();
    if (m === 'maia') this.prepareMaia();
    if (m === 'game') this.loadGame();
    if (m === 'tree') this.loadTree();
  }

  /** „Lösen“ wird für dieses Diagramm angeboten (Server, `BookSolverLine.ForReader`; alte Antworten ohne Feld: ja). */
  get solvable(): boolean { return this.diagram?.solvable !== false; }

  /** Die vorzuspielenden Gegnerzüge: bei „Gegner zuerst“ der erste Zug der gespeicherten Linie, sonst keiner. */
  private get lead(): string[] {
    const line = this.storedLine;
    return this.diagram?.opponentFirst && line.length >= 2 ? [line[0]] : [];
  }

  /** Die Farbe, die man spielt: gegen Maia die Seite am Zug der Buchstellung, beim Lösen der Löser — bei „Gegner zuerst“
   *  also die ANDERE Seite (der Löser spielt nie die Seite, die in der Linie mattgesetzt wird). */
  get userColor(): 'w' | 'b' {
    const side = sideToMove(this.diagram?.fen);
    return this.mode() === 'solve' && this.lead.length > 0 ? (side === 'w' ? 'b' : 'w') : side;
  }

  /** Lösen: der vorgespielte Gegnerzug steht noch aus. */
  get leadPending(): boolean { return this.mode() === 'solve' && this.played().length < this.lead.length; }

  private isUserTurn(): boolean {
    return sideToMove(this.boardFen()) === this.userColor;
  }

  onBoardMove(m: UserBoardMove): void {
    const uci = m.from + m.to + (m.promotion ?? '');
    switch (this.mode()) {
      case 'free':
        this.played.update(l => [...l, { san: m.san, fen: m.fen, uci }]);
        break;
      case 'calc':
        this.calcMove(m, uci);
        break;
      case 'solve':
        this.solveMove(m, uci);
        break;
      case 'maia':
        this.played.update(l => [...l, { san: m.san, fen: m.fen, uci }]);
        void this.maiaReply();
        break;
      case 'game':
        this.gameMove(m, uci);
        break;
      case 'tree':
        this.treeMove(m);
        break;
    }
  }

  /** Zug auf dem eingefrorenen Brett: gegen die tatsächliche Stellung prüfen, dann wie ein normaler Brettzug weiter. */
  onFrozenMove(e: { orig: Key; dest: Key; promotion?: string }): void {
    if (!this.playable()) return;   // das Viz-Brett nimmt auch während der Antwort Züge an
    const chess = tryLoadFen(this.boardFen());
    const mv = chess ? tryFreeMove(chess, e.orig, e.dest, e.promotion) : null;
    if (!chess || !mv) return;
    this.onBoardMove({ from: mv.from, to: mv.to, san: mv.san, fen: chess.fen(), ...(mv.promotion ? { promotion: mv.promotion as UserBoardMove['promotion'] } : {}) });
  }

  undo(): void {
    if (this.mode() === 'maia') {
      // Den eigenen Zug samt Maias Antwort zurück.
      this.played.update(l => l.slice(0, Math.max(0, l.length - (this.isUserTurn() ? 2 : 1))));
      return;
    }
    this.played.update(l => l.slice(0, -1));
  }

  reset(): void {
    this.clearTimer();
    this.played.set([]);
    this.maiaThinking.set(false);
    this.peek.set(false);
    // Mitten im Lösen zählen Fehler und Tipps weiter (wie „Zurücksetzen“ im Puzzle-Löser); „Nochmal“ danach ist ein neuer Versuch.
    if (this.mode() === 'solve') this.startSolving(this.solveState() !== 'running');
  }

  // ---------------------------------------------------------------- Kalkulieren

  private calcKey(): string { return `rh.ebooks.calc.${this.diagram.id}`; }

  private loadCalc(): void {
    const store = localStore();
    const tree = store ? deserializeTree(readRaw(store, this.calcKey()), this.diagram.fen) : null;
    this.calcTree = tree ?? createTree(this.diagram.fen);
    this.calcCursor.set(rootNode(this.calcTree).id);
    this.calcRevision.update(v => v + 1);
  }

  private saveCalc(): void {
    const store = localStore();
    if (!store || !this.calcTree) return;
    if (!writeRaw(store, this.calcKey(), serializeTree(this.calcTree))) {
      this.snackbar.warn(this.translate.instant('ebooks.board.calcSaveFailed'));
    }
  }

  private calcMove(m: UserBoardMove, uci: string): void {
    if (!this.calcTree) this.loadCalc();
    const added = addMove(this.calcTree!, this.calcCursor(), { san: m.san, uci, fen: m.fen });
    this.calcCursor.set(added.id);
    this.calcRevision.update(v => v + 1);
    this.saveCalc();
  }

  calcSelect(id: number): void { this.calcCursor.set(id); }
  calcNewLine(): void { if (this.calcTree) this.calcCursor.set(rootNode(this.calcTree).id); }
  calcDelete(leafId: number): void {
    if (!this.calcTree) return;
    this.calcCursor.set(removeLine(this.calcTree, leafId));
    this.calcRevision.update(v => v + 1);
    this.saveCalc();
  }
  /** Eine notierte Linie im Analysebrett prüfen. */
  calcAnalyze(leafId: number): void {
    if (!this.calcTree) return;
    const moves = pathToRoot(this.calcTree, leafId).map(n => n.uci).filter(u => !!u);
    void this.router.navigate(['/analysis'], { queryParams: {
      fen: this.diagram.fen, moves: moves.join(','), orientation: this.flipped() ? 'black' : 'white', from: `/ebooks/${this.ebookId}`,
    } });
  }
  get calcLineCount(): number { return this.calcTree ? calcLines(this.calcTree).filter(l => l.moves.length > 0).length : 0; }

  // ---------------------------------------------------------------- Lösen

  get hasStoredSolution(): boolean { return this.storedLine.length > 0; }
  private get storedLine(): string[] { return (this.diagram?.solutionUci ?? '').split(' ').filter(x => x.length >= 4); }

  /** Die Linie, gegen die gerade gelöst wird — ab der Buchstellung, also samt vorgespieltem Gegnerzug. */
  get solutionLine(): string[] {
    if (this.solveSource() !== 'engine') return this.storedLine;
    const engine = this.engineLine() ?? [];
    return engine.length > 0 ? [...this.lead, ...engine] : [];
  }

  setSolveSource(s: EbookSolveSource): void {
    this.solveSource.set(s);
    this.showSolution.set(false);
    if (this.mode() === 'solve') this.startSolving();
  }

  /** Gemerkte Art des Lösens; ohne (gültigen) Eintrag „Training“. */
  static loadSolveStyle(): EbookSolveStyle {
    const raw = readRaw(localStore(), EbookBoardComponent.SOLVE_STYLE_KEY);
    return raw === 'easy' || raw === 'training' ? raw : 'training';
  }

  toggleAutoNext(): void {
    const on = !this.autoNext();
    this.autoNext.set(on);
    writeRaw(localStore(), EbookBoardComponent.AUTO_NEXT_KEY, on ? '1' : '0');   // nur Bequemlichkeit
  }

  setSolveStyle(s: EbookSolveStyle): void {
    this.solveStyle.set(s);
    writeRaw(localStore(), EbookBoardComponent.SOLVE_STYLE_KEY, s);   // nur Bequemlichkeit — scheitert still
    if (this.mode() === 'solve') this.startSolving();
  }

  /** Neuer Lösungsversuch. `fresh` (Start, neues Diagramm, „Nochmal“ nach dem Ende) vergisst auch Fehler und Tipps;
   *  „Zurücksetzen“ mitten im Lösen nicht — wie im Puzzle-Löser zählt ein Fehlzug bis zum nächsten Diagramm. */
  startSolving(fresh = true): void {
    this.clearTimer();
    this.played.set([]);
    this.wrongAt.set(null);
    this.epoch++;
    if (fresh) { this.mistake.set(false); this.hintLadder.maxLevel = 0; }
    this.hintLadder.level = 0;
    this.alternative.set(false);
    this.mouseslipUsed.set(false);
    this.deviated.set(false);
    this.deviatedAt = null;
    this.checking.set(false);
    if (this.solveSource() === 'engine' && this.engineLine() == null) {
      this.solveState.set('idle');
      void this.computeEngineLine().then(() => { if (this.mode() === 'solve' && this.engineLine()?.length) this.startSolving(); });
      return;
    }
    this.solveState.set(this.solutionLine.length > 0 ? 'running' : 'idle');
    if (this.solveState() === 'running' && this.lead.length > 0) this.playLead();
  }

  /** „Gegner zuerst“ (wie eine Lichess-Aufgabe): nach kurzer Pause den Gegnerzug ausführen, dann ist der Löser am Zug. */
  private playLead(): void {
    const epoch = this.epoch;
    const u = this.lead[0];
    this.timer = setTimeout(() => {
      if (epoch !== this.epoch || this.mode() !== 'solve' || this.played().length > 0) return;
      if (!this.pushUci(u)) this.solveState.set('idle');   // sollte nicht vorkommen: der Server hat die Linie nachgespielt
      this.cdr.markForCheck();
    }, EbookBoardComponent.LEAD_DELAY_MS);
  }

  /** Stockfish-Lösung im Browser: bester Zug, Antwort, … bis {@link ENGINE_SOLUTION_PLIES} Halbzüge oder Partieende. */
  async computeEngineLine(): Promise<void> {
    if (this.engineLineBusy() || !this.legal()) return;
    const id = this.diagram.id;
    this.engineLineBusy.set(true);
    try {
      const chess = new Chess(this.diagram.fen);
      // „Gegner zuerst“: Stockfish rechnet ab der Stellung NACH dem Gegnerzug — auch hier löst nicht die Verliererseite.
      for (const u of this.lead) chess.move({ from: u.slice(0, 2), to: u.slice(2, 4), promotion: u.length > 4 ? u[4] : undefined });
      const line: string[] = [];
      for (let i = 0; i < ENGINE_SOLUTION_PLIES && !chess.isGameOver(); i++) {
        const r = await this.stockfish.getBestMove(chess.fen(), 16);
        if (this.diagram.id !== id || !r?.move) break;
        const u = r.move;
        chess.move({ from: u.slice(0, 2), to: u.slice(2, 4), promotion: u.length > 4 ? u[4] : undefined });
        line.push(u);
      }
      if (this.diagram.id === id) this.engineLine.set(line);
    } catch {
      this.snackbar.warn(this.translate.instant('ebooks.board.engineFailed'));
    } finally {
      this.engineLineBusy.set(false);
      this.cdr.markForCheck();
    }
  }

  /** Suchtiefe der Antworten und der Prüfung — die Buch-Einstellung des Puzzle-Lösers (1.011.0, vorher fest 14). */
  get depth(): number { return this.prefs.bookStockfishDepth; }

  private solveMove(m: UserBoardMove, uci: string): void {
    if (this.solveState() !== 'running' || this.checking()) return;
    const idx = this.played().length;
    const line = this.solutionLine;
    if (idx < this.lead.length || (this.wrongAt() == null && idx >= line.length)) return;
    const before = this.boardFen();
    this.played.update(l => [...l, { san: m.san, fen: m.fen, uci }]);
    if (this.wrongAt() != null) { this.reply(); return; }   // abseits der Lösung: Stockfish antwortet
    const onBook = !this.deviated() && (uci === line[idx] || (line[idx].length === 4 && uci.startsWith(line[idx])));
    if (onBook) { this.advance(); return; }
    // Ein abweichender Zug, der selbst mattsetzt, ist eine Lösung (wie im Puzzle-Löser) — ohne Prüfung, auch ohne Engine.
    if (EbookBoardComponent.isMate(m.fen)) { this.finish('solved', true); return; }
    // Abweichung (0.780.3): zählt, wenn der Zug das Ergebnis hält — Remis bleibt Remis, Gewinn bleibt Gewinn. Viele
    // Buchstellungen haben mehrere richtige Züge (z. B. ein Remis, das das Buch nur mit einer Linie belegt).
    const reference = this.deviated() ? null : (() => {
      try { const c = new Chess(before); applyUci(c, line[idx]); return c.fen(); } catch { return null; }
    })();
    const epoch = this.epoch;
    this.checking.set(true);
    void this.holdsResult(before, m.fen, reference).then(r => {
      if (epoch !== this.epoch || this.mode() !== 'solve') return;
      this.checking.set(false);
      if (r.ok && this.findBookMove) {
        // Auch gut, aber weiter den Buchzug suchen lassen (Nutzerwunsch 29.10.2026) — kein Fehler, nur zurücknehmen.
        this.snackbar.quick(this.translate.instant('ebooks.board.alternativeFindBook'));
        this.timer = setTimeout(() => { this.played.update(l => l.slice(0, -1)); this.cdr.markForCheck(); }, 600);
      } else if (r.ok) {
        this.deviated.set(true);
        this.deviatedAt = idx;
        this.snackbar.quick(this.translate.instant('ebooks.board.alternativeOk'));
        this.advance();
      } else {
        this.wrongMove(idx, r.reply);
      }
      this.cdr.markForCheck();
    });
  }

  /** Gleichwertiger Zug statt des Buchzugs: „auch gut“, aber zurücknehmen, damit man den Buchzug noch findet.
   *  Nicht bei der Stockfish-Linie (dort gibt es keinen Buchzug) und nicht, wenn die Prüfung den Buchzug als fraglich
   *  markiert hat — dann darf der bessere eigene Zug die Lösung sein (Antworten danach wie bisher von Stockfish). */
  private get findBookMove(): boolean {
    return this.solveSource() === 'stored' && this.diagram?.solutionCheck !== 'doubtful';
  }

  /** Falscher Zug (1.011.0, wie im Puzzle-Löser): kein „falsch“ — der Zug bleibt, der Fehler wird gemerkt, Stockfish
   *  antwortet (die beste Antwort kam schon aus der Prüfung mit), und man spielt gegen Stockfish weiter. */
  private wrongMove(idx: number, reply: string | null): void {
    this.mistake.set(true);
    this.wrongAt.set(idx);
    this.hintLadder.level = 0;   // die „falsch abgebogen“-Tipps beginnen bei Stufe 1
    if (EbookBoardComponent.isOver(this.boardFen())) { this.gameOver(); return; }
    if (reply && this.pushUci(reply)) { this.afterReply(); return; }
    this.reply();
  }

  /** Abseits der Lösung: Stockfish spielt den besten Gegenzug — verworfen, wenn inzwischen zurückgenommen, neu begonnen
   *  oder das Diagramm gewechselt wurde. Steht die Partie (Matt/Patt), ist der Versuch zu Ende. */
  private reply(): void {
    if (EbookBoardComponent.isOver(this.boardFen())) { this.gameOver(); return; }
    const epoch = this.epoch;
    this.checking.set(true);
    void this.stockfish.getBestMove(this.boardFen(), this.depth).then(r => r.move || null, () => null).then(u => {
      if (epoch !== this.epoch || this.mode() !== 'solve') return;
      this.checking.set(false);
      // Engine weg: keine Antwort — wie im Puzzle-Löser bleiben Mausrutscher und Zurücksetzen.
      if (u && !this.isUserTurn() && this.pushUci(u)) this.afterReply();
      this.cdr.markForCheck();
    });
  }

  private afterReply(): void {
    if (EbookBoardComponent.isOver(this.boardFen())) this.gameOver();
  }

  /** Partieende abseits der Lösung (wie `handleGameOver` im Puzzle-Löser): selbst mattgesetzt → gelöst (eigene Lösung),
   *  selbst matt → nicht gelöst, das Matt bleibt stehen; Patt/Remis → nicht gelöst (nach einer gleichwertigen Abweichung:
   *  gelöst, das Remis war das Ziel). */
  private gameOver(): void {
    const c = tryLoadFen(this.boardFen());
    if (c?.isCheckmate()) { this.finish(c.turn() === this.userColor ? 'failed' : 'solved', c.turn() !== this.userColor); return; }
    this.finish(this.wrongAt() == null ? 'solved' : 'failed');
  }

  /** Ende des Versuchs. Gelöst auf der Linie und „Automatisch weiter“ an: Countdown zum nächsten Diagramm (wie im
   *  Puzzle-Löser nicht nach einer eigenen Lösung und nicht, wenn zu einem Zug alle drei Tipps geholt wurden). */
  private finish(result: 'solved' | 'failed', alternative = false): void {
    this.clearTimer();
    this.epoch++;
    this.checking.set(false);
    this.alternative.set(alternative);
    this.solveState.set(result);
    if (result === 'solved' && !alternative && !this.hintLadder.failsSolve && this.autoNext() && this.hasNext()) {
      const id = this.diagram.id;
      this.nextTicker.start(EbookBoardComponent.AUTO_NEXT_SECONDS, () => {
        if (this.diagram.id === id && this.solveState() === 'solved') this.solvedNext.emit();
      });
    }
  }

  /** „Nächstes Diagramm“ (Knopf, Leertaste/Enter) — sofort, ohne den Countdown abzuwarten. */
  goNext(): void {
    this.nextTicker.stop();
    this.solvedNext.emit();
  }

  /** Ergebnis-Text wie in der Statuskarte des Puzzle-Lösers. Mit Fehler oder allen drei Tipps zu einem Zug „mehr oder
   *  weniger“ (die Lösung steht trotzdem selbst gespielt auf dem Brett). */
  get resultKey(): string {
    if (this.solveState() === 'failed') return 'solver.status.incorrect';
    if (this.mistake() || this.hintLadder.failsSolve) return 'solver.status.correctKindOf';
    return this.alternative() ? 'solver.status.checkmate' : 'solver.status.correct';
  }

  /** Statuszeile während des Lösens — die Sätze des Puzzle-Lösers; auf dem Lösungsweg der des Buchs (mit der Farbe, die
   *  man spielt, bei „Gegner zuerst“). */
  get statusKey(): string {
    if (this.leadPending) return 'solver.status.watchOpponent';
    if (this.checking()) return 'solver.status.stockfishThinking';
    if (this.wrongAt() != null) return 'book.status.yourMoveVsStockfish';
    if (!this.diagram.opponentFirst) return 'ebooks.reader.yourMove';
    return this.userColor === 'w' ? 'ebooks.board.yourMoveWhite' : 'ebooks.board.yourMoveBlack';
  }

  /** Nicht gelöst, weil der Löser matt steht — das Matt bleibt auf dem Brett (wie im Puzzle-Löser). */
  get userMated(): boolean {
    const c = this.solveState() === 'failed' ? tryLoadFen(this.boardFen()) : null;
    return !!c && c.isCheckmate() && c.turn() === this.userColor;
  }

  /** Mausrutscher (wie im Puzzle-Löser): einmal je Versuch das letzte Zugpaar zurück (rechnet die Antwort noch: nur den
   *  eigenen Zug) — auf dem Lösungsweg wie danach; ein Fehlzug bleibt gezählt. Der vorgespielte Gegnerzug bleibt. */
  get canMouseslip(): boolean {
    return this.mode() === 'solve' && this.solveState() === 'running' && !this.mouseslipUsed() && this.played().length > this.lead.length;
  }

  mouseslip(): void {
    if (!this.canMouseslip) return;
    this.epoch++;
    this.clearTimer();
    this.checking.set(false);
    const keep = Math.max(this.lead.length, this.played().length - (this.isUserTurn() ? 2 : 1));
    this.played.update(l => l.slice(0, keep));
    const at = this.wrongAt();
    if (at != null && keep <= at) this.wrongAt.set(null);
    if (this.deviatedAt != null && keep <= this.deviatedAt) { this.deviated.set(false); this.deviatedAt = null; }
    this.mouseslipUsed.set(true);
    this.hintLadder.level = 0;
  }

  /** „Zurücksetzen“ während des Lösens (wie im Puzzle-Löser) — Fehler und Tipps bleiben gezählt. */
  get canResetSolve(): boolean { return this.solveState() === 'running' && this.played().length > this.lead.length; }

  // ---- Tipps (die gestuften des Puzzle-Lösers: Zugart → Figur → Zug; abseits der Lösung „falsch abgebogen“) ----
  private readonly tr = (k: string, p?: object) => this.translate.instant(k, p) as string;

  /** Die Tipp-Stufen zum gerade erwarteten Zug; leer, solange Stockfish rechnet oder nach einer gleichwertigen Abweichung
   *  (dort gibt es keinen Buchzug mehr). */
  get hintTexts(): string[] {
    if (this.mode() !== 'solve' || this.solveState() !== 'running' || this.checking() || this.leadPending) return [];
    const at = this.wrongAt();
    if (at != null) return offPathHintTexts(this.tr, Math.floor((at - this.lead.length) / 2) + 1);
    if (this.deviated() || !this.isUserTurn()) return [];
    const u = this.solutionLine[this.played().length];
    return u ? buildStagedHints(classifyMoveFromFen(this.boardFen(), u), this.tr) : [];
  }
  get shownHints(): string[] { return this.hintLadder.shown(this.hintTexts); }
  get canShowMoreHints(): boolean { return this.hintLadder.canReveal(this.hintTexts.length); }
  showNextHint(): void { this.hintLadder.reveal(this.hintTexts.length, this.wrongAt() == null); }

  /** Zustand für die Vollbild-Leiste des Puzzle-Lösers ({@link BoardFsActionsComponent}). */
  get fsState(): string {
    if (this.solveState() !== 'running') return 'SOLVED';
    if (this.checking() || !this.isUserTurn()) return 'THINKING';
    return this.wrongAt() != null ? 'PLAYING' : 'AWAITING_USER_MOVE';
  }

  /** Leertaste/Enter = „Nächstes Diagramm“, sobald der Versuch zu Ende ist (wie im Puzzle-Löser). Die Pfeile bleiben beim
   *  Leser (Blättern); ein fokussierter Knopf oder Link behält seine Taste. */
  @HostListener('document:keydown', ['$event'])
  onNextKey(e: KeyboardEvent): void {
    if (this.mode() !== 'solve' || (this.solveState() !== 'solved' && this.solveState() !== 'failed') || !this.hasNext()) return;
    if ((e.key !== ' ' && e.key !== 'Spacebar' && e.key !== 'Enter') || !isBoardHotkey(e)) return;
    if ((e.target as HTMLElement | null)?.closest?.('button, a, [role="button"], [role^="menuitem"]')) return;
    e.preventDefault();
    this.goNext();
  }

  private static isMate(fen: string): boolean { try { return new Chess(fen).isCheckmate(); } catch { return false; } }
  private static isOver(fen: string): boolean { try { return new Chess(fen).isGameOver(); } catch { return true; } }

  /**
   * Hält der gespielte Zug das Ergebnis? Stockfish bewertet die Stellung nach dem Zug und die Vergleichsstellung (nach dem
   * Buchzug; nach einer Abweichung die Stellung davor mit bestem Spiel) — jeweils aus Sicht des Ziehenden. Gilt, wenn er
   * höchstens 50 cp schlechter ist, ein Remis ein Remis bleibt (beide ±80 cp) oder ein Gewinn ein Gewinn (beide ≥ 300).
   * `reply` ist die beste Antwort auf den Zug (aus derselben Suche) — die Widerlegung, falls er nicht hält.
   */
  private async holdsResult(before: string, after: string, reference: string | null): Promise<{ ok: boolean; reply: string | null }> {
    const mover = whiteToMove(before) ? 1 : -1;
    let reply: string | null = null;
    try {
      const mine = await this.stockfish.getBestMove(after, this.depth);
      reply = mine.move || null;
      const ref = await this.stockfish.getBestMove(reference ?? before, this.depth);
      const a = scoreCp(mine.score, after) * mover;
      const b = scoreCp(ref.score, reference ?? before) * mover;
      return { ok: sameResult(b, a), reply };
    } catch {
      return { ok: false, reply };   // Engine nicht verfügbar: wie bisher nur der Buchzug
    }
  }

  /** Nach einem richtigen Zug: Antwort nachziehen (aus der Linie, nach einer Abweichung von Stockfish), am Ende „gelöst“. */
  private advance(): void {
    const line = this.solutionLine;
    if (this.played().length >= line.length || EbookBoardComponent.isOver(this.boardFen())) { this.lineEnd(); return; }
    const epoch = this.epoch;
    this.timer = setTimeout(async () => {
      let u: string | null = line[this.played().length];
      if (this.deviated()) {
        this.checking.set(true);
        try { u = (await this.stockfish.getBestMove(this.boardFen(), this.depth)).move || null; } catch { u = null; }
      }
      if (epoch !== this.epoch || this.mode() !== 'solve') return;
      this.checking.set(false);
      if (!u || !this.pushUci(u) || this.played().length >= line.length || EbookBoardComponent.isOver(this.boardFen())) this.lineEnd();
      else this.hintLadder.level = 0;   // Tipps gelten je Zug
      this.cdr.markForCheck();
    }, EbookBoardComponent.REPLY_DELAY_MS);
  }

  /** Linie zu Ende (oder Stockfish hat nach einer Abweichung nichts mehr): gelöst — außer der Löser steht matt. */
  private lineEnd(): void {
    const c = tryLoadFen(this.boardFen());
    this.finish(c?.isCheckmate() && c.turn() === this.userColor ? 'failed' : 'solved');
  }

  private pushUci(u: string): boolean {
    try {
      const chess = new Chess(this.boardFen());
      const mv = applyUci(chess, u);
      if (!mv) return false;
      this.played.update(l => [...l, { san: mv.san, fen: chess.fen(), uci: u }]);
      return true;
    } catch {
      return false;
    }
  }

  // ---------------------------------------------------------------- Partie

  /** Partie zum Diagramm laden (bei jedem neuen Diagramm im Modus „Partie“); ohne hinterlegte Partie „missing“. */
  private loadGame(): void {
    const d = this.diagram;
    const req = ++this.gameRequest;
    this.played.set([]);
    if (!d?.hasGame || !this.ebookId) { this.gameLine.set(null); this.gameState.set('missing'); return; }
    const cur = this.gameLine();
    this.gameState.set('loading');
    this.ebooks.diagramGame(this.ebookId, d.id).subscribe({
      next: g => {
        if (req !== this.gameRequest) return;
        // Gleiche Partie wie beim vorigen Diagramm: nur zum neuen Halbzug springen.
        const line = cur && cur.game.id === g.id ? { ...cur, game: g } : replayGame(g);
        this.gameLine.set(line);
        this.gamePly.set(Math.min(g.ply, line.sans.length));
        this.gameState.set('ready');
        this.cdr.markForCheck();
      },
      error: () => {
        if (req !== this.gameRequest) return;
        this.gameLine.set(null);
        this.gameState.set('missing');
        this.cdr.markForCheck();
      },
    });
  }

  /** Brettzug in der Partie: ist es der nächste Partiezug, geht es in der Partie weiter, sonst beginnt (bzw. wächst) die Nebenlinie. */
  private gameMove(m: UserBoardMove, uci: string): void {
    const g = this.gameLine();
    if (!g) return;
    if (this.played().length === 0 && g.fens[this.gamePly() + 1] === m.fen) { this.gamePly.update(p => p + 1); return; }
    this.played.update(l => [...l, { san: m.san, fen: m.fen, uci }]);
  }

  /** In der Partie springen (Halbzug 0 … Partieende); verlässt eine Nebenlinie. */
  gameGo(ply: number): void {
    const g = this.gameLine();
    if (!g) return;
    this.played.set([]);
    this.gamePly.set(Math.max(0, Math.min(ply, g.sans.length)));
  }

  /** ◀/▶: in einer Nebenlinie nimmt ◀ deren letzten Zug zurück; sonst einen Halbzug in der Partie. */
  gameStep(delta: number): void {
    if (this.played().length > 0) { if (delta < 0) this.played.update(l => l.slice(0, -1)); return; }
    this.gameGo(this.gamePly() + delta);
  }

  /** Zugliste der Partie als eine Zeile je Vollzug (DESIGN „Zuglisten“): Nummer, Weiß, Schwarz — je mit dem Halbzug,
   *  auf den ein Klick springt (1 = nach 1. Zug von Weiß). */
  readonly gameRows = computed(() => {
    const sans = this.gameLine()?.sans ?? [];
    const rows: { no: number; white: { san: string; ply: number }; black?: { san: string; ply: number } }[] = [];
    for (let i = 0; i < sans.length; i += 2) {
      rows.push({ no: i / 2 + 1, white: { san: sans[i], ply: i + 1 }, ...(i + 1 < sans.length ? { black: { san: sans[i + 1], ply: i + 2 } } : {}) });
    }
    return rows;
  });

  /** UI-Sweep 2026-11-10 (d10-rk-ebook-partiekopf-pgn-rohwerte): Kopf als Feldliste (Trenner `.rh-dots`), Datum im
   *  Locale-Format, PGN-Platzhalter „*“ fällt weg. */
  get gameHead(): string[] {
    const g = this.gameLine()?.game;
    if (!g) return [];
    return [[g.white, g.black].filter(Boolean).join(' – '), g.event, cardDate(g.date, cardLang(this.translate)), this.gameResult]
      .filter((x): x is string => !!x);
  }

  /** Ergebnis zum Anzeigen: „*“/leer → leer, „1/2-1/2“ → „½–½“, sonst „1–0“/„0–1“. */
  get gameResult(): string {
    return ebookResultLabel(this.gameLine()?.game?.result);
  }

  /** Die Nebenlinie mit Zugnummern ab der Partiestellung, von der sie abzweigt. */
  get sideLineText(): string {
    const g = this.gameLine();
    if (!g || this.played().length === 0) return '';
    const fen = g.fens[this.gamePly()];
    const parts = fen.split(' ');
    parts[5] = String(Math.floor(this.gamePly() / 2) + 1);
    return numberedSans(parts.join(' '), this.played().map(p => p.san));
  }

  // ---------------------------------------------------------------- Baum

  /** Baum laden (einmal je Buch) und den Knoten der Diagrammstellung suchen; nicht gefunden → Grundstellung mit Hinweis. */
  private loadTree(): void {
    const req = ++this.treeRequest;
    this.treeOffFen.set(null);
    const cached = EbookBoardComponent.treeCache;
    if (cached && cached.ebookId === this.ebookId) { this.openingTree.set(cached.tree); this.treeState.set('ready'); this.locateTree(); return; }
    if (!this.ebookId) { this.treeState.set('missing'); return; }
    this.treeState.set('loading');
    this.ebooks.tree(this.ebookId).subscribe({
      next: wire => {
        if (req !== this.treeRequest) return;
        const tree = new EbookOpeningTree(wire);
        EbookBoardComponent.treeCache = { ebookId: this.ebookId, tree };
        this.openingTree.set(tree);
        this.treeState.set('ready');
        this.locateTree();
        this.cdr.markForCheck();
      },
      error: () => {
        if (req !== this.treeRequest) return;
        this.openingTree.set(null);
        this.treeState.set('missing');
        this.cdr.markForCheck();
      },
    });
  }

  private locateTree(): void {
    const t = this.openingTree();
    if (!t || !this.diagram) return;
    const hit = t.find(this.diagram.fen);
    this.treeCursor.set(hit.node ?? 0);
    this.treeNotInTree.set(hit.node === null);
    // Gedreht gesetztes Diagramm (Schwarz unten): so zeigen, wie das Buch es zeigt.
    if (hit.rotated) this.flipped.set(true);
  }

  treeGo(node: number): void {
    const t = this.openingTree();
    if (!t || node < 0 || node >= t.fen.length || (node > 0 && t.parent[node] < 0)) return;
    this.treeOffFen.set(null);
    this.treeCursor.set(node);
  }

  /** Brettzug: steht er im Buch, geht es dorthin; sonst kurz zeigen, „nicht im Buch“, zurück. */
  private treeMove(m: UserBoardMove): void {
    const t = this.openingTree();
    if (!t) return;
    const child = t.children[this.treeCursor()].find(c => t.san[c] === m.san);
    if (child !== undefined) { this.treeGo(child); return; }
    this.treeOffFen.set(m.fen);
    this.snackbar.quick(this.translate.instant('ebooks.tree.notInBook'));
    this.clearTimer();
    this.timer = setTimeout(() => { this.treeOffFen.set(null); this.cdr.markForCheck(); }, 600);
  }

  /** ← / → im Baum (Knoten davor / Hauptfortsetzung), solange der Fokus im Brett-Bereich liegt — sonst blättern die Pfeile
   *  weiter das Buch (der Leser lässt eine schon verarbeitete Taste liegen). */
  @HostListener('keydown', ['$event'])
  onTreeKey(e: KeyboardEvent): void {
    if (this.mode() !== 'tree' || this.treeState() !== 'ready' || !isBoardHotkey(e)) return;
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
    const t = this.openingTree()!;
    const cur = this.treeCursor();
    if (e.key === 'ArrowLeft' && cur > 0) this.treeGo(t.parent[cur]);
    if (e.key === 'ArrowRight' && t.children[cur].length > 0) this.treeGo(t.children[cur][0]);
    e.preventDefault();   // auch am Rand des Baums: kein Blättern im Buch, während man im Baum ist
  }

  // ---------------------------------------------------------------- Maia

  setMaiaElo(elo: number): void { this.maiaElo.set(elo); }

  loadMaia(): void { this.maiaUsed = true; void this.maia.download(); }

  private prepareMaia(): void {
    this.maiaUsed = true;
    void this.maia.prepare();
  }

  private async maiaReply(): Promise<void> {
    const fen = this.boardFen();
    try { if (new Chess(fen).isGameOver()) return; } catch { return; }
    this.maiaThinking.set(true);
    const id = this.diagram.id;
    try {
      const u = await this.maia.chooseMove(fen, this.maiaElo());
      if (this.diagram.id !== id || this.mode() !== 'maia' || this.boardFen() !== fen || !u) return;
      this.pushUci(u);
    } catch {
      this.snackbar.warn(this.translate.instant('ebooks.board.maiaFailed'));
    } finally {
      this.maiaThinking.set(false);
      this.cdr.markForCheck();
    }
  }

  // ---------------------------------------------------------------- Engine

  toggleEngine(): void {
    const s = this.engine();
    if (s) { s.destroy(); this.engine.set(null); return; }
    this.engine.set(new LiveEngineSession());
  }

  // ---------------------------------------------------------------- Anzeige

  get movesText(): string {
    return this.diagram ? numberedSans(this.diagram.fen, this.played().map(m => m.san)) : '';
  }

  /** Remisstellung laut Stockfish-Prüfung (±80 cp) — dann ist die gezeigte Linie nur eine von mehreren (0.780.3). */
  get drawish(): boolean {
    const e = this.diagram?.engineEval;
    return e != null && Math.abs(e) <= 80;
  }

  /** Die Lösung zum Lesen (gespeichert: SAN aus dem Buch; Stockfish: aus der gerechneten Linie). */
  get solutionText(): string {
    const d = this.diagram;
    if (!d) return '';
    if (this.solveSource() === 'engine') {
      const sans: string[] = [];
      try {
        const c = new Chess(d.fen);
        for (const u of this.solutionLine) sans.push(c.move({ from: u.slice(0, 2), to: u.slice(2, 4), promotion: u.length > 4 ? u[4] : undefined }).san);
      } catch { /* Linie passt nicht mehr — was da ist, zeigen */ }
      return numberedSans(d.fen, sans);
    }
    return this.storedSolutionText;
  }

  /** Die gespeicherte Linie zum Lesen (auch ohne „Lösen“, unter „Ansehen“). */
  get storedSolutionText(): string {
    const d = this.diagram;
    return d ? numberedSans(d.fen, (d.solutionSan ?? '').split(' ').filter(Boolean)) : '';
  }

  analysisParams(): Record<string, string> {
    return { fen: this.boardFen() || this.diagram.fen, orientation: this.flipped() ? 'black' : 'white', from: `/ebooks/${this.ebookId}` };
  }

  copyFen(): void {
    navigator.clipboard?.writeText(this.boardFen() || this.diagram.fen).then(
      () => this.snackbar.quick(this.translate.instant('ebooks.reader.fenCopied')),
      () => this.snackbar.warn(this.translate.instant('ebooks.reader.copyFailed')),
    );
  }

  modeIcon(m: EbookBoardMode): string {
    return { view: 'visibility', free: 'pan_tool_alt', calc: 'account_tree', solve: 'extension', maia: 'smart_toy', game: 'replay', tree: 'park' }[m];
  }

  private clearTimer(): void {
    if (this.timer) { clearTimeout(this.timer); this.timer = null; }
    this.nextTicker.stop();
  }
}
