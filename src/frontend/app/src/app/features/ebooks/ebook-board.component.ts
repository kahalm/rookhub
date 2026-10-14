import {
  ChangeDetectionStrategy, ChangeDetectorRef, Component, DestroyRef, EventEmitter, Input, OnChanges, Output, SimpleChanges,
  computed, effect, inject, signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { SnackbarService } from '../../core/snackbar.service';
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
import { EbookDiagram } from './ebook.service';

/** Was das Brett gerade tut (0.777.0). */
export type EbookBoardMode = 'view' | 'free' | 'calc' | 'solve' | 'maia';
/** Lösen: einfach (falscher Zug wird zurückgenommen, nochmal versuchen) oder Training (ein Versuch, dann zeigt das Brett die Lösung). */
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
 *   Stockfish-Lösung; „Einfach“ nimmt falsche Züge zurück, „Training“ wertet den ersten Versuch und spielt danach die Lösung vor.
 * - **Gegen Maia** — Sparring ab der Buchstellung; man spielt die Seite am Zug, Maia antwortet in der gewählten Stärke.
 * Die Engine (Stockfish im Browser, {@link LiveEngineSession}) ist in jedem Modus zuschaltbar.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-ebook-board',
  standalone: true,
  imports: [RouterLink, MatButtonModule, MatButtonToggleModule, MatIconModule, MatMenuModule, MatProgressBarModule, MatSlideToggleModule,
    TranslatePipe, IconLabelDirective, ChessBoardComponent, CalcLinesComponent, LiveEnginePanelComponent],
  templateUrl: './ebook-board.component.html',
  styleUrls: ['./ebook-board.component.scss'],
})
export class EbookBoardComponent implements OnChanges {
  @Input({ required: true }) diagram!: EbookDiagram;
  @Input() ebookId = 0;
  /** „Zur Lösungsseite“ — der Leser springt dorthin. */
  @Output() solutionPage = new EventEmitter<number>();

  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly stockfish = inject(StockfishService);
  readonly maia = inject(MaiaEngineService);

  readonly modes: EbookBoardMode[] = ['view', 'free', 'calc', 'solve', 'maia'];
  readonly mode = signal<EbookBoardMode>('view');
  readonly flipped = signal(false);
  /** Die Buchstellung als Signal (für computed/effect). */
  readonly start = signal<EbookDiagram | null>(null);

  /** Frei ziehen / Lösen / Maia: gespielte Züge ab der Buchstellung. */
  readonly played = signal<{ san: string; fen: string; uci: string }[]>([]);

  /** Kalkulieren: Variantenbaum + gewählter Knoten; `revision` lässt die Linienliste neu zeichnen. */
  calcTree: CalcTree | null = null;
  readonly calcCursor = signal(0);
  readonly calcRevision = signal(0);

  /** Lösen. */
  readonly solveStyle = signal<EbookSolveStyle>('easy');
  readonly solveSource = signal<EbookSolveSource>('stored');
  readonly solveState = signal<'idle' | 'running' | 'solved' | 'failed'>('idle');
  /** Stockfish-Lösung (UCI), sobald gerechnet; `null` = noch nicht / läuft. */
  readonly engineLine = signal<string[] | null>(null);
  readonly engineLineBusy = signal(false);
  readonly showSolution = signal(false);
  private timer: ReturnType<typeof setTimeout> | null = null;

  /** Maia. */
  readonly maiaElo = signal<number>(MAIA_DEFAULT_ELO);
  readonly maiaEloOptions = MAIA_ELO_OPTIONS;
  readonly maiaThinking = signal(false);

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
    if (this.mode() === 'calc') {
      this.calcRevision();
      const node = this.calcTree ? findNode(this.calcTree, this.calcCursor()) : undefined;
      return node?.fen ?? d.fen;
    }
    return this.played().at(-1)?.fen ?? d.fen;
  });

  readonly playable = computed(() => {
    if (!this.legal()) return false;
    switch (this.mode()) {
      case 'view': return false;
      case 'solve': return this.solveState() === 'running' && this.isUserTurn();
      case 'maia': return !this.maiaThinking() && this.isUserTurn() && this.maia.status() === 'ready';
      default: return true;
    }
  });

  constructor() {
    // Engine rechnet jede neue Brettstellung (neuer Index = neue Stellung, keine eigene Nebenvariante).
    effect(() => {
      const s = this.engine();
      const fen = this.boardFen();
      if (s && fen) s.sync(++this.engineIndex, fen);
    });
    inject(DestroyRef).onDestroy(() => { this.clearTimer(); this.engine()?.destroy(); });
  }

  ngOnChanges(ch: SimpleChanges): void {
    if (!ch['diagram'] || !this.diagram) return;
    const prev = ch['diagram'].previousValue as EbookDiagram | undefined;
    if (prev?.id === this.diagram.id) { this.start.set(this.diagram); return; }   // nur Meldung o. Ä. geändert
    this.clearTimer();
    this.start.set(this.diagram);
    this.played.set([]);
    this.engineLine.set(null);
    this.showSolution.set(false);
    this.solveState.set('idle');
    this.maiaThinking.set(false);
    this.flipped.set(this.diagram.fen.split(' ')[1] === 'b');
    if (!this.legal() && this.mode() !== 'view') this.mode.set('view');
    if (!this.hasStoredSolution) this.solveSource.set('engine');
    if (this.mode() === 'calc') this.loadCalc();
    if (this.mode() === 'solve') this.startSolving();
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
  }

  /** Seite am Zug in der Buchstellung — die spielt man beim Lösen und gegen Maia. */
  get userColor(): 'w' | 'b' { return (this.diagram?.fen.split(' ')[1] === 'b' ? 'b' : 'w'); }

  private isUserTurn(): boolean {
    return (this.boardFen().split(' ')[1] ?? 'w') === this.userColor;
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
    }
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

  setSolveStyle(s: EbookSolveStyle): void {
    this.solveStyle.set(s);
    if (this.mode() === 'solve') this.startSolving();
  }

  startSolving(): void {
    this.clearTimer();
    this.played.set([]);
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
    if (idx >= line.length || this.solveState() !== 'running') return;
    const ok = uci === line[idx] || (line[idx].length === 4 && uci.startsWith(line[idx]));
    this.played.update(l => [...l, { san: m.san, fen: m.fen, uci }]);
    if (ok) { this.advance(); return; }
    if (this.solveStyle() === 'easy') {
      // Kurz zeigen, dann zurücknehmen — nochmal versuchen.
      this.snackbar.quick(this.translate.instant('ebooks.reader.notBookMove'));
      this.timer = setTimeout(() => { this.played.update(l => l.slice(0, -1)); this.cdr.markForCheck(); }, 600);
      return;
    }
    // Training: der erste Versuch zählt — falsch, dann spielt das Brett die Lösung vor.
    this.solveState.set('failed');
    this.timer = setTimeout(() => {
      this.played.update(l => l.slice(0, -1));
      this.playOutSolution();
    }, 700);
  }

  /** Nach einem richtigen Zug: Antwort aus der Linie nachziehen, am Ende „gelöst“. */
  private advance(): void {
    const line = this.solutionLine;
    if (this.played().length >= line.length) { this.solveState.set('solved'); return; }
    this.timer = setTimeout(() => {
      this.pushUci(line[this.played().length]);
      if (this.played().length >= line.length) this.solveState.set('solved');
      this.cdr.markForCheck();
    }, 450);
  }

  /** Training nach einem Fehler: die restliche Lösung Zug für Zug vorspielen. */
  private playOutSolution(): void {
    const line = this.solutionLine;
    if (this.played().length >= line.length) { this.cdr.markForCheck(); return; }
    this.pushUci(line[this.played().length]);
    this.cdr.markForCheck();
    this.timer = setTimeout(() => this.playOutSolution(), 700);
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
    let white = parts[1] !== 'b';
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
    return { view: 'visibility', free: 'pan_tool_alt', calc: 'account_tree', solve: 'extension', maia: 'smart_toy' }[m];
  }

  private clearTimer(): void {
    if (this.timer) { clearTimeout(this.timer); this.timer = null; }
  }
}
