import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { apiErrorText } from '../../core/api-error';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { parseAltMoves } from '../puzzles/alt-moves.util';
import { BasePuzzleSolver } from '../puzzles/base-puzzle-solver';
import { applyThemeMode } from '../puzzles/board-theme.util';
import { LongSolveService } from '../puzzles/long-solve.service';
import { PuzzleBoardComponent } from '../puzzles/puzzle-board.component';
import { BookPuzzleDto } from '../puzzles/puzzle.service';
import { PuzzleStatusCardComponent } from '../puzzles/puzzle-status-card.component';
import { StockfishService } from '../puzzles/stockfish.service';
import { daysLeft, WoodpeckerPlan, WoodpeckerService } from './woodpecker.service';

const STOCKFISH_DEPTH = 12;

/**
 * `/woodpecker/:planId/solve`: der Löser eines Woodpecker-Trainings. Der Server bestimmt die Aufgabe (Zyklus 1 in
 * Kursreihenfolge, danach zufällig aus dem Satz) und wertet je Zyklus nur den ersten Versuch — Wiederholen nach einem
 * Fehler ist erlaubt, ändert aber nichts mehr. Der Lös-Automat kommt aus {@link BasePuzzleSolver}, Brett und
 * Status-Karte sind dieselben wie in den anderen Puzzle-Modi.
 *
 * <p>Default-Change-Detection wie die übrigen Löser: der Automat der Basisklasse schreibt in gewöhnliche Felder.</p>
 */
/** Endzustände dieses Lösers (die übrigen setzt {@link BasePuzzleSolver}). */
export type WoodpeckerEndState = 'SOLVED' | 'FAILED';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-woodpecker-solve',
  standalone: true,
  imports: [RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatProgressBarModule, TranslatePipe,
    IconLabelDirective, LoadErrorComponent, LoadingSpinnerComponent, PuzzleBoardComponent, PuzzleStatusCardComponent],
  template: `
    <div class="page">
      <div class="top">
        <a mat-icon-button routerLink="/woodpecker" [appIconLabel]="'common.back' | translate"><mat-icon>arrow_back</mat-icon></a>
        <div class="top-text">
          <h1 [title]="plan()?.name ?? ''">{{ plan()?.name ?? ('woodpecker.title' | translate) }}</h1>
          @if (plan(); as p) {
            <div class="meta rh-dots">
              <span>{{ 'woodpecker.cycleOf' | translate: { n: p.currentCycle, max: p.maxCycles } }}</span>
              @if (p.currentCycle === 1) {
                <!-- d3-rk-wp-loeser-im-satz-beschriftung: derselbe Wortlaut wie auf der Übersichtskarte („Satz: 0 Aufgaben von
                     287“) statt „0 im Satz“. -->
                <span>{{ (p.setSize === 1 ? 'woodpecker.setGrowingOne' : 'woodpecker.setGrowing') | translate: { n: p.setSize, total: p.availableCount } }}</span>
              } @else {
                <span>{{ 'woodpecker.cycleProgress' | translate: { done: cycle()!.attempted, total: p.setSize } }}</span>
              }
              <span>{{ (left() === 1 ? 'woodpecker.daysLeftOne' : 'woodpecker.daysLeft') | translate: { days: left() } }}</span>
            </div>
          }
        </div>
      </div>
      @if (plan(); as p) {
        @if (p.currentCycle > 1) {
          <mat-progress-bar mode="determinate" [value]="p.setSize ? 100 * cycle()!.attempted / p.setSize : 0" />
        }
      }

      @if (phase() === 'loading') {
        <app-loading-spinner />
      } @else if (phase() === 'missing') {
        <!-- c1-wp-loeser-404-verbindung: fremdes/gelöschtes Training — „Wiederholen“ hülfe nie; Weg zurück statt
             Verbindungsfehler (DESIGN 5.10/5.11). -->
        <mat-card appearance="outlined" class="done missing">
          <p>{{ 'woodpecker.notFound' | translate }}</p>
          <a mat-flat-button color="primary" routerLink="/woodpecker">{{ 'woodpecker.toOverview' | translate }}</a>
        </mat-card>
      } @else if (phase() === 'error') {
        <app-load-error (retry)="loadNext()" />
      } @else if (phase() === 'done') {
        <mat-card appearance="outlined" class="done">
          <mat-icon class="done-icon">emoji_events</mat-icon>
          <p>{{ doneText() }}</p>
          <a mat-flat-button color="primary" routerLink="/woodpecker">{{ 'woodpecker.toOverview' | translate }}</a>
        </mat-card>
      } @else if (puzzle) {
        <div class="solver">
          <div class="board-section" [class.viz-hidden]="vizPiecesHidden && !vizShowPressed">
            <app-puzzle-board
              [fen]="boardFen"
              [actualFen]="actualFen"
              [orientation]="orientation"
              [turnColor]="turnColor"
              [dests]="dests"
              [lastMove]="lastMove"
              [viewOnly]="reviewMode || (state !== 'AWAITING_USER_MOVE' && state !== 'PLAYING' && state !== 'THINKING')"
              [premovable]="state === 'THINKING'"
              [check]="isCheck"
              [boardTheme]="boardTheme"
              [pieceSet]="pieceSet"
              [crazyPieceMode]="crazyPieceMode"
              [visualization]="(state !== 'SOLVED' && state !== 'FAILED') ? visualizationMode : 0"
              [vizOpponentArrow]="vizOpponentLastMove"
              (moveMade)="onMoveMade($event)" />
          </div>
          <div class="side">
            @if (puzzle.bookTitle || puzzle.chapter) {
              <p class="source">{{ puzzle.bookTitle }}@if (puzzle.chapter) { · {{ puzzle.chapterLabel ?? puzzle.chapter }}}</p>
            }
            <app-puzzle-status-card
              mode="book"
              [state]="state"
              [showEval]="showEval"
              [evalLoading]="evalLoading"
              [initialEval]="initialEval"
              [currentEval]="currentEval"
              [gaveUp]="gaveUp"
              [elapsedSeconds]="elapsedSeconds"
              [showMouseslip]="!mouseslipUsed && (!onSolutionPath || hasMadeFirstMove)"
              [hasMadeFirstMove]="hasMadeFirstMove"
              [alternativeSolve]="alternativeSolve"
              [solvedCountdown]="solvedCountdown"
              [reviewIndex]="reviewIndex"
              [reviewTotal]="reviewTotal"
              (evalToggled)="toggleEval()"
              (resetClicked)="resetPuzzle()"
              (mouseslipClicked)="mouseslip()"
              (giveUpClicked)="giveUp()"
              (retryClicked)="retry()"
              (nextClicked)="next()"
              (failedNextClicked)="next()"
              (analyzeClicked)="analyze()"
              (reviewPrev)="reviewPrev()"
              (reviewNext)="reviewNext()"
              (exitReviewClicked)="exitReview()"
              (showOriginalSolutionClicked)="showOriginalSolution()" />
            @if (repeatHint()) {
              <p class="repeat">{{ 'woodpecker.repeatHint' | translate }}</p>
            }
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    /* c1-wp-woodpecker-seitenpolster-96vw: ohne 96vw — der Rand ist das 16-px-Polster (DESIGN 3.2). */
    .page { max-width: var(--page-max-width); margin: 0 auto; padding: 8px 16px 16px; box-sizing: border-box; }
    .top { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
    .top-text { flex: 1 1 auto; min-width: 0; }
    /* d4-rk-woodpecker-solve-titel-abgeschnitten: der Seitenkopf bricht um (DESIGN 5.1), einzeilig mit „…" nur in
       Listenkarten (5.2). Trenner „·" über das geteilte .rh-dots (styles.scss, 5.13): nie am Zeilenanfang. */
    .top h1 { margin: 0; font-size: 1.2rem; overflow-wrap: anywhere; text-wrap: balance; }
    .meta { row-gap: 2px; font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
    .meta > span { white-space: nowrap; }
    @media (max-width: 599.98px) { .top h1 { font-size: 1.05rem; } }
    mat-progress-bar { margin-bottom: 12px; }
    .solver { display: grid; grid-template-columns: 1fr; gap: 16px; }
    .board-section { width: min(100%, calc(100vh - 180px)); margin: 0 auto; }
    .side { display: flex; flex-direction: column; gap: 8px; min-width: 0; }
    .source { margin: 0; font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
    .repeat { margin: 0; font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
    .done { padding: 2rem 1rem; text-align: center; display: flex; flex-direction: column; align-items: center; gap: 8px; }
    .done-icon { font-size: 40px; width: 40px; height: 40px; color: var(--rh-accent); }
    @media (min-width: 900px) {
      .solver { grid-template-columns: minmax(0, 1fr) minmax(360px, 400px); align-items: start; }
      .board-section { width: min(100%, calc(100vh - 160px)); }
    }
    /* c1-wp-loeser-ultrawide-brett: Breitbild-Stufe wie die übrigen Puzzle-Löser (DESIGN 4.1/5.12) — Seite im breiten
       Token, Brett bis Fensterhöhe, daneben 360–400 px Seitenspalte, beides mittig. Unter 2400 px unverändert. */
    @media (min-width: 2400px) {
      .page { max-width: min(var(--page-max-width-wide, 1880px), 96vw); }
      .solver { justify-content: center; grid-template-columns: minmax(0, calc(100vh - 160px)) minmax(360px, 400px); }
      .board-section { width: min(100%, calc(100vh - 160px)); }
      /* Abschluss-/Fehlkarte bleibt bei der Seitenbreite (DESIGN 4.1). */
      .done { max-width: var(--page-max-width); margin-inline: auto; box-sizing: border-box; }
    }
  `],
})
export class WoodpeckerSolveComponent extends BasePuzzleSolver<WoodpeckerEndState> implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private service = inject(WoodpeckerService);
  private prefs = inject(PreferencesService);
  private longSolve = inject(LongSolveService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  readonly plan = signal<WoodpeckerPlan | null>(null);
  /** `missing`: Training gibt es nicht (mehr) oder gehört jemand anderem (404/403) — kein Verbindungsfehler. */
  readonly phase = signal<'loading' | 'error' | 'missing' | 'solving' | 'done'>('loading');
  /** Nach einem Wiederholen: der erste Versuch ist schon gewertet. */
  readonly repeatHint = signal(false);

  puzzle: BookPuzzleDto | null = null;
  boardTheme = 'brown';
  pieceSet = 'cburnett';
  gaveUp = false;
  private planId = 0;
  /** Dieser Aufgabe wurde schon ein Versuch gemeldet (der Server wertet ohnehin nur den ersten). */
  private recorded = false;
  private loadEpoch = 0;

  constructor() {
    super(inject(StockfishService));
    this.stockfish.init().catch(() => {});
  }

  cycle() { const p = this.plan(); return p ? p.cycles[p.cycles.length - 1] : null; }
  left(): number { const c = this.cycle(); return c ? daysLeft(c.deadline) : 0; }

  ngOnInit(): void {
    this.planId = Number(this.route.snapshot.paramMap.get('planId'));
    this.loadNext();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.stopTimer();
    this.releaseSolver();   // bricht auch Countdown und Lösungs-Wiedergabe ab
  }

  loadNext(): void {
    const epoch = ++this.loadEpoch;
    this.phase.set('loading');
    this.service.next(this.planId).subscribe({
      next: r => {
        if (epoch !== this.loadEpoch || this.destroyed) return;
        this.plan.set(r.plan);
        if (!r.puzzle) { this.puzzle = null; this.phase.set('done'); return; }
        this.puzzle = r.puzzle;
        this.recorded = false;
        this.repeatHint.set(false);
        this.phase.set('solving');
        this.setupPuzzle(r.puzzle);
      },
      error: (e: HttpErrorResponse) => {
        if (epoch !== this.loadEpoch) return;
        this.phase.set(e?.status === 404 || e?.status === 403 ? 'missing' : 'error');
      },
    });
  }

  /** Abschlusstext je nach Stand: Methode fertig, Pause, oder (Zyklus 1) alles gelöst. */
  doneText(): string {
    const p = this.plan();
    if (!p) return '';
    if (p.status === 'completed') return this.translate.instant('woodpecker.doneCompleted');
    return this.translate.instant('woodpecker.doneCycle', { n: p.currentCycle });
  }

  next(): void {
    this.stopCountdown();
    this.loadNext();
  }

  private setupPuzzle(p: BookPuzzleDto): void {
    this.gaveUp = false;
    this.reviewMode = false;
    this.reviewIndex = 0;
    this.hintLevel = 0;
    this.initialEval = '';
    this.setupSolver(p.fen, p.moves, p.startPly ?? 0);
    // Geduldete Alternativzüge NACH setupSolver setzen (das leert sie beim Aufsetzen).
    this.altMovesByPly = parseAltMoves(p.altMoves);
  }

  // ===== Hooks für BasePuzzleSolver =====
  protected override get depth(): number { return STOCKFISH_DEPTH; }

  protected override onSetupStart(): void {
    const applied = applyThemeMode(this.themeMode, this.prefs.boardTheme, this.prefs.pieceSet);
    this.boardTheme = applied.boardTheme;
    this.pieceSet = applied.pieceSet;
  }

  protected override onSolvingBegins(): void {
    this.startTimer();
  }

  protected override handleSolved(alternative: boolean): void {
    this.state = 'SOLVED';
    this.stopTimer();
    this.updateBoard();
    this.enterSolutionReview();
    // Auffällig lange Lösezeit (Tab lag offen) → nachfragen, bevor gewertet wird.
    this.longSolve.resolve(this.elapsedSeconds).subscribe(seconds => {
      if (this.destroyed) return;
      this.record(true, seconds);
      if (alternative) return;   // eigene Lösung: der Spieler entscheidet selbst
      this.startSolvedCountdown(() => this.next());
    });
  }

  /** P-1: der erste Fehlzug ist der gewertete Fehlversuch (der Server wertet ohnehin nur den ersten Versuch). */
  protected override onFirstWrongMove(): void {
    this.record(false, this.elapsedSeconds);
  }

  protected override handleFailed(): void {
    this.state = 'FAILED';
    this.stopTimer();
    this.updateBoard();
    this.enterSolutionReview();
    this.record(false, this.elapsedSeconds);
  }

  override get reviewTotal(): number {
    return this.puzzle ? this.puzzle.moves.split(' ').filter(m => m).length : 0;
  }

  protected override reviewGoTo(index: number): void {
    if (!this.puzzle) return;
    this.reviewGoToCore(this.puzzle.fen, this.puzzle.moves.split(' ').filter(m => m), index);
  }

  // ===== Aktionen der Status-Karte =====
  giveUp(): void {
    if (!this.puzzle) return;
    this.abortSolver();
    this.stopTimer();
    this.gaveUp = true;
    this.state = 'FAILED';
    this.record(false, this.elapsedSeconds);
    this.playSolutionFromStart();
  }

  retry(): void {
    if (!this.puzzle) return;
    this.clearSolutionPlay();
    this.repeatHint.set(this.recorded);
    this.setupPuzzle(this.puzzle);
  }

  resetPuzzle(): void {
    if (!this.puzzle) return;
    // Zurücksetzen nach einem Zug zählt wie ein Fehlversuch — sonst ließe sich jede Aufgabe „gratis“ neu beginnen.
    if (this.moveLog.length > 0 && this.state !== 'FAILED' && this.state !== 'SOLVED') this.record(false, this.elapsedSeconds);
    this.aborted = true;
    this.retry();
  }

  analyze(): void {
    if (!this.puzzle) return;
    const moves = (this.puzzle.moves || '').split(' ').filter(m => m);
    this.router.navigate(['/analysis'], {
      queryParams: { fen: this.puzzle.fen, moves: moves.join(','), orientation: this.orientation, from: this.router.url },
    });
  }

  /** Meldet den Versuch einmal je Aufgabe; der Fortschritt im Kopf kommt aus der Antwort. */
  private record(solved: boolean, seconds: number): void {
    if (!this.puzzle || this.recorded) return;
    this.recorded = true;
    this.service.recordAttempt(this.planId, this.puzzle.id, solved, seconds).subscribe({
      next: plan => this.plan.set(plan),
      error: err => {
        this.recorded = false;
        this.snackbar.warn(apiErrorText(err, this.translate, 'woodpecker.saveFailed'));
      },
    });
  }
}
