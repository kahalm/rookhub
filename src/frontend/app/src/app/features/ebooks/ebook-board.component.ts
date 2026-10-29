import {
  ChangeDetectionStrategy, ChangeDetectorRef, Component, DestroyRef, EventEmitter, Input, OnChanges, Output, SimpleChanges,
  computed, effect, inject, signal, untracked,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
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
import { tryFreeMove, tryLoadFen } from '../../shared/chess/puzzle-move.util';
import { EbookDiagram, EbookGame, EbookService } from './ebook.service';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { sideToMove, whiteToMove } from '../../shared/chess/fen-side';

/** Was das Brett gerade tut (0.777.0). */
export type EbookBoardMode = 'view' | 'free' | 'calc' | 'solve' | 'maia' | 'game';

/** Partie im Modus „Partie“ (0.831.0): Kopf, Hauptlinie und die Stellung nach jedem Halbzug (Index 0 = Grundstellung). */
export interface EbookGameLine {
  game: EbookGame;
  sans: string[];
  fens: string[];
}
/** Lösen: in beiden Arten wird ein falscher Zug zurückgenommen und man sucht weiter (Nutzerwunsch 20.10.2026: keine Lösung
 *  vorspielen); „Training“ merkt sich den Fehler und meldet am Ende „gelöst mit Fehler“. Seit 0.825.0 bleibt im „Training“
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
 *   Stockfish-Lösung; ein falscher Zug wird zurückgenommen, „Training“ merkt sich den Fehler.
 * - **Gegen Maia** — Sparring ab der Buchstellung; man spielt die Seite am Zug, Maia antwortet in der gewählten Stärke.
 * - **Partie** (0.831.0, Partiensammlungen) — die ganze Partie, aus der das Diagramm stammt, ab der Diagrammstellung zum
 *   Durchklicken; eigene Züge werden zur Nebenlinie (Zurück führt in die Partie). Nur mit hinterlegter Partie.
 * Die Engine (Stockfish im Browser, {@link LiveEngineSession}) ist in jedem Modus zuschaltbar.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-ebook-board',
  standalone: true,
  imports: [RouterLink, NgTemplateOutlet, MatButtonModule, MatIconModule, MatMenuModule, MatProgressBarModule,
    TranslatePipe, IconLabelDirective, ChessBoardComponent, PuzzleBoardComponent, CalcLinesComponent, LiveEnginePanelComponent, HelpHintComponent],
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
  /** Gelöst und „Automatisch weiter“ an: der Leser springt zum nächsten Diagramm. */
  @Output() solvedNext = new EventEmitter<void>();

  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly stockfish = inject(StockfishService);
  readonly maia = inject(MaiaEngineService);
  readonly prefs = inject(PreferencesService);

  readonly modes: EbookBoardMode[] = ['view', 'free', 'calc', 'solve', 'maia', 'game'];
  /** Sichtbare Modi: „Partie“ nur, wenn das Diagramm eine Partie hat oder das Buch auf „Partie“ steht (Partiensammlung). */
  get visibleModes(): EbookBoardMode[] {
    return this.diagram?.hasGame || this.defaultMode === 'game' || this.mode() === 'game' ? this.modes : this.modes.filter(m => m !== 'game');
  }
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
  /** Pause vor dem Weiterspringen — Zeit, die eigene Lösung noch anzusehen. */
  static readonly AUTO_NEXT_DELAY_MS = 3000;
  /** Training: in diesem Lösen schon ein falscher Zug gespielt. */
  readonly mistake = signal(false);
  readonly solveSource = signal<EbookSolveSource>('stored');
  readonly solveState = signal<'idle' | 'running' | 'solved'>('idle');
  /** Stockfish-Lösung (UCI), sobald gerechnet; `null` = noch nicht / läuft. */
  readonly engineLine = signal<string[] | null>(null);
  readonly engineLineBusy = signal(false);
  readonly showSolution = signal(false);
  /** Training: das eingefrorene Brett kurz auf die aktuelle Stellung umschalten (Auge, Nutzerwunsch 25.10.2026) —
   *  die Lösung hat dafür die Glühbirne. Nur Ansehen; gezogen wird weiter auf dem Viz-Brett. */
  readonly peek = signal(false);
  /** Stockfish prüft gerade einen abweichenden Zug. */
  readonly checking = signal(false);
  /** Vom Buch (bzw. der gespeicherten Linie) abgewichen, aber gleichwertig — Antworten kommen jetzt von Stockfish. */
  readonly deviated = signal(false);
  private timer: ReturnType<typeof setTimeout> | null = null;

  /** Maia. */
  readonly maiaElo = signal<number>(MAIA_DEFAULT_ELO);
  readonly maiaEloOptions = MAIA_ELO_OPTIONS;
  readonly maiaThinking = signal(false);

  /** Partie (0.831.0): geladene Partie, Lade-Zustand und der Halbzug in der Hauptlinie; eigene Züge ab dort in {@link played}. */
  readonly gameLine = signal<EbookGameLine | null>(null);
  readonly gameState = signal<'idle' | 'loading' | 'ready' | 'missing'>('idle');
  readonly gamePly = signal(0);
  private gameRequest = 0;

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

  /** Eingefrorenes Brett: der letzte Zug der Gegenseite als Pfeil (der eigene steht ja im Kopf). */
  readonly opponentArrow = computed<[Key, Key] | undefined>(() => {
    if (this.mode() !== 'solve') return undefined;
    const l = this.played();
    if (l.length === 0 || l.length % 2 === 1) return undefined;
    const u = l[l.length - 1].uci;
    return [u.slice(0, 2) as Key, u.slice(2, 4) as Key];
  });

  readonly noDests = new Map<Key, Key[]>();

  readonly playable = computed(() => {
    if (!this.legal()) return false;
    switch (this.mode()) {
      case 'view': return false;
      case 'solve': return this.solveState() === 'running' && !this.checking() && this.isUserTurn();
      case 'maia': return !this.maiaThinking() && this.isUserTurn() && this.maia.status() === 'ready';
      case 'game': return this.gameState() === 'ready';
      default: return true;
    }
  });

  constructor() {
    // Engine rechnet jede neue Brettstellung (neuer Index = neue Stellung, keine eigene Nebenvariante).
    // `sync` liest und schreibt Signale der Sitzung (baseIndex …) — OHNE untracked hinge der Effekt an ihnen, jeder
    // Schreibzugriff löste ihn neu aus, mit neuem Index: Endlosschleife, die Seite stand (0.801.0, „Engine an“).
    effect(() => {
      const s = this.engine();
      const fen = this.boardFen();
      if (s && fen) untracked(() => s.sync(++this.engineIndex, fen));
    });

    inject(DestroyRef).onDestroy(() => { this.clearTimer(); this.engine()?.destroy(); });
  }

  ngOnChanges(ch: SimpleChanges): void {
    if (!ch['diagram'] || !this.diagram) return;
    const prev = ch['diagram'].previousValue as EbookDiagram | undefined;
    // Nur Meldung o. Ä. geändert; eine korrigierte Stellung (gleiche Id, neue FEN) beginnt dagegen von vorn.
    if (prev?.id === this.diagram.id && prev.fen === this.diagram.fen) { this.start.set(this.diagram); return; }
    this.clearTimer();
    this.start.set(this.diagram);
    if (!prev) this.mode.set(this.defaultMode && this.modes.includes(this.defaultMode) ? this.defaultMode : 'solve');
    this.played.set([]);
    this.engineLine.set(null);
    this.showSolution.set(false);
    this.peek.set(false);
    this.solveState.set('idle');
    this.maiaThinking.set(false);
    this.flipped.set(!whiteToMove(this.diagram.fen));
    if (!this.legal() && this.mode() !== 'view') this.mode.set('view');
    if (!this.hasStoredSolution) this.solveSource.set('engine');
    if (this.mode() === 'calc') this.loadCalc();
    if (this.mode() === 'solve') this.startSolving();
    if (this.mode() === 'maia') void this.maia.prepare();
    if (this.mode() === 'game') this.loadGame();
  }

  // ---------------------------------------------------------------- Modus

  setMode(m: EbookBoardMode): void {
    if (m !== 'view' && !this.legal()) return;
    this.clearTimer();
    this.played.set([]);
    this.solveState.set('idle');
    this.maiaThinking.set(false);
    this.mode.set(m);
    if (m === 'calc') this.loadCalc();
    if (m === 'solve') this.startSolving();
    if (m === 'maia') void this.maia.prepare();
    if (m === 'game') this.loadGame();
  }

  /** Seite am Zug in der Buchstellung — die spielt man beim Lösen und gegen Maia. */
  get userColor(): 'w' | 'b' { return sideToMove(this.diagram?.fen); }

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
    if (this.mode() === 'solve') this.startSolving();
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

  /** Die Linie, gegen die gerade gelöst wird. */
  get solutionLine(): string[] {
    return this.solveSource() === 'engine' ? this.engineLine() ?? [] : this.storedLine;
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

  /** Gelöst; mit „Automatisch weiter“ nach kurzer Pause zum nächsten Diagramm (der Haken bleibt kurz sichtbar). */
  private markSolved(): void {
    this.solveState.set('solved');
    if (!this.autoNext()) return;
    const id = this.diagram.id;
    this.clearTimer();
    this.timer = setTimeout(() => {
      if (this.diagram.id === id && this.solveState() === 'solved') this.solvedNext.emit();
    }, EbookBoardComponent.AUTO_NEXT_DELAY_MS);
  }

  setSolveStyle(s: EbookSolveStyle): void {
    this.solveStyle.set(s);
    writeRaw(localStore(), EbookBoardComponent.SOLVE_STYLE_KEY, s);   // nur Bequemlichkeit — scheitert still
    if (this.mode() === 'solve') this.startSolving();
  }

  startSolving(): void {
    this.clearTimer();
    this.played.set([]);
    this.mistake.set(false);
    this.deviated.set(false);
    this.checking.set(false);
    if (this.solveSource() === 'engine' && this.engineLine() == null) {
      this.solveState.set('idle');
      void this.computeEngineLine().then(() => { if (this.mode() === 'solve' && this.engineLine()?.length) this.startSolving(); });
      return;
    }
    this.solveState.set(this.solutionLine.length > 0 ? 'running' : 'idle');
  }

  /** Stockfish-Lösung im Browser: bester Zug, Antwort, … bis {@link ENGINE_SOLUTION_PLIES} Halbzüge oder Partieende. */
  async computeEngineLine(): Promise<void> {
    if (this.engineLineBusy() || !this.legal()) return;
    const id = this.diagram.id;
    this.engineLineBusy.set(true);
    try {
      const chess = new Chess(this.diagram.fen);
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

  private solveMove(m: UserBoardMove, uci: string): void {
    const line = this.solutionLine;
    const idx = this.played().length;
    if (idx >= line.length || this.solveState() !== 'running' || this.checking()) return;
    const before = this.boardFen();
    const onBook = !this.deviated() && (uci === line[idx] || (line[idx].length === 4 && uci.startsWith(line[idx])));
    this.played.update(l => [...l, { san: m.san, fen: m.fen, uci }]);
    if (onBook) { this.advance(); return; }
    // Abweichung (0.780.3): zählt, wenn der Zug das Ergebnis hält — Remis bleibt Remis, Gewinn bleibt Gewinn. Viele
    // Buchstellungen haben mehrere richtige Züge (z. B. ein Remis, das das Buch nur mit einer Linie belegt).
    const reference = this.deviated() ? null : (() => {
      try {
        const c = new Chess(before); const u = line[idx];
        c.move({ from: u.slice(0, 2), to: u.slice(2, 4), promotion: u.length > 4 ? u[4] : undefined });
        return c.fen();
      } catch { return null; }
    })();
    const id = this.diagram.id;
    this.checking.set(true);
    void this.holdsResult(before, m.fen, reference).then(ok => {
      if (this.diagram.id !== id || this.mode() !== 'solve') return;
      this.checking.set(false);
      if (ok && this.findBookMove) {
        // Auch gut, aber weiter den Buchzug suchen lassen (Nutzerwunsch 29.10.2026) — kein Fehler, nur zurücknehmen.
        this.snackbar.quick(this.translate.instant('ebooks.board.alternativeFindBook'));
        this.timer = setTimeout(() => { this.played.update(l => l.slice(0, -1)); this.cdr.markForCheck(); }, 600);
      } else if (ok) {
        this.deviated.set(true);
        this.snackbar.quick(this.translate.instant('ebooks.board.alternativeOk'));
        this.advance();
      } else {
        this.rejectMove();
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

  /** Falscher Zug: nur „falsch“ sagen und zurücknehmen — weitersuchen; „Training“ merkt sich den Fehler. */
  private rejectMove(): void {
    if (this.solveStyle() === 'training') this.mistake.set(true);
    this.snackbar.quick(this.translate.instant('ebooks.board.wrongMove'));
    this.timer = setTimeout(() => { this.played.update(l => l.slice(0, -1)); this.cdr.markForCheck(); }, 600);
  }

  /**
   * Hält der gespielte Zug das Ergebnis? Stockfish bewertet die Stellung nach dem Zug und die Vergleichsstellung (nach dem
   * Buchzug; nach einer Abweichung die Stellung davor mit bestem Spiel) — jeweils aus Sicht des Ziehenden. Gilt, wenn er
   * höchstens 50 cp schlechter ist, ein Remis ein Remis bleibt (beide ±80 cp) oder ein Gewinn ein Gewinn (beide ≥ 300).
   */
  private async holdsResult(before: string, after: string, reference: string | null): Promise<boolean> {
    const mover = whiteToMove(before) ? 1 : -1;
    try {
      const mine = await this.stockfish.getBestMove(after, EbookBoardComponent.CHECK_DEPTH);
      const ref = await this.stockfish.getBestMove(reference ?? before, EbookBoardComponent.CHECK_DEPTH);
      const a = EbookBoardComponent.scoreCp(mine.score, after) * mover;
      const b = EbookBoardComponent.scoreCp(ref.score, reference ?? before) * mover;
      return EbookBoardComponent.sameResult(b, a);
    } catch {
      return false;   // Engine nicht verfügbar: wie bisher nur der Buchzug
    }
  }

  static readonly CHECK_DEPTH = 14;

  /** Weiß-Sicht in Centipawns; Matt in n = ±(10000 − n). Ohne Bewertung (Partieende): Matt/Patt aus der Stellung. */
  static scoreCp(score: { cp?: number; mate?: number } | undefined, fen: string): number {
    if (score?.mate != null) return score.mate > 0 ? 10000 - score.mate : -10000 - score.mate;
    if (score?.cp != null) return score.cp;
    try {
      const c = new Chess(fen);
      if (c.isCheckmate()) return c.turn() === 'w' ? -10000 : 10000;
    } catch { /* unten */ }
    return 0;   // Patt, Remis, unbekannt
  }

  /** Gleiches Ergebnis aus Sicht des Ziehenden (Vergleich `reference`, eigener Zug `mine`). */
  static sameResult(reference: number, mine: number): boolean {
    if (mine >= reference - 50) return true;
    if (Math.abs(reference) <= 80 && mine >= -80) return true;   // Remis bleibt Remis
    if (reference >= 300 && mine >= 300) return true;            // Gewinn bleibt Gewinn
    return false;
  }

  /** Nach einem richtigen Zug: Antwort nachziehen (aus der Linie, nach einer Abweichung von Stockfish), am Ende „gelöst“. */
  private advance(): void {
    const line = this.solutionLine;
    if (this.played().length >= line.length) { this.markSolved(); return; }
    const id = this.diagram.id;
    this.timer = setTimeout(async () => {
      let u: string | null = line[this.played().length];
      if (this.deviated()) {
        try { u = (await this.stockfish.getBestMove(this.boardFen(), EbookBoardComponent.CHECK_DEPTH)).move || null; } catch { u = null; }
      }
      if (this.diagram.id !== id || this.mode() !== 'solve') return;
      if (!u || !this.pushUci(u) || this.played().length >= line.length) this.markSolved();
      this.cdr.markForCheck();
    }, 450);
  }

  private pushUci(u: string): boolean {
    try {
      const chess = new Chess(this.boardFen());
      const mv = chess.move({ from: u.slice(0, 2), to: u.slice(2, 4), promotion: u.length > 4 ? u[4] : undefined });
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
        const line = cur && cur.game.id === g.id ? { ...cur, game: g } : EbookBoardComponent.replay(g);
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

  /** Hauptlinie nachspielen; ab einem unspielbaren Zug wird abgeschnitten (kaputte Quelle soll den Leser nicht sprengen). */
  static replay(game: EbookGame): EbookGameLine {
    const chess = new Chess();
    const sans: string[] = [];
    const fens = [chess.fen()];
    for (const san of game.moves) {
      try { sans.push(chess.move(san).san); fens.push(chess.fen()); } catch { break; }
    }
    return { game, sans, fens };
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

  get gameHead(): string {
    const g = this.gameLine()?.game;
    if (!g) return '';
    return [[g.white, g.black].filter(Boolean).join(' – '), g.event, g.date, g.result].filter(Boolean).join(' · ');
  }

  /** Die Nebenlinie mit Zugnummern ab der Partiestellung, von der sie abzweigt. */
  get sideLineText(): string {
    const g = this.gameLine();
    if (!g || this.played().length === 0) return '';
    const fen = g.fens[this.gamePly()];
    const parts = fen.split(' ');
    parts[5] = String(Math.floor(this.gamePly() / 2) + 1);
    return EbookBoardComponent.numbered(parts.join(' '), this.played().map(p => p.san));
  }

  // ---------------------------------------------------------------- Maia

  setMaiaElo(elo: number): void { this.maiaElo.set(elo); }

  loadMaia(): void { void this.maia.download(); }

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

  /** Zugnummern ab einer Stellung vor eine SAN-Folge setzen („17… Txh2+ 18. Kxh2“). */
  static numbered(fen: string, sans: readonly string[]): string {
    if (sans.length === 0) return '';
    const parts = fen.split(' ');
    let n = Number(parts[5]) || 1;
    let white = whiteToMove(fen);
    const out: string[] = [];
    sans.forEach((san, i) => {
      if (white) out.push(`${n}. ${san}`);
      else { out.push(i === 0 ? `${n}… ${san}` : san); n++; }
      white = !white;
    });
    return out.join(' ');
  }

  get movesText(): string {
    return this.diagram ? EbookBoardComponent.numbered(this.diagram.fen, this.played().map(m => m.san)) : '';
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
        for (const u of this.engineLine() ?? []) sans.push(c.move({ from: u.slice(0, 2), to: u.slice(2, 4), promotion: u.length > 4 ? u[4] : undefined }).san);
      } catch { /* Linie passt nicht mehr — was da ist, zeigen */ }
      return EbookBoardComponent.numbered(d.fen, sans);
    }
    return EbookBoardComponent.numbered(d.fen, (d.solutionSan ?? '').split(' ').filter(Boolean));
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
    return { view: 'visibility', free: 'pan_tool_alt', calc: 'account_tree', solve: 'extension', maia: 'smart_toy', game: 'replay' }[m];
  }

  private clearTimer(): void {
    if (this.timer) { clearTimeout(this.timer); this.timer = null; }
  }
}
